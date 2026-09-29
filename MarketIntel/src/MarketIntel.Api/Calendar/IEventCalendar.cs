using MarketIntel.Shared.Contracts;

namespace MarketIntel.Api.Calendar;

/// <summary>
/// Abstraction over an economic/earnings calendar. The rest of the platform depends only on
/// this, so a real provider (Finnhub, Trading Economics, Alpaca corporate actions…) can
/// replace the mock without touching the event-risk engine or the API surface.
/// </summary>
public interface IEventCalendar
{
    /// <summary>Macro releases (CPI, FOMC, NFP…) scheduled within the window from <paramref name="fromUtc"/>.</summary>
    Task<IReadOnlyList<EconomicEventDto>> GetUpcomingMacroAsync(
        DateTime fromUtc, TimeSpan window, CancellationToken ct = default);

    /// <summary>The next earnings report for a symbol at or after <paramref name="fromUtc"/>, if known.</summary>
    Task<EconomicEventDto?> GetNextEarningsAsync(
        string symbol, DateTime fromUtc, CancellationToken ct = default);
}
