using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using MarketIntel.Shared.Contracts;
using Microsoft.Extensions.Options;

namespace MarketIntel.Api.MarketData;

/// <summary>
/// Real market data from Alpaca (https://alpaca.markets). Implements the same
/// <see cref="IMarketDataProvider"/> contract as the mock, so nothing else in the platform
/// changes. Free accounts use the IEX feed (15-minute delayed); paid plans can use SIP.
/// </summary>
public sealed class AlpacaMarketDataProvider : IMarketDataProvider
{
    private readonly HttpClient _http;
    private readonly AlpacaOptions _options;

    public AlpacaMarketDataProvider(HttpClient http, IOptions<AlpacaOptions> options)
    {
        _options = options.Value;
        _http = http;
        _http.BaseAddress = new Uri(_options.DataBaseUrl);
        _http.DefaultRequestHeaders.Add("APCA-API-KEY-ID", _options.ApiKeyId);
        _http.DefaultRequestHeaders.Add("APCA-API-SECRET-KEY", _options.ApiSecretKey);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public string Name => $"Alpaca ({_options.Feed})";
    public bool IsSynthetic => false;

    // A spread of equities, ETFs and crypto. A production scanner would pull this from a screener.
    public Task<IReadOnlyList<string>> GetUniverseAsync(CancellationToken ct = default)
        => Task.FromResult(AssetClass.DefaultUniverse);

    public async Task<IReadOnlyList<CandleDto>> GetCandlesAsync(
        string symbol, Timeframe timeframe, int count, CancellationToken ct = default)
    {
        // Crypto lives on a different Alpaca API (24/7, no feed, pair symbology).
        if (AssetClass.IsCrypto(symbol))
            return await GetCryptoCandlesAsync(symbol, timeframe, count, ct);

        string sym = MapSymbol(symbol);
        string tf = ToAlpacaTimeframe(timeframe);

        // Look back generously (markets are closed nights/weekends) and take the newest `count`.
        int bufferFactor = timeframe is Timeframe.D1 or Timeframe.W1 ? 2 : 5;
        DateTime start = DateTime.UtcNow - timeframe.ToTimeSpan() * count * bufferFactor;
        string startStr = start.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        string url = $"/v2/stocks/{Uri.EscapeDataString(sym)}/bars" +
                     $"?timeframe={tf}&limit={count}&feed={_options.Feed}" +
                     $"&adjustment=raw&sort=desc&start={startStr}";

        using var resp = await _http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var result = new List<CandleDto>(count);
        if (doc.RootElement.TryGetProperty("bars", out var bars) && bars.ValueKind == JsonValueKind.Array)
        {
            foreach (var b in bars.EnumerateArray())
                result.Add(ParseBar(b));
        }

        // We requested newest-first; the engines expect oldest-first.
        result.Reverse();
        return result;
    }

    /// <summary>Crypto bars via Alpaca's v1beta3 crypto API. Response is keyed by the pair symbol.</summary>
    private async Task<IReadOnlyList<CandleDto>> GetCryptoCandlesAsync(
        string symbol, Timeframe timeframe, int count, CancellationToken ct)
    {
        string pair = AssetClass.CryptoPair(symbol); // e.g. BTC/USD
        string tf = ToAlpacaTimeframe(timeframe);
        // Crypto trades 24/7, so a small buffer is enough to cover the requested window.
        DateTime start = DateTime.UtcNow - timeframe.ToTimeSpan() * count * 2;
        string startStr = start.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        string url = $"/v1beta3/crypto/us/bars?symbols={Uri.EscapeDataString(pair)}" +
                     $"&timeframe={tf}&limit={count}&sort=desc&start={startStr}";

        using var resp = await _http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var result = new List<CandleDto>(count);
        if (doc.RootElement.TryGetProperty("bars", out var barsObj) && barsObj.ValueKind == JsonValueKind.Object
            && barsObj.TryGetProperty(pair, out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var b in arr.EnumerateArray())
                result.Add(ParseBar(b));
        }

        result.Reverse(); // newest-first -> oldest-first
        return result;
    }

    private static CandleDto ParseBar(JsonElement b) => new(
        TimeUtc: DateTimeOffset.Parse(b.GetProperty("t").GetString()!, CultureInfo.InvariantCulture).UtcDateTime,
        Open: b.GetProperty("o").GetDecimal(),
        High: b.GetProperty("h").GetDecimal(),
        Low: b.GetProperty("l").GetDecimal(),
        Close: b.GetProperty("c").GetDecimal(),
        Volume: b.GetProperty("v").GetDecimal());

    public async Task<decimal> GetLastPriceAsync(string symbol, CancellationToken ct = default)
    {
        try
        {
            if (AssetClass.IsCrypto(symbol))
            {
                string pair = AssetClass.CryptoPair(symbol);
                string curl = $"/v1beta3/crypto/us/latest/trades?symbols={Uri.EscapeDataString(pair)}";
                using var cresp = await _http.GetAsync(curl, ct);
                cresp.EnsureSuccessStatusCode();
                await using var cstream = await cresp.Content.ReadAsStreamAsync(ct);
                using var cdoc = await JsonDocument.ParseAsync(cstream, cancellationToken: ct);
                if (cdoc.RootElement.TryGetProperty("trades", out var trades)
                    && trades.TryGetProperty(pair, out var t) && t.TryGetProperty("p", out var cp))
                    return cp.GetDecimal();
            }
            else
            {
                string sym = MapSymbol(symbol);
                string url = $"/v2/stocks/{Uri.EscapeDataString(sym)}/trades/latest?feed={_options.Feed}";
                using var resp = await _http.GetAsync(url, ct);
                resp.EnsureSuccessStatusCode();
                await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                if (doc.RootElement.TryGetProperty("trade", out var trade) &&
                    trade.TryGetProperty("p", out var p))
                    return p.GetDecimal();
            }
        }
        catch
        {
            // Fall back to the latest bar close below.
        }

        var candles = await GetCandlesAsync(symbol, Timeframe.M1, 1, ct);
        return candles.Count > 0 ? candles[^1].Close : 0m;
    }

    /// <summary>The VIX index is not on the equities data API; use the VIXY ETF as a proxy.</summary>
    private static string MapSymbol(string symbol)
        => symbol.ToUpperInvariant() == "VIX" ? "VIXY" : symbol.ToUpperInvariant();

    private static string ToAlpacaTimeframe(Timeframe tf) => tf switch
    {
        Timeframe.M1 => "1Min",
        Timeframe.M5 => "5Min",
        Timeframe.M15 => "15Min",
        Timeframe.M30 => "30Min",
        Timeframe.H1 => "1Hour",
        Timeframe.H4 => "4Hour",
        Timeframe.D1 => "1Day",
        Timeframe.W1 => "1Week",
        _ => "1Hour"
    };
}
