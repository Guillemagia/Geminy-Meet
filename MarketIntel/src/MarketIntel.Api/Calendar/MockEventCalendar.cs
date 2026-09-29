using MarketIntel.Api.Localization;
using MarketIntel.Api.MarketData;
using MarketIntel.Shared.Contracts;
using MarketIntel.Shared.Enums;

namespace MarketIntel.Api.Calendar;

/// <summary>
/// Deterministic synthetic calendar. Macro releases follow realistic recurring rules; earnings
/// are derived from a per-symbol quarterly cycle. Not real dates — replace with a data provider.
/// </summary>
public sealed class MockEventCalendar : IEventCalendar
{
    public Task<IReadOnlyList<EconomicEventDto>> GetUpcomingMacroAsync(
        DateTime fromUtc, TimeSpan window, CancellationToken ct = default)
    {
        var events = new List<EconomicEventDto>();
        DateTime end = fromUtc + window;

        for (var day = fromUtc.Date; day <= end.Date; day = day.AddDays(1))
        {
            foreach (var e in MacroEventsOn(day))
            {
                if (e.WhenUtc >= fromUtc && e.WhenUtc <= end)
                    events.Add(e with { HoursUntil = Math.Round((e.WhenUtc - fromUtc).TotalHours, 1) });
            }
        }

        return Task.FromResult<IReadOnlyList<EconomicEventDto>>(
            events.OrderBy(e => e.WhenUtc).ToList());
    }

    public Task<EconomicEventDto?> GetNextEarningsAsync(
        string symbol, DateTime fromUtc, CancellationToken ct = default)
    {
        symbol = symbol.ToUpperInvariant();
        // ETFs and crypto have no company earnings.
        if (!AssetClass.HasEarnings(symbol)) return Task.FromResult<EconomicEventDto?>(null);

        // Quarterly cycle (~91 days) with a per-symbol phase so reports are spread out.
        int phase = StableHash(symbol) % 91;
        var lastBeforePhase = fromUtc.Date.AddDays(-phase);
        var next = lastBeforePhase;
        while (next < fromUtc.Date) next = next.AddDays(91);

        // Reports land after the close (~16:15 ET ≈ 20:15 UTC).
        var whenUtc = next.AddHours(20).AddMinutes(15);
        double hours = Math.Round((whenUtc - fromUtc).TotalHours, 1);

        var dto = new EconomicEventDto(
            Title: Loc.T("Cal.EarningsTitle", symbol),
            Kind: EventKind.Earnings,
            Importance: EventImportance.Critical,
            WhenUtc: whenUtc,
            HoursUntil: hours,
            Detail: Loc.T("Cal.EarningsDetail", symbol),
            Symbol: symbol);

        return Task.FromResult<EconomicEventDto?>(dto);
    }

    private static IEnumerable<EconomicEventDto> MacroEventsOn(DateTime day)
    {
        // CPI ~13th, 08:30 ET (12:30 UTC).
        if (day.Day == 13)
            yield return Macro(Loc.T("Cal.CPI.Title"), EventImportance.Critical, day, 12, 30, Loc.T("Cal.CPI.Detail"));
        // PPI ~12th.
        if (day.Day == 12)
            yield return Macro(Loc.T("Cal.PPI.Title"), EventImportance.High, day, 12, 30, Loc.T("Cal.PPI.Detail"));
        // Retail sales ~16th.
        if (day.Day == 16)
            yield return Macro(Loc.T("Cal.Retail.Title"), EventImportance.Medium, day, 12, 30, Loc.T("Cal.Retail.Detail"));
        // NFP: first Friday of the month.
        if (day.DayOfWeek == DayOfWeek.Friday && day.Day <= 7)
            yield return Macro(Loc.T("Cal.NFP.Title"), EventImportance.Critical, day, 12, 30, Loc.T("Cal.NFP.Detail"));
        // FOMC: approximate meeting months, ~18th, 14:00 ET (18:00 UTC).
        if (day.Day == 18 && new[] { 1, 3, 5, 6, 7, 9, 11, 12 }.Contains(day.Month))
            yield return Macro(Loc.T("Cal.FOMC.Title"), EventImportance.Critical, day, 18, 0, Loc.T("Cal.FOMC.Detail"));
        // GDP: quarterly, ~26th.
        if (day.Day == 26 && new[] { 1, 4, 7, 10 }.Contains(day.Month))
            yield return Macro(Loc.T("Cal.GDP.Title"), EventImportance.High, day, 12, 30, Loc.T("Cal.GDP.Detail"));
    }

    private static EconomicEventDto Macro(string title, EventImportance imp, DateTime day, int hUtc, int mUtc, string detail)
        => new(title, EventKind.Macro, imp, day.Date.AddHours(hUtc).AddMinutes(mUtc), 0, detail, null);

    private static int StableHash(string s)
    {
        unchecked
        {
            const uint fnvPrime = 16777619;
            uint hash = 2166136261;
            foreach (char c in s) { hash ^= c; hash *= fnvPrime; }
            return (int)(hash & 0x7FFFFFFF);
        }
    }
}
