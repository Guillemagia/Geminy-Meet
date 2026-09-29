using MarketIntel.Shared.Contracts;

namespace MarketIntel.Api.MarketData;

/// <summary>
/// Abstraction over the market-data source. The rest of the platform depends only on this
/// interface, so a real provider (Alpaca, Polygon, Finnhub, ...) can replace the mock
/// without touching indicators, signals, scanner or the API surface.
/// </summary>
public interface IMarketDataProvider
{
    /// <summary>Human-readable provider name, surfaced in data-quality reporting.</summary>
    string Name { get; }

    /// <summary>True when this provider returns synthetic (non-tradeable) data.</summary>
    bool IsSynthetic { get; }

    /// <summary>Most recent OHLCV candles for a symbol at a timeframe, oldest first.</summary>
    Task<IReadOnlyList<CandleDto>> GetCandlesAsync(
        string symbol, Timeframe timeframe, int count, CancellationToken ct = default);

    /// <summary>Latest traded price for a symbol.</summary>
    Task<decimal> GetLastPriceAsync(string symbol, CancellationToken ct = default);

    /// <summary>The set of symbols the scanner iterates over.</summary>
    Task<IReadOnlyList<string>> GetUniverseAsync(CancellationToken ct = default);
}
