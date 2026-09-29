using MarketIntel.Api.Localization;
using MarketIntel.Shared.Contracts;
using MarketIntel.Shared.Enums;

namespace MarketIntel.Api.Engines;

/// <summary>
/// Turns upcoming events (a symbol's earnings + imminent macro releases) into an event-risk
/// verdict. Close, high-impact events raise risk and can force a NO-TRADE: a good platform
/// avoids opening positions right before earnings or a CPI/FOMC print.
/// </summary>
public sealed class EventRiskEngine
{
    public EventRiskDto Evaluate(EconomicEventDto? nextEarnings, IReadOnlyList<EconomicEventDto> upcomingMacro)
    {
        var relevant = new List<EconomicEventDto>();

        // Earnings within ~2 weeks are worth surfacing.
        if (nextEarnings is not null && nextEarnings.HoursUntil <= 14 * 24)
            relevant.Add(nextEarnings);

        // Only High/Critical macro within 72h matters for a trade decision.
        relevant.AddRange(upcomingMacro
            .Where(e => e.HoursUntil <= 72 && e.Importance >= EventImportance.High));

        relevant = relevant.OrderBy(e => e.HoursUntil).Take(5).ToList();

        bool earningsImminent = nextEarnings is not null && nextEarnings.HoursUntil <= 24;
        bool earningsSoon = nextEarnings is not null && nextEarnings.HoursUntil <= 48;
        bool earningsThisWeek = nextEarnings is not null && nextEarnings.HoursUntil <= 7 * 24;

        var criticalMacro = upcomingMacro.Where(e => e.Importance == EventImportance.Critical).ToList();
        bool macroImminent = criticalMacro.Any(e => e.HoursUntil <= 2);
        bool macroToday = criticalMacro.Any(e => e.HoursUntil <= 24);
        bool macroSoon = upcomingMacro.Any(e => e.Importance >= EventImportance.High && e.HoursUntil <= 72);

        bool forcesNoTrade = earningsImminent || macroImminent;

        RiskLevel level =
            forcesNoTrade || earningsSoon || macroToday ? RiskLevel.High :
            earningsThisWeek || macroSoon ? RiskLevel.Medium :
            RiskLevel.Low;

        string message = BuildMessage(nextEarnings, criticalMacro, forcesNoTrade, level, relevant);
        return new EventRiskDto(level, forcesNoTrade, message, relevant);
    }

    private static string BuildMessage(
        EconomicEventDto? earnings, List<EconomicEventDto> criticalMacro,
        bool forcesNoTrade, RiskLevel level, List<EconomicEventDto> relevant)
    {
        // Nearest driver first.
        var nearest = relevant.FirstOrDefault();
        if (forcesNoTrade && nearest is not null)
            return Loc.T("Event.ForceNoTrade", nearest.Title, FormatWhen(nearest.HoursUntil));

        if (level == RiskLevel.High && nearest is not null)
            return Loc.T("Event.HighRisk", nearest.Title, FormatWhen(nearest.HoursUntil));

        if (nearest is not null)
            return Loc.T("Event.Upcoming", nearest.Title, FormatWhen(nearest.HoursUntil));

        return Loc.T("Event.None");
    }

    private static string FormatWhen(double hours) => hours switch
    {
        < 1 => Loc.T("When.Min", Math.Max(1, Math.Round(hours * 60))),
        < 48 => Loc.T("When.Hours", Math.Round(hours)),
        _ => Loc.T("When.Days", Math.Round(hours / 24))
    };
}
