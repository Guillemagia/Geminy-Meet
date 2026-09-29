namespace MarketIntel.Api.MarketData;

/// <summary>Supported candle timeframes. The engines request several so the analysis
/// is never based only on the last few minutes.</summary>
public enum Timeframe
{
    M1,
    M5,
    M15,
    M30,
    H1,
    H4,
    D1,
    W1
}

public static class TimeframeExtensions
{
    /// <summary>Duration of one candle at this timeframe.</summary>
    public static TimeSpan ToTimeSpan(this Timeframe tf) => tf switch
    {
        Timeframe.M1 => TimeSpan.FromMinutes(1),
        Timeframe.M5 => TimeSpan.FromMinutes(5),
        Timeframe.M15 => TimeSpan.FromMinutes(15),
        Timeframe.M30 => TimeSpan.FromMinutes(30),
        Timeframe.H1 => TimeSpan.FromHours(1),
        Timeframe.H4 => TimeSpan.FromHours(4),
        Timeframe.D1 => TimeSpan.FromDays(1),
        Timeframe.W1 => TimeSpan.FromDays(7),
        _ => TimeSpan.FromMinutes(1)
    };

    /// <summary>
    /// Number of these bars in a trading year (~252 sessions of 6.5h). Used to scale
    /// volatility per bar; a naive minutes-based ratio wrongly inflates daily/weekly moves.
    /// </summary>
    public static double BarsPerYear(this Timeframe tf) => tf switch
    {
        Timeframe.M1 => 252 * 390,
        Timeframe.M5 => 252 * 78,
        Timeframe.M15 => 252 * 26,
        Timeframe.M30 => 252 * 13,
        Timeframe.H1 => 252 * 6.5,
        Timeframe.H4 => 252 * 1.625,
        Timeframe.D1 => 252,
        Timeframe.W1 => 52,
        _ => 252 * 390
    };
}
