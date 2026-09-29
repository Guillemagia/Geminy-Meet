using MarketIntel.Api.Localization;
using MarketIntel.Shared.Contracts;
using MarketIntel.Shared.Enums;

namespace MarketIntel.Api.Engines;

/// <summary>
/// The META-MODEL. It does not add another opinion on direction; instead it judges how much an
/// already-formed signal deserves to be acted on. It distils independent QUALITY factors
/// (model consensus, signal strength, regime/structure alignment, VWAP/volume confirmation,
/// model reliability and event-risk clearance) into one 0-100 quality score, then maps it to a
/// letter grade (A+ … D) and a recommended action.
///
/// Crucially, the grade is a FILTER, not a promise: only the top grades are meant to be traded,
/// and the grade is only meaningful next to the out-of-sample calibration that proves its real
/// hit rate. The engine deliberately leaves <see cref="SignalQualityDto.CalibratedHitRate"/> null;
/// the orchestrator fills it from the calibration report so responsibilities stay separate.
/// </summary>
public sealed class SignalQualityEngine
{
    // Max points per factor. They sum to 100 so the quality score is a plain weighted total.
    private const double MaxConsensus = 22;
    private const double MaxStrength = 18;
    private const double MaxRegime = 14;
    private const double MaxStructure = 10;
    private const double MaxVwap = 8;
    private const double MaxVolume = 8;
    private const double MaxModel = 8;
    private const double MaxEvent = 12;

    public SignalQualityDto Build(SignalDto s, IndicatorSnapshot primary, double marketBias)
    {
        int dirSign = s.Direction == SignalDirection.Bullish ? 1
                    : s.Direction == SignalDirection.Bearish ? -1 : 0;
        bool actionable = dirSign != 0 && !s.IsNoTrade;

        var factors = new List<QualityFactorDto>();

        // 1) Model consensus: share of ensemble models agreeing with the signal's side.
        double consensusShare = ConsensusShare(s, dirSign);
        double consensusPts = MaxConsensus * consensusShare;
        factors.Add(Factor("Quality.Factor.Consensus", consensusPts, MaxConsensus,
            consensusShare >= 0.6, Loc.T("Quality.Detail.Consensus", (int)Math.Round(consensusShare * 100))));

        // 2) Signal strength: how far the composite score sits from the 50 coin-flip line.
        double strength = Math.Clamp(Math.Abs(s.Score - 50) / 40.0, 0, 1); // 40+ points from 50 == full
        double strengthPts = MaxStrength * strength;
        factors.Add(Factor("Quality.Factor.Strength", strengthPts, MaxStrength,
            strength >= 0.4, Loc.T("Quality.Detail.Strength", s.Score)));

        // 3) Regime alignment: is the broad market pushing the same way as the signal?
        double regimeAlign = dirSign == 0 ? 0 : Math.Clamp(marketBias * dirSign, -1, 1);
        double regimePts = MaxRegime * (0.5 + 0.5 * regimeAlign); // -1 -> 0 pts, +1 -> full
        factors.Add(Factor("Quality.Factor.Regime", regimePts, MaxRegime,
            regimeAlign > 0.05, Loc.T(regimeAlign >= 0 ? "Quality.Detail.RegimeWith" : "Quality.Detail.RegimeAgainst")));

        // 4) Trend structure: HH/HL (or LH/LL) agreeing with the direction, plus a strong ADX.
        double structAlign = StructureAlignment(s, dirSign);
        double adxBoost = double.IsNaN(primary.Adx) ? 0 : Math.Clamp((primary.Adx - 20) / 20.0, 0, 1);
        double structScore = Math.Clamp(0.7 * structAlign + 0.3 * adxBoost, 0, 1);
        double structPts = MaxStructure * structScore;
        factors.Add(Factor("Quality.Factor.Structure", structPts, MaxStructure,
            structScore >= 0.5, Loc.T("Quality.Detail.Structure", double.IsNaN(primary.Adx) ? 0 : primary.Adx)));

        // 5) VWAP / EMA location confirming the side.
        double vwapConfirm = VwapConfirm(primary, dirSign);
        double vwapPts = MaxVwap * vwapConfirm;
        factors.Add(Factor("Quality.Factor.Vwap", vwapPts, MaxVwap,
            vwapConfirm >= 0.5, Loc.T(vwapConfirm >= 0.5 ? "Quality.Detail.VwapConfirm" : "Quality.Detail.VwapAgainst")));

        // 6) Relative volume behind the move.
        double relVol = double.IsNaN(primary.RelativeVolume) ? 1.0 : primary.RelativeVolume;
        double volScore = Math.Clamp((relVol - 0.8) / 1.2, 0, 1); // 0.8x -> 0, 2.0x -> full
        double volPts = MaxVolume * volScore;
        factors.Add(Factor("Quality.Factor.Volume", volPts, MaxVolume,
            relVol >= 1.2, Loc.T("Quality.Detail.Volume", relVol)));

        // 7) Model reliability: the ML model agrees AND has above-random out-of-sample accuracy.
        double modelScore = ModelReliability(s, dirSign);
        double modelPts = MaxModel * modelScore;
        factors.Add(Factor("Quality.Factor.Model", modelPts, MaxModel,
            modelScore >= 0.5, Loc.T("Quality.Detail.Model", (int)Math.Round((s.Ml?.TestAccuracy ?? 0) * 100))));

        // 8) Event-risk clearance: full marks when no imminent/high-impact event clouds the read.
        double eventClear = s.EventRisk is null ? 1.0
            : s.EventRisk.ForcesNoTrade ? 0.0
            : s.EventRisk.Level == RiskLevel.High ? 0.4
            : s.EventRisk.Level == RiskLevel.Medium ? 0.75
            : 1.0;
        double eventPts = MaxEvent * eventClear;
        factors.Add(Factor("Quality.Factor.Event", eventPts, MaxEvent,
            eventClear >= 0.75, Loc.T(eventClear >= 0.75 ? "Quality.Detail.EventClear" : "Quality.Detail.EventRisk")));

        double total = factors.Sum(f => f.Points);
        int quality = (int)Math.Round(Math.Clamp(total, 0, 100));

        // A signal the decision brain already refused (NO-TRADE / neutral) can never be a tradeable grade.
        if (!actionable) quality = Math.Min(quality, 45);

        var (grade, action) = GradeFor(quality, actionable);

        // Raw model confidence = the model's own probability for THIS side, shown as-is (not rewritten).
        double rawConf = RawConfidence(s, dirSign);

        string summary = Loc.T("Quality.Summary", quality, Loc.T($"Grade.{grade}"), Loc.T($"Quality.Action.{action}"));

        return new SignalQualityDto(
            quality, grade, action,
            Loc.T($"Grade.{grade}"),
            Loc.T($"Quality.Action.{action}"),
            summary,
            factors,
            Math.Round(rawConf, 3),
            CalibratedHitRate: null, // filled by the orchestrator from the calibration report
            CalibrationSample: 0);
    }

