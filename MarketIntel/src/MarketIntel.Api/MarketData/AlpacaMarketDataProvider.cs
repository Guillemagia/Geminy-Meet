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

    // Same universe as the mock for now. A production scanner would pull this from a screener.
    private static readonly string[] Universe =
        ["NVDA", "TSLA", "AAPL", "AMD", "META", "MSFT", "GOOGL", "AMZN", "NFLX", "AVGO"];

    public Task<IReadOnlyList<string>> GetUniverseAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>(Universe);

    public async Task<IReadOnlyList<CandleDto>> GetCandlesAsync(
        string symbol, Timeframe timeframe, int count, CancellationToken ct = default)
    {
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
            {
                result.Add(new CandleDto(
                    TimeUtc: DateTimeOffset.Parse(b.GetProperty("t").GetString()!, CultureInfo.InvariantCulture).UtcDateTime,
                    Open: b.GetProperty("o").GetDecimal(),
                    High: b.GetProperty("h").GetDecimal(),
                    Low: b.GetProperty("l").GetDecimal(),
                    Close: b.GetProperty("c").GetDecimal(),
                    Volume: b.GetProperty("v").GetDecimal()));
            }
        }

        // We requested newest-first; the engines expect oldest-first.
        result.Reverse();
        return result;
    }

    public async Task<decimal> GetLastPriceAsync(string symbol, CancellationToken ct = default)
    {
        string sym = MapSymbol(symbol);
        string url = $"/v2/stocks/{Uri.EscapeDataString(sym)}/trades/latest?feed={_options.Feed}";
        try
        {
            using var resp = await _http.GetAsync(url, ct);
            resp.EnsureSuccessStatusCode();
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (doc.RootElement.TryGetProperty("trade", out var trade) &&
                trade.TryGetProperty("p", out var p))
                return p.GetDecimal();
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
