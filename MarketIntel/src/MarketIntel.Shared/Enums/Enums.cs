namespace MarketIntel.Shared.Enums;

/// <summary>Direction of an expected move.</summary>
public enum SignalDirection
{
    Bearish = -1,
    Neutral = 0,
    Bullish = 1
}

/// <summary>Bias contributed by a single indicator or factor.</summary>
public enum FactorBias
{
    Bearish = -1,
    Neutral = 0,
    Bullish = 1
}

/// <summary>How much trust to place in a signal, driven by data quality and model agreement.</summary>
public enum ConfidenceLevel
{
    Low = 0,
    Medium = 1,
    High = 2
}

/// <summary>Risk classification for acting on a signal.</summary>
public enum RiskLevel
{
    Low = 0,
    Medium = 1,
    High = 2
}

/// <summary>
/// Overall quality grade the meta-model assigns to a signal. Higher is better. Only the top
/// grades are meant to be acted on; the grade is backed by out-of-sample calibration, not a promise.
/// </summary>
public enum SignalGrade
{
    D = 0,      // do not trade
    C = 1,      // do not trade
    B = 2,      // wait for confirmation
    A = 3,      // trade with reduced risk
    APlus = 4   // trade
}

/// <summary>What the platform recommends doing with a signal, derived from its quality grade.</summary>
public enum TradeAction
{
    NoTrade = 0,
    WaitConfirmation = 1,
    ReducedRisk = 2,
    Trade = 3
}

/// <summary>How market-moving a calendar event tends to be.</summary>
public enum EventImportance
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}

/// <summary>Kind of calendar event.</summary>
public enum EventKind
{
    Macro = 0,
    Earnings = 1
}

/// <summary>News sentiment classification.</summary>
public enum NewsSentiment
{
    MuyNegativo = -2,
    Negativo = -1,
    Neutral = 0,
    Positivo = 1,
    MuyPositivo = 2
}

/// <summary>Option contract type.</summary>
public enum OptionType
{
    Call = 0,
    Put = 1
}

/// <summary>Classification of a swing point relative to the previous same-type swing.</summary>
public enum SwingLabel
{
    HigherHigh = 0,
    LowerHigh = 1,
    HigherLow = 2,
    LowerLow = 3
}

/// <summary>Whether a price zone sits below (support) or above (resistance) the current price.</summary>
public enum ZoneKind
{
    Support = 0,
    Resistance = 1
}

/// <summary>
/// Prediction horizons. The platform never gives a single prediction: a symbol can be
/// bullish over 15 minutes yet bearish on the daily chart.
/// </summary>
public enum Horizon
{
    Min15 = 0,
    Min30 = 1,
    Hour1 = 2,
    Today = 3,
    Week1 = 4
}