    // ---- grade / action mapping (echoes the A+/A/B/C/D operating table) ----

    private static (SignalGrade Grade, TradeAction Action) GradeFor(int quality, bool actionable)
    {
        if (!actionable) return (SignalGrade.D, TradeAction.NoTrade);
        return quality switch
        {
            >= 85 => (SignalGrade.APlus, TradeAction.Trade),
            >= 75 => (SignalGrade.A, TradeAction.ReducedRisk),
            >= 62 => (SignalGrade.B, TradeAction.WaitConfirmation),
            >= 50 => (SignalGrade.C, TradeAction.NoTrade),
            _ => (SignalGrade.D, TradeAction.NoTrade)
        };
    }

    // ---- factor helpers ----

    private static double ConsensusShare(SignalDto s, int dirSign)
    {
        if (dirSign == 0) return 0;
        if (s.Ensemble is { Models.Count: > 0 } e)
        {
            bool bull = dirSign > 0;
            return e.Models.Count(m => (m.BullishProbability >= 0.5) == bull) / (double)e.Models.Count;
        }
        // Fall back to the score-breakdown agreement.
        if (s.ScoreBreakdown.Count > 0)
            return s.ScoreBreakdown.Count(c => Math.Sign(c.Contribution) == dirSign) / (double)s.ScoreBreakdown.Count;
        return 0.5;
    }

    private static double StructureAlignment(SignalDto s, int dirSign)
    {
        if (dirSign == 0 || s.Structure is null) return 0.5;
        int trendSign = s.Structure.TrendBias switch
        {
            FactorBias.Bullish => 1,
            FactorBias.Bearish => -1,
            _ => 0
        };
        if (trendSign == 0) return 0.5;
        return trendSign == dirSign ? 1.0 : 0.0;
    }

    private static double VwapConfirm(IndicatorSnapshot s, int dirSign)
    {
        if (dirSign == 0 || double.IsNaN(s.Vwap) || s.Vwap == 0 || double.IsNaN(s.Price)) return 0.5;
        bool aboveVwap = s.Price >= s.Vwap;
        return (aboveVwap && dirSign > 0) || (!aboveVwap && dirSign < 0) ? 1.0 : 0.0;
    }

    private static double ModelReliability(SignalDto s, int dirSign)
    {
        if (dirSign == 0 || s.Ml is null) return 0.5; // neutral when no ML available
        if (s.Ml.TestAccuracy < 0.5) return 0.3;      // a model that is worse than a coin flip earns little
        bool mlBull = s.Ml.BullishProbability >= 0.5;
        bool agrees = (mlBull && dirSign > 0) || (!mlBull && dirSign < 0);
        double edge = Math.Clamp((s.Ml.TestAccuracy - 0.5) / 0.15, 0, 1); // 0.65+ acc == full
        return agrees ? 0.5 + 0.5 * edge : 0.5 * (1 - edge);
    }

    private static double RawConfidence(SignalDto s, int dirSign)
    {
        double pBull = s.Ensemble?.BullishProbability ?? Math.Clamp(s.Score / 100.0, 0, 1);
        return dirSign >= 0 ? pBull : 1 - pBull;
    }

    private static QualityFactorDto Factor(string nameKey, double pts, double max, bool favorable, string detail)
        => new(Loc.T(nameKey), Math.Round(pts, 1), max, favorable, detail);
}
