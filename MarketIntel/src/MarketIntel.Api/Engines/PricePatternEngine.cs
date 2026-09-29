using MarketIntel.Api.Localization;
using MarketIntel.Shared.Contracts;
using MarketIntel.Shared.Enums;

namespace MarketIntel.Api.Engines;

/// <summary>
/// Detects price-action patterns (candlesticks and breakouts) on the latest bar, and — crucially —
/// measures each pattern's EMPIRICAL success rate over the symbol's own history rather than
/// assuming a pattern is bullish/bearish by name. This is a lightweight backtest per pattern.
/// </summary>
public sealed class PricePatternEngine
{
    private const int Lookback = 20;   // breakout window
    private const int Horizon = 5;     // bars ahead used to judge success

    // NameKey is a resource key; the display name is localized per request in Compute.
    private sealed record Detector(
        string NameKey,
        FactorBias Bias,
        Func<IReadOnlyList<CandleDto>, int, bool> Match);

    private static readonly Detector[] Detectors =
    [
        new("Pat.BullEngulf", FactorBias.Bullish, IsBullishEngulfing),
        new("Pat.BearEngulf", FactorBias.Bearish, IsBearishEngulfing),
        new("Pat.Hammer", FactorBias.Bullish, IsHammer),
        new("Pat.ShootingStar", FactorBias.Bearish, IsShootingStar),
        new("Pat.Breakout", FactorBias.Bullish, IsBreakout),
        new("Pat.Breakdown", FactorBias.Bearish, IsBreakdown),
    ];

    public IReadOnlyList<PatternDto> Compute(IReadOnlyList<CandleDto> candles)
    {
        int n = candles.Count;
        if (n < Lookback + Horizon + 3) return [];

        var result = new List<PatternDto>();
        int last = n - 1;

        foreach (var d in Detectors)
        {
            if (!d.Match(candles, last)) continue;

            var (winRate, sample) = HistoricalWinRate(candles, d);
            string dirWord = d.Bias == FactorBias.Bullish ? Loc.T("Word.WentUp") : Loc.T("Word.WentDown");
            string detail = sample >= 4
                ? Loc.T("Pat.Detail.Hist", dirWord, Math.Round(winRate * 100), sample, Horizon)
                : Loc.T("Pat.Detail.Insufficient", sample);

            result.Add(new PatternDto(Loc.T(d.NameKey), d.Bias, Loc.T("Pat.TfLabel"), Math.Round(winRate, 2), sample, detail));
        }

        return result;
    }

    /// <summary>Empirical win rate: over all prior occurrences, how often the expected move happened.</summary>
    private static (double Rate, int Sample) HistoricalWinRate(IReadOnlyList<CandleDto> candles, Detector d)
    {
        int n = candles.Count, wins = 0, total = 0;
        for (int i = Lookback; i < n - Horizon; i++)
        {
            if (!d.Match(candles, i)) continue;
            total++;
            double now = (double)candles[i].Close;
            double later = (double)candles[i + Horizon].Close;
            bool win = d.Bias == FactorBias.Bullish ? later > now : later < now;
            if (win) wins++;
        }
        return total == 0 ? (0.0, 0) : ((double)wins / total, total);
    }

    // ---------- candle geometry helpers ----------

    private static double Body(CandleDto c) => Math.Abs((double)(c.Close - c.Open));
    private static double Range(CandleDto c) => (double)(c.High - c.Low);
    private static double UpperShadow(CandleDto c) => (double)(c.High - Math.Max(c.Open, c.Close));
    private static double LowerShadow(CandleDto c) => (double)(Math.Min(c.Open, c.Close) - c.Low);
    private static bool IsBull(CandleDto c) => c.Close > c.Open;
    private static bool IsBear(CandleDto c) => c.Close < c.Open;

    // ---------- detectors ----------

    private static bool IsBullishEngulfing(IReadOnlyList<CandleDto> c, int i)
    {
        if (i < 1) return false;
        var prev = c[i - 1];
        var cur = c[i];
        return IsBear(prev) && IsBull(cur) &&
               cur.Open <= prev.Close && cur.Close >= prev.Open &&
               Body(cur) > Body(prev);
    }

    private static bool IsBearishEngulfing(IReadOnlyList<CandleDto> c, int i)
    {
        if (i < 1) return false;
        var prev = c[i - 1];
        var cur = c[i];
        return IsBull(prev) && IsBear(cur) &&
               cur.Open >= prev.Close && cur.Close <= prev.Open &&
               Body(cur) > Body(prev);
    }

    private static bool IsHammer(IReadOnlyList<CandleDto> c, int i)
    {
        if (i < 3) return false;
        var cur = c[i];
        double range = Range(cur), body = Body(cur);
        if (range <= 0) return false;
        bool shape = body <= 0.35 * range && LowerShadow(cur) >= 2 * body && UpperShadow(cur) <= body;
        bool priorDrop = (double)c[i - 1].Close < (double)c[i - 3].Close; // context: recent weakness
        return shape && priorDrop;
    }

    private static bool IsShootingStar(IReadOnlyList<CandleDto> c, int i)
    {
        if (i < 3) return false;
        var cur = c[i];
        double range = Range(cur), body = Body(cur);
        if (range <= 0) return false;
        bool shape = body <= 0.35 * range && UpperShadow(cur) >= 2 * body && LowerShadow(cur) <= body;
        bool priorRise = (double)c[i - 1].Close > (double)c[i - 3].Close; // context: recent strength
        return shape && priorRise;
    }

    private static bool IsBreakout(IReadOnlyList<CandleDto> c, int i)
    {
        if (i < Lookback) return false;
        double priorHigh = double.MinValue;
        for (int k = i - Lookback; k < i; k++) priorHigh = Math.Max(priorHigh, (double)c[k].High);
        return (double)c[i].Close > priorHigh;
    }

    private static bool IsBreakdown(IReadOnlyList<CandleDto> c, int i)
    {
        if (i < Lookback) return false;
        double priorLow = double.MaxValue;
        for (int k = i - Lookback; k < i; k++) priorLow = Math.Min(priorLow, (double)c[k].Low);
        return (double)c[i].Close < priorLow;
    }
}
