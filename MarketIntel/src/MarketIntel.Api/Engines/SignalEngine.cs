using MarketIntel.Api.Localization;
using MarketIntel.Api.MarketData;
using MarketIntel.Shared.Contracts;
using MarketIntel.Shared.Enums;

namespace MarketIntel.Api.Engines;

/// <summary>
/// The decision brain. Combines the outputs of independent factors (trend, momentum, VWAP,
/// volume, trend-strength, market context) into ONE transparent 0-100 score, a probability
/// distribution per horizon, scenarios, and — crucially — a NO-TRADE verdict when acting
/// would be irresponsible. It estimates probabilities of scenarios; it never predicts an
/// exact price.
/// </summary>
public sealed class SignalEngine
{
    private readonly RiskEngine _risk;

    public SignalEngine(RiskEngine risk) => _risk = risk;

    // Maximum absolute contribution of each factor. They sum to 100.
    private const double MaxTrend = 22;
    private const double MaxMomentum = 20;
    private const double MaxVwap = 14;
    private const double MaxVolume = 14;
    private const double MaxAdx = 12;
    private const double MaxMarket = 18;

    public SignalDto Build(
        string symbol,
        decimal price,
        IReadOnlyDictionary<Timeframe, IndicatorSnapshot> snaps,
        double marketBias,
        EventRiskDto? eventRisk = null)
    {
        // Primary timeframe for the headline signal: prefer H1, fall back to whatever exists.
        var primary = Pick(snaps, Timeframe.H1);
        var daily = Pick(snaps, Timeframe.D1);

        // Insufficient data -> honest NO-TRADE rather than a signal built on NaNs.
        if (primary.Completeness < 0.3 || double.IsNaN(primary.Price))
        {
            return new SignalDto
            {
                Symbol = symbol,
                Price = price,
                AsOfUtc = DateTime.UtcNow,
                Score = 50,
                Direction = SignalDirection.Neutral,
                Confidence = ConfidenceLevel.Low,
                IsNoTrade = true,
                NoTradeReasons = [Loc.T("Signal.NoDataReason")],
                Risk = _risk.Evaluate(SignalDirection.Neutral, price, 0, 0, ConfidenceLevel.Low, isNoTrade: true),
                Explanation = Loc.T("Signal.NoDataExplanation", symbol),
                DataQuality = (int)Math.Round(primary.Completeness * 100),
                EventRisk = eventRisk
            };
        }

        // ---- Signed factor contributions in [-max, +max] ----
        var components = new List<ScoreComponentDto>();

        double trend = EmaTrend(primary) * MaxTrend;
        components.Add(new ScoreComponentDto(Loc.T("Comp.Trend.Name"), Round(trend), MaxTrend,
            trend > 0 ? Loc.T("Comp.Trend.Bull") : Loc.T("Comp.Trend.Bear")));

        double momentum = Momentum(primary) * MaxMomentum;
        components.Add(new ScoreComponentDto(Loc.T("Comp.Momentum.Name"), Round(momentum), MaxMomentum,
            Loc.T("Comp.Momentum.Detail", primary.Rsi, primary.MacdHistogram >= 0 ? Loc.T("Word.Positive") : Loc.T("Word.Negative"))));

        double vwap = VwapFactor(primary) * MaxVwap;
        components.Add(new ScoreComponentDto(Loc.T("Comp.Vwap.Name"), Round(vwap), MaxVwap,
            primary.Price >= primary.Vwap ? Loc.T("Vwap.Above") : Loc.T("Vwap.Below")));

        double volume = VolumeFactor(primary) * MaxVolume;
        components.Add(new ScoreComponentDto(Loc.T("Comp.Volume.Name"), Round(volume), MaxVolume,
            Loc.T("Comp.Volume.Detail", primary.RelativeVolume)));

        double adx = AdxFactor(primary) * MaxAdx;
        components.Add(new ScoreComponentDto(Loc.T("Comp.Adx.Name"), Round(adx), MaxAdx,
            Loc.T("Comp.Adx.Detail", primary.Adx, primary.Adx > 25 ? Loc.T("Word.StrongTrend") : Loc.T("Word.Sideways"))));

        double market = Math.Clamp(marketBias, -1, 1) * MaxMarket;
        components.Add(new ScoreComponentDto(Loc.T("Comp.Market.Name"), Round(market), MaxMarket,
            marketBias >= 0 ? Loc.T("Market.Support") : Loc.T("Market.Against")));

        double raw = trend + momentum + vwap + volume + adx + market; // [-100, 100]
        int score = (int)Math.Round(Math.Clamp(50 + raw / 2, 0, 100));

        // Agreement: share of factors pointing the same way as the net signal.
        double netSign = Math.Sign(raw);
        double agreement = netSign == 0 ? 0.5
            : components.Count(c => Math.Sign(c.Contribution) == netSign) / (double)components.Count;

        double dataQuality = 100 * primary.Completeness;

        // ---- No-trade logic: knowing when NOT to act. ----
        var reasons = new List<string>();
        if (Math.Abs(score - 50) < 6) reasons.Add(Loc.T("NoTrade.Balanced"));
        if (primary.Adx < 18) reasons.Add(Loc.T("NoTrade.WeakTrend"));
        if (!double.IsNaN(primary.RelativeVolume) && primary.RelativeVolume < 0.6) reasons.Add(Loc.T("NoTrade.LowVolume"));
        if (agreement < 0.45) reasons.Add(Loc.T("NoTrade.Contradict"));
        if (dataQuality < 60) reasons.Add(Loc.T("NoTrade.LowData"));

        bool isNoTrade =
            dataQuality < 60 ||
            (Math.Abs(score - 50) < 6 && primary.Adx < 20) ||
            agreement < 0.4;

        // An imminent, high-impact event forces a NO-TRADE.
        if (eventRisk?.ForcesNoTrade == true)
        {
            isNoTrade = true;
            reasons.Add(eventRisk.Message);
        }

        SignalDirection direction =
            isNoTrade ? SignalDirection.Neutral :
            score >= 58 ? SignalDirection.Bullish :
            score <= 42 ? SignalDirection.Bearish :
            SignalDirection.Neutral;

        ConfidenceLevel confidence =
            (agreement >= 0.7 && dataQuality >= 80 && Math.Abs(score - 50) >= 15) ? ConfidenceLevel.High :
            (agreement >= 0.55 && Math.Abs(score - 50) >= 8) ? ConfidenceLevel.Medium :
            ConfidenceLevel.Low;

        // Elevated (but not imminent) event risk shaves one notch of confidence.
        if (eventRisk?.Level == RiskLevel.High && !isNoTrade && confidence > ConfidenceLevel.Low)
            confidence = (ConfidenceLevel)((int)confidence - 1);

        // ---- Horizons ----
        var horizons = new List<HorizonPredictionDto>
        {
            BuildHorizon(Horizon.Min15, Loc.T("Horizon.15min"), Pick(snaps, Timeframe.M15), marketBias),
            BuildHorizon(Horizon.Min30, Loc.T("Horizon.30min"), Pick(snaps, Timeframe.M30), marketBias),
            BuildHorizon(Horizon.Hour1, Loc.T("Horizon.1h"), Pick(snaps, Timeframe.H1), marketBias),
            BuildHorizon(Horizon.Today, Loc.T("Horizon.Today"), daily, marketBias),
            BuildHorizon(Horizon.Week1, Loc.T("Horizon.1w"), Pick(snaps, Timeframe.W1), marketBias),
        };

        // ---- Scenarios (from the daily horizon + daily ATR) ----
        var today = horizons.First(h => h.Horizon == Horizon.Today);
        double dailyAtr = double.IsNaN(daily.Atr) ? (double)price * 0.02 : daily.Atr;
        var scenarios = new List<ScenarioDto>
        {
            new(Loc.T("Scenario.Bull.Name"), Math.Round(today.BullishProbability, 2),
                Loc.T("Scenario.Bull.Desc", price + 1.5m * (decimal)dailyAtr),
                Math.Round(price + 1.5m * (decimal)dailyAtr, 2)),
            new(Loc.T("Scenario.Base.Name"), Math.Round(today.NeutralProbability, 2),
                Loc.T("Scenario.Base.Desc", price), null),
            new(Loc.T("Scenario.Bear.Name"), Math.Round(today.BearishProbability, 2),
                Loc.T("Scenario.Bear.Desc", price - 1.5m * (decimal)dailyAtr),
                Math.Round(price - 1.5m * (decimal)dailyAtr, 2)),
        };

        var risk = _risk.Evaluate(direction, price, dailyAtr, primary.Adx, confidence, isNoTrade);
        string explanation = BuildExplanation(symbol, direction, components, marketBias, isNoTrade, reasons, confidence);

        return new SignalDto
        {
            Symbol = symbol,
            Price = price,
            AsOfUtc = DateTime.UtcNow,
            Score = score,
            Direction = direction,
            Confidence = confidence,
            IsNoTrade = isNoTrade,
            NoTradeReasons = reasons,
            Horizons = horizons,
            Indicators = [], // filled by the orchestrator (it owns the display list)
            ScoreBreakdown = components,
            Scenarios = scenarios,
            Risk = risk,
            Explanation = explanation,
            DataQuality = (int)Math.Round(dataQuality),
            EventRisk = eventRisk
        };
    }

