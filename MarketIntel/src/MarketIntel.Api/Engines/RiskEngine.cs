using MarketIntel.Api.Localization;
using MarketIntel.Shared.Contracts;
using MarketIntel.Shared.Enums;

namespace MarketIntel.Api.Engines;

/// <summary>
/// Translates a directional signal into concrete risk parameters. Even an 85/100 signal
/// does not justify betting everything: this engine sizes the risk down.
/// </summary>
public sealed class RiskEngine
{
    public RiskDto Evaluate(
        SignalDirection direction,
        decimal price,
        double atr,
        double adx,
        ConfidenceLevel confidence,
        bool isNoTrade)
    {
        double atrPct = price > 0 ? atr / (double)price : 0;

        // Volatility + trend quality + confidence drive the risk level.
        RiskLevel level;
        if (isNoTrade || atrPct > 0.035 || confidence == ConfidenceLevel.Low)
            level = RiskLevel.High;
        else if (atrPct > 0.02 || adx < 20 || confidence == ConfidenceLevel.Medium)
            level = RiskLevel.Medium;
        else
            level = RiskLevel.Low;

        if (direction == SignalDirection.Neutral || isNoTrade)
        {
            return new RiskDto(
                Level: level,
                SuggestedStop: null,
                SuggestedTarget: null,
                RiskReward: null,
                SuggestedRiskPercent: 0,
                Note: isNoTrade ? Loc.T("Risk.NoTradeNote") : Loc.T("Risk.NeutralNote"));
        }

        decimal atrDec = (decimal)atr;
        const decimal stopMult = 1.5m;
        const decimal targetMult = 3.0m; // 2:1 reward:risk

        decimal stop, target;
        if (direction == SignalDirection.Bullish)
        {
            stop = Math.Round(price - stopMult * atrDec, 2);
            target = Math.Round(price + targetMult * atrDec, 2);
        }
        else
        {
            stop = Math.Round(price + stopMult * atrDec, 2);
            target = Math.Round(price - targetMult * atrDec, 2);
        }

        double risk = Math.Abs((double)(price - stop));
        double reward = Math.Abs((double)(target - price));
        double rr = risk > 0 ? Math.Round(reward / risk, 2) : 0;

        double riskPct = level switch
        {
            RiskLevel.High => 0.5,
            RiskLevel.Medium => 1.0,
            _ => 1.5
        };

        return new RiskDto(
            Level: level,
            SuggestedStop: stop,
            SuggestedTarget: target,
            RiskReward: rr,
            SuggestedRiskPercent: riskPct,
            Note: Loc.T("Risk.Note", Loc.T($"Risk.{level}"), riskPct, stop));
    }
}
