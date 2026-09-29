using MarketIntel.Shared.Contracts;

namespace MarketIntel.Api.MarketData;

/// <summary>
/// Deterministic synthetic market-data provider. Generates plausible OHLCV series
/// (trends, ranges, volume spikes) so the whole platform runs end-to-end before a real
/// data licence is wired in. Data is NOT tradeable — <see cref="IsSynthetic"/> is true.
/// </summary>
public sealed class MockMarketDataProvider : IMarketDataProvider
{
    public string Name => "Synthetic (mock)";
    public bool IsSynthetic => true;

    // Symbol -> starting price and character. Benchmarks are included so the market-regime
    // engine has SPY/QQQ/IWM/DIA/VIX to look at, plus ETFs and crypto for those asset classes.
    private static readonly IReadOnlyDictionary<string, decimal> BasePrices = new Dictionary<string, decimal>
    {
        ["NVDA"] = 178m, ["TSLA"] = 245m, ["AAPL"] = 228m, ["AMD"] = 168m, ["META"] = 592m,
        ["MSFT"] = 431m, ["GOOGL"] = 178m, ["AMZN"] = 201m, ["NFLX"] = 705m, ["AVGO"] = 172m,
        ["SPY"] = 574m, ["QQQ"] = 502m, ["IWM"] = 221m, ["DIA"] = 431m, ["VIX"] = 15.4m,
        ["GLD"] = 245m, ["TLT"] = 92m, ["XLK"] = 231m, ["XLE"] = 92m, ["SMH"] = 245m, ["ARKK"] = 62m,
        // Crypto (slash-free canonical form)
        ["BTCUSD"] = 96000m, ["ETHUSD"] = 3400m, ["SOLUSD"] = 195m,
        ["DOGEUSD"] = 0.38m, ["AVAXUSD"] = 38m, ["LINKUSD"] = 22m,
    };

    public Task<IReadOnlyList<string>> GetUniverseAsync(CancellationToken ct = default)
        => Task.FromResult(AssetClass.DefaultUniverse);

    public Task<decimal> GetLastPriceAsync(string symbol, CancellationToken ct = default)
    {
        var candles = Generate(symbol, Timeframe.M1, 200);
        return Task.FromResult(candles[^1].Close);
    }

    public Task<IReadOnlyList<CandleDto>> GetCandlesAsync(
        string symbol, Timeframe timeframe, int count, CancellationToken ct = default)
        => Task.FromResult(Generate(symbol, timeframe, count));

    private static IReadOnlyList<CandleDto> Generate(string symbol, Timeframe tf, int count)
    {
        symbol = symbol.ToUpperInvariant();
        decimal basePrice = BasePrices.TryGetValue(symbol, out var p) ? p : 100m;

        // Stable seed: string.GetHashCode is randomised per process, so use our own hash.
        // Bucket by UTC date so the series evolves day to day but is stable within a run.
        int dayBucket = (int)(DateTime.UtcNow.Date.ToBinary() % int.MaxValue);
        int seed = StableHash($"{symbol}|{tf}|{dayBucket}");
        var rng = new Random(seed);

        // Per-symbol character.
        double annualDrift = (StableHash(symbol) % 1000 / 1000.0 - 0.35) * 0.6; // -0.21..+0.39
        double annualVol = 0.25 + (StableHash(symbol + "v") % 1000 / 1000.0) * 0.55; // 0.25..0.80
        if (symbol == "VIX") { annualDrift = 0; annualVol = 1.4; }

        // Convert annual figures to per-bar figures.
        double dt = 1.0 / tf.BarsPerYear();
        double muBar = annualDrift * dt;
        double sigBar = annualVol * Math.Sqrt(dt);

        // A slow trend regime so the series has trending and ranging stretches.
        double regimePhase = (StableHash(symbol + tf) % 628) / 100.0; // 0..2π
        double regimeAmp = muBar * 4;

        var closes = new double[count];
        double price = (double)basePrice;
        // Walk backwards from a point so the LAST bar lands near basePrice-ish then drifts.
        for (int i = 0; i < count; i++)
        {
            double z = NextGaussian(rng);
            double trend = muBar + regimeAmp * Math.Sin(regimePhase + i * 0.05);
            double ret = trend + sigBar * z;
            price *= (1 + ret);
            if (price < 0.5) price = 0.5;
            closes[i] = price;
        }

        var candles = new List<CandleDto>(count);
        var span = tf.ToTimeSpan();
        DateTime start = DateTime.UtcNow - span * count;
        double baseVolume = symbol == "VIX" ? 0 : 500_000 + (StableHash(symbol + "vol") % 3000) * 1000;

        for (int i = 0; i < count; i++)
        {
            double close = closes[i];
            double open = i == 0 ? close * (1 - sigBar * 0.3) : closes[i - 1];
            double intrabar = close * sigBar * (0.3 + rng.NextDouble() * 0.5);
            double high = Math.Max(open, close) + intrabar * rng.NextDouble();
            double low = Math.Min(open, close) - intrabar * rng.NextDouble();

            // Volume: baseline noise plus occasional spikes (relative-volume events).
            double volNoise = 0.6 + rng.NextDouble() * 0.9;
            double spike = rng.NextDouble() < 0.08 ? 1.8 + rng.NextDouble() * 2.0 : 1.0;
            decimal volume = (decimal)Math.Round(baseVolume * volNoise * spike);

            candles.Add(new CandleDto(
                TimeUtc: start + span * i,
                Open: Round(open),
                High: Round(high),
                Low: Round(low),
                Close: Round(close),
                Volume: volume));
        }

        return candles;
    }

    private static decimal Round(double v) => Math.Round((decimal)v, 2);

    /// <summary>Box-Muller standard normal.</summary>
    private static double NextGaussian(Random rng)
    {
        double u1 = 1.0 - rng.NextDouble();
        double u2 = 1.0 - rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }

    /// <summary>Deterministic FNV-1a hash (process-stable, unlike string.GetHashCode).</summary>
    private static int StableHash(string s)
    {
        unchecked
        {
            const uint fnvPrime = 16777619;
            uint hash = 2166136261;
            foreach (char c in s)
            {
                hash ^= c;
                hash *= fnvPrime;
            }
            return (int)(hash & 0x7FFFFFFF);
        }
    }
}