    // ---------- factor helpers (each returns a value in [-1, 1]) ----------

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
        double macdComp = 0;
        if (!double.IsNaN(s.MacdHistogram) && !double.IsNaN(s.Atr) && s.Atr > 0)
            macdComp = Math.Clamp(s.MacdHistogram / (s.Atr * 0.5), -1, 1);
        return Math.Clamp((rsiComp + macdComp) / 2.0, -1, 1);
    }

    private static double VwapFactor(IndicatorSnapshot s)
    {
        if (double.IsNaN(s.Vwap) || s.Vwap == 0) return 0;
        double dist = (s.Price - s.Vwap) / s.Vwap;
        return Math.Clamp(dist / 0.01, -1, 1); // 1% away from VWAP == full weight
    }

    private static double VolumeFactor(IndicatorSnapshot s)
    {
        if (double.IsNaN(s.RelativeVolume)) return 0;
        double magnitude = Math.Clamp((s.RelativeVolume - 1.0) / 1.0, 0, 1); // volume confirms...
        double dirSign = s.Price >= s.Vwap ? 1 : -1;                          // ...the current direction
        return magnitude * dirSign;
    }

    private static double AdxFactor(IndicatorSnapshot s)
    {
        if (double.IsNaN(s.Adx)) return 0;
        double magnitude = Math.Clamp((s.Adx - 15) / 25.0, 0, 1);
        double dirSign = s.Price >= s.Ema50 ? 1 : -1;
        return magnitude * dirSign;
    }

    private static HorizonPredictionDto BuildHorizon(Horizon h, string label, IndicatorSnapshot s, double marketBias)
    {
        double strength = Math.Clamp(
            0.35 * EmaTrend(s) + 0.25 * Momentum(s) + 0.20 * VwapFactor(s) + 0.20 * Math.Clamp(marketBias, -1, 1),
            -1, 1);

        double neutral = 0.10 + 0.25 * (1 - Math.Abs(strength));
        double bullShare = 1.0 / (1.0 + Math.Exp(-3.0 * strength));
        double bull = (1 - neutral) * bullShare;
        double bear = 1 - neutral - bull;

        // Normalise to guard against rounding drift.
        double sum = bull + bear + neutral;
        bull /= sum; bear /= sum; neutral /= sum;

        SignalDirection dir =
            bull > bear && bull > neutral ? SignalDirection.Bullish :
            bear > bull && bear > neutral ? SignalDirection.Bearish :
            SignalDirection.Neutral;

        ConfidenceLevel conf =
            (Math.Abs(strength) > 0.45 && !double.IsNaN(s.Adx) && s.Adx > 22) ? ConfidenceLevel.High :
            Math.Abs(strength) > 0.25 ? ConfidenceLevel.Medium :
            ConfidenceLevel.Low;

        return new HorizonPredictionDto(h, label,
            Math.Round(bull, 2), Math.Round(bear, 2), Math.Round(neutral, 2), dir, conf);
    }

    private static string BuildExplanation(
        string symbol, SignalDirection direction, List<ScoreComponentDto> comps,
        double marketBias, bool isNoTrade, List<string> reasons, ConfidenceLevel confidence)
    {
        if (isNoTrade)
            return Loc.T("Explain.NoTrade", symbol, string.Join("; ", reasons));

        string dirText = direction switch
        {
            SignalDirection.Bullish => Loc.T("Dir.BullStructure"),
            SignalDirection.Bearish => Loc.T("Dir.BearStructure"),
            _ => Loc.T("Dir.NeutralStructure")
        };

        var top = comps.OrderByDescending(c => Math.Abs(c.Contribution)).Take(3)
                       .Select(c => c.Detail.ToLowerInvariant());
        string marketNote = marketBias >= 0 ? Loc.T("Explain.MarketSupport") : Loc.T("Explain.MarketAgainst");

        return Loc.T("Explain.Body", symbol, dirText, string.Join(", ", top), marketNote, Loc.T($"Conf.{confidence}"));
    }

    private static IndicatorSnapshot Pick(IReadOnlyDictionary<Timeframe, IndicatorSnapshot> snaps, Timeframe want)
    {
        if (snaps.TryGetValue(want, out var s)) return s;
        // Fall back to the closest available timeframe.
        return snaps.Values.First();
    }

    private static double Round(double v) => Math.Round(v, 1);
}
