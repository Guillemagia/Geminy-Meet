using MarketIntel.Api.Localization;
using MarketIntel.Shared.Contracts;

namespace MarketIntel.Api.Engines;

/// <summary>
/// The prediction engine: treats each analytical angle (technical, momentum, volume, market,
/// volatility, ML) as an INDEPENDENT model that outputs a bullish probability, then combines
/// them with explicit weights into one consensus score. Every model's opinion is exposed so
/// the combined number is fully explainable.
/// </summary>
public sealed class EnsembleEngine
{
    // Base weights, keyed by a stable model id (the display name is localized at build time).
    // When the ML model is unavailable, its weight is redistributed proportionally.
    private static readonly (string Id, double Weight)[] Weights =
    [
        ("Technical", 0.26),
        ("Momentum", 0.16),
        ("Volume", 0.10),
        ("Market", 0.14),
        ("Volatility", 0.06),
        ("News", 0.13),
        ("AI", 0.20),
    ];

    public EnsembleDto Build(IndicatorSnapshot s, double marketBias, double? mlProbability, double? newsProbability = null)
    {
        double tech = 0.6 * EmaTrend(s) + 0.4 * VwapFactor(s);
        var raw = new Dictionary<string, double>
        {
            ["Technical"] = ToProb(tech),
            ["Momentum"] = ToProb(Momentum(s)),
            ["Volume"] = ToProb(VolumeFactor(s)),
            ["Market"] = ToProb(Math.Clamp(marketBias, -1, 1)),
            ["Volatility"] = ToProb(VolatilityFactor(s)),
        };
        if (newsProbability is double np) raw["News"] = Math.Clamp(np, 0.05, 0.95);
        if (mlProbability is double p) raw["AI"] = Math.Clamp(p, 0.02, 0.98);

        // Keep only available models and renormalise their weights to sum to 1.
        var active = Weights.Where(w => raw.ContainsKey(w.Id)).ToList();
        double weightSum = active.Sum(w => w.Weight);

        var models = new List<ModelOpinionDto>();
        double ensembleProb = 0;
        foreach (var (id, weight) in active)
        {
            double norm = weight / weightSum;
            ensembleProb += norm * raw[id];
            models.Add(new ModelOpinionDto(Loc.T($"Model.{id}"), Math.Round(raw[id], 2), Math.Round(norm, 2)));
        }

        int score = (int)Math.Round(ensembleProb * 100);

        // Consensus = share of models agreeing with the ensemble's side.
        bool bullish = ensembleProb >= 0.5;
        double agree = models.Count(m => (m.BullishProbability >= 0.5) == bullish) / (double)models.Count;
        string consensus =
            agree >= 0.8 ? Loc.T("Consensus.Strong") :
            agree >= 0.6 ? Loc.T("Consensus.Moderate") :
            Loc.T("Consensus.Split");

        return new EnsembleDto(score, Math.Round(ensembleProb, 3), consensus, models);
    }

    // ---- factor helpers (each returns a value in [-1, 1]) ----

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

    private static double VwapFactor(IndicatorSnapshot s)
    {
        if (double.IsNaN(s.Vwap) || s.Vwap == 0) return 0;
        return Math.Clamp((s.Price - s.Vwap) / s.Vwap / 0.01, -1, 1);
    }

    private static double VolumeFactor(IndicatorSnapshot s)
    {
        if (double.IsNaN(s.RelativeVolume)) return 0;
        double magnitude = Math.Clamp((s.RelativeVolume - 1.0) / 1.0, 0, 1);
        double dirSign = s.Price >= s.Vwap ? 1 : -1;
        return magnitude * dirSign;
    }

    private static double VolatilityFactor(IndicatorSnapshot s)
    {
        if (double.IsNaN(s.BollingerUpper) || double.IsNaN(s.BollingerMiddle)) return 0;
        double band = s.BollingerUpper - s.BollingerMiddle;
        return band > 0 ? Math.Clamp((s.Price - s.BollingerMiddle) / band, -1, 1) : 0;
    }

    private static double ToProb(double factor) => Math.Clamp(0.5 + 0.5 * factor, 0.02, 0.98);
}
