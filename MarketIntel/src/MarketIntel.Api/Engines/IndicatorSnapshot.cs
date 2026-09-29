namespace MarketIntel.Api.Engines;

/// <summary>Raw computed indicator values for one symbol at one timeframe. Internal to the API.</summary>
public sealed record IndicatorSnapshot
{
    public required double Price { get; init; }
    public required double Ema9 { get; init; }
    public required double Ema20 { get; init; }
    public required double Ema50 { get; init; }
    public required double Ema200 { get; init; }
    public required double Sma20 { get; init; }
    public required double Sma50 { get; init; }
    public required double Rsi { get; init; }
    public required double MacdLine { get; init; }
    public required double MacdSignal { get; init; }
    public required double MacdHistogram { get; init; }
    public required double Atr { get; init; }
    public required double Vwap { get; init; }
    public required double BollingerUpper { get; init; }
    public required double BollingerMiddle { get; init; }
    public required double BollingerLower { get; init; }
    public required double RelativeVolume { get; init; }
    public required double Adx { get; init; }

    /// <summary>Fraction of indicators that computed to a finite value, 0-1. Feeds data quality.</summary>
    public required double Completeness { get; init; }
}
