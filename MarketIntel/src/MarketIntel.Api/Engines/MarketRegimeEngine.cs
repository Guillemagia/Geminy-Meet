using MarketIntel.Api.Localization;
using MarketIntel.Shared.Contracts;
using MarketIntel.Shared.Enums;

namespace MarketIntel.Api.Engines;

/// <summary>
/// Builds overall market context from benchmark snapshots (SPY, QQQ, IWM, DIA, VIX) and a
/// scalar bias in [-1, 1] that every per-symbol signal folds in. A great NVDA setup is worth
/// less when QQQ is falling hard.
/// </summary>
public sealed class MarketRegimeEngine
{
    private static readonly (string Symbol, string Name, double Weight)[] Benchmarks =
    [
        ("SPY", "S&P 500", 0.40),
        ("QQQ", "Nasdaq 100", 0.40),
        ("IWM", "Russell 2000", 0.10),
        ("DIA", "Dow Jones", 0.05),
    ];

    public (MarketRegimeDto Regime, double Bias) Build(IReadOnlyDictionary<string, IndicatorSnapshot> indexSnaps)
    {
        var indices = new List<IndexSnapshotDto>();
        double bias = 0;

        foreach (var (sym, name, weight) in Benchmarks)
        {
            if (!indexSnaps.TryGetValue(sym, out var s)) continue;
            double b = Math.Clamp(0.6 * EmaTrend(s) + 0.4 * Momentum(s), -1, 1);
            bias += weight * b;
            double bull = 0.5 + 0.5 * b;
            indices.Add(new IndexSnapshotDto(sym, name,
                b > 0.1 ? FactorBias.Bullish : b < -0.1 ? FactorBias.Bearish : FactorBias.Neutral,
                Math.Round(bull, 2),
                b >= 0 ? Loc.T("Idx.AboveAverages") : Loc.T("Idx.BelowAverages")));
        }

        // VIX: rising volatility is risk-off. It subtracts from the bias and drives risk level.
        RiskLevel risk = RiskLevel.Medium;
        if (indexSnaps.TryGetValue("VIX", out var vix))
        {
            double vixTrend = EmaTrend(vix);         // +1 = VIX rising
            bias += 0.05 * -vixTrend;                 // rising VIX pulls bias down
            risk = vix.Price > 22 ? RiskLevel.High : vix.Price < 15 ? RiskLevel.Low : RiskLevel.Medium;
            indices.Add(new IndexSnapshotDto("VIX", Loc.T("Index.Volatility"),
                vixTrend > 0.1 ? FactorBias.Bearish : FactorBias.Bullish,   // rising VIX shown as bearish-for-market
                Math.Round(0.5 - 0.5 * vixTrend, 2),
                Loc.T("Vix.Detail", vix.Price, vixTrend > 0 ? Loc.T("Word.Rising") : Loc.T("Word.Falling"))));
        }

        bias = Math.Clamp(bias, -1, 1);
        string regime =
            bias > 0.30 ? Loc.T("Regime.BullTrend") :
            bias > 0.05 ? Loc.T("Regime.BullBias") :
            bias < -0.30 ? Loc.T("Regime.BearTrend") :
            bias < -0.05 ? Loc.T("Regime.BearBias") :
            Loc.T("Regime.Sideways");

        string note = risk == RiskLevel.High
            ? Loc.T("Regime.NoteHighVol")
            : bias >= 0 ? Loc.T("Regime.NoteSupport")
            : Loc.T("Regime.NoteAdverse");

        var dto = new MarketRegimeDto(regime, risk, note, indices, DateTime.UtcNow);
        return (dto, bias);
    }

    private static double EmaTrend(IndicatorSnapshot s)
    {
        double v = 0;
        v += s.Price > s.Ema9 ? 1 : -1;
        v += s.Price > s.Ema20 ? 1 : -1;
        v += s.Price > s.Ema50 ? 1 : -1;
        v += s.Ema9 > s.Ema20 ? 1 : -1;
        v += s.Ema20 > s.Ema50 ? 1 : -1;
        return v / 5.0;
    }

    private static double Momentum(IndicatorSnapshot s)
    {
        double rsiComp = double.IsNaN(s.Rsi) ? 0 : Math.Clamp((s.Rsi - 50) / 50.0, -1, 1);
        double macdComp = (!double.IsNaN(s.MacdHistogram) && !double.IsNaN(s.Atr) && s.Atr > 0)
            ? Math.Clamp(s.MacdHistogram / (s.Atr * 0.5), -1, 1) : 0;
        return Math.Clamp((rsiComp + macdComp) / 2.0, -1, 1);
    }
}
