using MarketIntel.Api.Localization;
using MarketIntel.Shared.Contracts;
using MarketIntel.Shared.Enums;

namespace MarketIntel.Api.Engines;

/// <summary>
/// Reads market structure from swing points: labels each swing HH/HL/LH/LL, infers the trend
/// from the sequence, detects ranging/consolidation, and flags a break of structure (BOS).
/// </summary>
public sealed class MarketStructureEngine
{
    private const int Window = 3;
    private const int RangeLookback = 20;
    private const int MaxSwings = 6;

    public MarketStructureDto? Compute(IReadOnlyList<CandleDto> candles, decimal price, double atr)
    {
        int n = candles.Count;
        if (n < Window * 2 + 5) return null;

        // 1. Collect swing points chronologically.
        var swings = new List<(double Price, bool IsHigh, DateTime T)>();
        for (int i = Window; i < n - Window; i++)
        {
            if (IsSwingHigh(candles, i)) swings.Add(((double)candles[i].High, true, candles[i].TimeUtc));
            else if (IsSwingLow(candles, i)) swings.Add(((double)candles[i].Low, false, candles[i].TimeUtc));
        }
        if (swings.Count < 2) return null;

        // 2. Label each swing against the previous same-type swing.
        double? prevHigh = null, prevLow = null;
        var labeled = new List<SwingPointDto>();
        foreach (var s in swings)
        {
            SwingLabel label;
            if (s.IsHigh)
            {
                label = prevHigh is null || s.Price >= prevHigh ? SwingLabel.HigherHigh : SwingLabel.LowerHigh;
                prevHigh = s.Price;
            }
            else
            {
                label = prevLow is null || s.Price >= prevLow ? SwingLabel.HigherLow : SwingLabel.LowerLow;
                prevLow = s.Price;
            }
            labeled.Add(new SwingPointDto(label, Round((decimal)s.Price), s.T));
        }

        var lastHigh = labeled.LastOrDefault(l => l.Label is SwingLabel.HigherHigh or SwingLabel.LowerHigh);
        var lastLow = labeled.LastOrDefault(l => l.Label is SwingLabel.HigherLow or SwingLabel.LowerLow);

        // 3. Range detection over the recent window.
        double hi = double.MinValue, lo = double.MaxValue;
        for (int i = Math.Max(0, n - RangeLookback); i < n; i++)
        {
            hi = Math.Max(hi, (double)candles[i].High);
            lo = Math.Min(lo, (double)candles[i].Low);
        }
        bool inRange = atr > 0 && (hi - lo) <= atr * 3.5;

        // 4. Trend from the latest high/low labels (a language-independent kind, then localized text).
        bool isBull = lastHigh?.Label == SwingLabel.HigherHigh && lastLow?.Label == SwingLabel.HigherLow;
        bool isBear = lastHigh?.Label == SwingLabel.LowerHigh && lastLow?.Label == SwingLabel.LowerLow;

        string trend =
            inRange ? Loc.T("Struct.Range") :
            isBull ? Loc.T("Struct.BullTrend") :
            isBear ? Loc.T("Struct.BearTrend") :
            Loc.T("Struct.Mixed");

        FactorBias trendBias = inRange ? FactorBias.Neutral
            : isBull ? FactorBias.Bullish
            : isBear ? FactorBias.Bearish
            : FactorBias.Neutral;

        // 5. Break of structure: current close beyond the most recent swing.
        string? bos = null;
        var lastSwingHigh = swings.Where(s => s.IsHigh).Select(s => (double?)s.Price).LastOrDefault();
        var lastSwingLow = swings.Where(s => !s.IsHigh).Select(s => (double?)s.Price).LastOrDefault();
        double close = (double)price;
        if (lastSwingHigh is double h && close > h) bos = Loc.T("Struct.BOS.Up");
        else if (lastSwingLow is double l && close < l) bos = Loc.T("Struct.BOS.Down");

        string desc =
            inRange ? Loc.T("Struct.Desc.Range", lo, hi) :
            isBull ? Loc.T("Struct.Desc.Bull") :
            isBear ? Loc.T("Struct.Desc.Bear") :
            Loc.T("Struct.Desc.None");
        if (bos is not null) desc += " " + bos;

        var display = labeled.TakeLast(MaxSwings).ToList();
        return new MarketStructureDto(
            trend, desc, display, inRange,
            inRange ? Round((decimal)lo) : null,
            inRange ? Round((decimal)hi) : null,
            trendBias);
    }

    private static bool IsSwingHigh(IReadOnlyList<CandleDto> c, int i)
    {
        double h = (double)c[i].High;
        for (int k = 1; k <= Window; k++)
            if (h < (double)c[i - k].High || h < (double)c[i + k].High) return false;
        return true;
    }

    private static bool IsSwingLow(IReadOnlyList<CandleDto> c, int i)
    {
        double lo = (double)c[i].Low;
        for (int k = 1; k <= Window; k++)
            if (lo > (double)c[i - k].Low || lo > (double)c[i + k].Low) return false;
        return true;
    }

    private static decimal Round(decimal v) => Math.Round(v, 2);
}
