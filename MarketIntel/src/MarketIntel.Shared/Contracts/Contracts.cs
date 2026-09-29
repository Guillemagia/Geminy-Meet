using MarketIntel.Shared.Enums;

namespace MarketIntel.Shared.Contracts;

/// <summary>A single OHLCV candle at some timeframe. Used for charts and by the indicator engine.</summary>
public sealed record CandleDto(
    DateTime TimeUtc,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume);

/// <summary>A technical indicator reduced to a display value plus a directional bias.</summary>
public sealed record IndicatorDto(
    string Name,
    string Value,
    FactorBias Bias,
    string? Detail = null);

/// <summary>
/// Probabilistic outcome over one horizon. Probabilities sum to ~1.0. We estimate the
/// probability of scenarios, we do NOT predict an exact price.
/// </summary>
public sealed record HorizonPredictionDto(
    Horizon Horizon,
    string HorizonLabel,
    double BullishProbability,
    double BearishProbability,
    double NeutralProbability,
    SignalDirection Direction,
    ConfidenceLevel Confidence);

/// <summary>One "what could happen" branch, e.g. Bull / Base / Bear case.</summary>
public sealed record ScenarioDto(
    string Name,
    double Probability,
    string Description,
    decimal? Target = null);

/// <summary>
/// One line of the "why this signal?" breakdown. Contribution can be negative
/// (a factor arguing against the overall direction).
/// </summary>
public sealed record ScoreComponentDto(
    string Name,
    double Contribution,
    double MaxContribution,
    string Detail);

/// <summary>Risk-engine output. A high score never means "bet everything".</summary>
public sealed record RiskDto(
    RiskLevel Level,
    decimal? SuggestedStop,
    decimal? SuggestedTarget,
    double? RiskReward,
    double SuggestedRiskPercent,
    string Note);

/// <summary>
/// The full intelligence package for one symbol: probabilities per horizon, the
/// indicators behind them, a transparent score breakdown, scenarios and risk.
/// </summary>
public sealed record SignalDto
{
    public required string Symbol { get; init; }
    public required decimal Price { get; init; }
    public required DateTime AsOfUtc { get; init; }

    /// <summary>0-100 transparent composite score.</summary>
    public required int Score { get; init; }
    public required SignalDirection Direction { get; init; }
    public required ConfidenceLevel Confidence { get; init; }

    /// <summary>True when the responsible action is to NOT trade.</summary>
    public bool IsNoTrade { get; init; }
    public IReadOnlyList<string> NoTradeReasons { get; init; } = [];

    public IReadOnlyList<HorizonPredictionDto> Horizons { get; init; } = [];
    public IReadOnlyList<IndicatorDto> Indicators { get; init; } = [];
    public IReadOnlyList<ScoreComponentDto> ScoreBreakdown { get; init; } = [];
    public IReadOnlyList<ScenarioDto> Scenarios { get; init; } = [];

    /// <summary>Support/resistance zones nearest to the current price.</summary>
    public IReadOnlyList<PriceZoneDto> Levels { get; init; } = [];

    /// <summary>Price-action patterns on the latest bar, with empirical win rates.</summary>
    public IReadOnlyList<PatternDto> Patterns { get; init; } = [];

    /// <summary>Upcoming-event risk (earnings, macro releases) affecting this symbol.</summary>
    public EventRiskDto? EventRisk { get; init; }

    /// <summary>Automatic market-structure read (HH/HL/LH/LL, trend, range, break of structure).</summary>
    public MarketStructureDto? Structure { get; init; }

    /// <summary>Machine-learning model opinion for this symbol/horizon.</summary>
    public MlPredictionDto? Ml { get; init; }

    /// <summary>Multi-model ensemble (prediction engine) combining independent models.</summary>
    public EnsembleDto? Ensemble { get; init; }

    /// <summary>Stacked ensemble whose combination weights are LEARNED out-of-sample, not hand-set.</summary>
    public LearnedEnsembleDto? LearnedEnsemble { get; init; }

    /// <summary>Regime-conditional weighting: model weights learned separately for the current regime.</summary>
    public RegimeWeightingDto? RegimeWeighting { get; init; }

    /// <summary>Meta-model quality grade (A+…D) + recommended action, with calibrated hit rate.</summary>
    public SignalQualityDto? Quality { get; init; }
    public required RiskDto Risk { get; init; }

    /// <summary>Human-readable rationale generated from the factors above (no invented facts).</summary>
    public required string Explanation { get; init; }

    /// <summary>How complete the underlying data was, 0-100. Feeds confidence.</summary>
    public int DataQuality { get; init; } = 100;
}

/// <summary>
/// One independent quality factor and how many points (of its max) it contributed to the
/// overall signal-quality score. Exposing each factor keeps the grade fully explainable.
/// </summary>
public sealed record QualityFactorDto(
    string Name,
    double Points,
    double MaxPoints,
    bool Favorable,
    string Detail)
{
    /// <summary>Points as a 0-1 fraction of the max, for a progress bar. Computed client-side.</summary>
    public double Fraction => MaxPoints > 0 ? Math.Clamp(Points / MaxPoints, 0, 1) : 0;
}

/// <summary>
/// The meta-model verdict: a 0-100 signal-quality score distilled from independent quality
/// factors (consensus, regime alignment, structure, volume, event risk, calibration…), a letter
/// grade (A+ … D) and the recommended action. Only high grades are meant to be acted on. The
/// grade is only meaningful together with the calibration report that proves its historical hit rate.
/// </summary>
public sealed record SignalQualityDto(
    int QualityScore,
    SignalGrade Grade,
    TradeAction Action,
    string GradeLabel,
    string ActionLabel,
    string Summary,
    IReadOnlyList<QualityFactorDto> Factors,
    // Raw model confidence as a probability [0,1], shown as-is...
    double RawConfidence,
    // ...next to the realized historical hit rate for signals of this grade (from calibration).
    // Null until a calibration report exists for the current data set.
    double? CalibratedHitRate,
    int CalibrationSample);

/// <summary>
/// One row of the calibration/reliability report: for signals the meta-model graded at this
/// bucket, the probability it implied vs. the hit rate they ACTUALLY achieved out-of-sample,
/// with the sample size. When predicted ≈ realized, the system is well-calibrated.
/// </summary>
public sealed record CalibrationBucketDto(
    SignalGrade Grade,
    string GradeLabel,
    double PredictedHitRate,
    double RealizedHitRate,
    int Sample,
    // Share that reached the target before the stop (TP-before-SL), the honest edge metric.
    double TpBeforeSlRate);

/// <summary>
/// Walk-forward calibration report: the platform re-generates its own graded signals across
/// unseen historical bars and measures how often each grade actually hit its target. This is the
/// proof that a grade means what it claims — on synthetic data it correctly lands near ~50%.
/// </summary>
public sealed record CalibrationReportDto(
    string Method,
    int TotalSignals,
    int HorizonBars,
    double BrierScore,
    IReadOnlyList<CalibrationBucketDto> Buckets,
    string Note);

/// <summary>
/// A live-registry snapshot: signals the running system actually emitted and later resolved,
/// aggregated by grade. Unlike the walk-forward report this accumulates real forward evidence.
/// </summary>
public sealed record SignalRegistryStatsDto(
    int Total,
    int Resolved,
    int Pending,
    IReadOnlyList<CalibrationBucketDto> ByGrade,
    string Note);

/// <summary>
/// A support or resistance ZONE (not a single line). Built from clustered swing points;
/// strength reflects how many times price reacted there.
/// </summary>
public sealed record PriceZoneDto(
    ZoneKind Kind,
    decimal Low,
    decimal High,
    int Strength,
    string Label,
    double DistancePercent);

/// <summary>
/// A price-action pattern detected on the latest bar, together with its EMPIRICAL success
/// rate: how often the same pattern led to the expected move in this symbol's own history.
/// A pattern is never assumed bullish/bearish by name alone.
/// </summary>
public sealed record PatternDto(
    string Name,
    FactorBias Bias,
    string Timeframe,
    double HistoricalWinRate,
    int Sample,
    string Detail);

/// <summary>A labeled swing point (higher-high, lower-low, …) in the price structure.</summary>
public sealed record SwingPointDto(
    SwingLabel Label,
    decimal Price,
    DateTime TimeUtc);

/// <summary>
/// Automatic market-structure read: the trend implied by the sequence of swings (HH/HL vs
/// LH/LL), whether price is ranging, and any break of structure.
/// </summary>
public sealed record MarketStructureDto(
    string Trend,
    string Description,
    IReadOnlyList<SwingPointDto> Swings,
    bool InRange,
    decimal? RangeLow,
    decimal? RangeHigh,
    // Language-independent trend direction, so the UI can colour the trend without parsing its text.
    FactorBias TrendBias = FactorBias.Neutral);

/// <summary>
/// Output of the machine-learning model: an estimated bullish probability for the horizon,
/// plus the model's out-of-sample accuracy so the reader knows how much to trust it.
/// </summary>
public sealed record MlPredictionDto(
    double BullishProbability,
    double TestAccuracy,
    int Sample,
    string ModelName);

/// <summary>One independent model's opinion and its weight in the ensemble.</summary>
public sealed record ModelOpinionDto(
    string Name,
    double BullishProbability,
    double Weight);

/// <summary>
/// The prediction engine: several independent models combined by explicit weights into one
/// consensus score. Shows each model's opinion so the combined number is fully explainable.
/// </summary>
public sealed record EnsembleDto(
    int Score,
    double BullishProbability,
    string Consensus,
    IReadOnlyList<ModelOpinionDto> Models);

/// <summary>
/// A stacked ("learned") ensemble: instead of hand-set weights, a meta-model is trained
/// out-of-sample on the base models' historical outputs to learn how to combine them. Each base
/// model's opinion carries the LEARNED weight, and the whole thing reports its own walk-forward
/// accuracy — so we can tell whether learning the combination actually beats the fixed weights.
/// On synthetic data it correctly lands near chance; the weights are the honest signal.
/// </summary>
public sealed record LearnedEnsembleDto(
    double BullishProbability,
    double OutOfSampleAccuracy,
    int Sample,
    string Note,
    IReadOnlyList<ModelOpinionDto> Models);

/// <summary>
/// Regime-conditional weighting: the same model is worth different amounts in different market
/// regimes (trend-following shines when trending, volatility matters when things get wild). A
/// separate meta-model is learned PER local regime, so the combination adapts to the regime the
/// symbol is actually in right now. The weights shown are those the model learned for the CURRENT
/// regime; the accuracy is a regime-routed walk-forward (each past bar judged by its own regime's
/// model), so it stays honest out-of-sample.
/// </summary>
public sealed record RegimeWeightingDto(
    string RegimeLabel,
    double BullishProbability,
    double OutOfSampleAccuracy,
    int Sample,
    string Note,
    IReadOnlyList<ModelOpinionDto> Models);

/// <summary>
/// Formal backtest metrics for a strategy over historical bars. Proving a strategy works
/// historically matters more than a claimed accuracy number.
/// </summary>
public sealed record BacktestResultDto(
    string Strategy,
    string Symbol,
    int Trades,
    double WinRate,
    double AvgWinPercent,
    double AvgLossPercent,
    double ProfitFactor,
    double MaxDrawdownPercent,
    double Sharpe,
    double TotalReturnPercent);

/// <summary>A news headline with its analysed sentiment and importance.</summary>
public sealed record NewsItemDto(
    string Headline,
    string Source,
    NewsSentiment Sentiment,
    EventImportance Importance,
    string TimeAgo);

/// <summary>Aggregated news sentiment for a symbol, with the underlying headlines.</summary>
public sealed record NewsAnalysisDto(
    string Symbol,
    NewsSentiment OverallSentiment,
    double SentimentScore,
    string Summary,
    IReadOnlyList<NewsItemDto> Items);

/// <summary>A single option contract with pricing, Greeks and a liquidity/quality score.</summary>
public sealed record OptionContractDto(
    OptionType Type,
    decimal Strike,
    string Expiration,
    int DaysToExpiration,
    decimal Bid,
    decimal Ask,
    decimal Mid,
    long Volume,
    long OpenInterest,
    double Iv,
    double Delta,
    double Gamma,
    double Theta,
    double Vega,
    double VolumeOiRatio,
    int QualityScore);

/// <summary>
/// Options view for a symbol: the market's expected move, the best-quality contracts, and any
/// unusual activity (volume far above open interest). Unusual volume is flagged, not interpreted.
/// </summary>
public sealed record OptionsAnalysisDto(
    string Symbol,
    decimal Spot,
    decimal ExpectedMove,
    double ExpectedMovePercent,
    string ExpectedMoveLabel,
    IReadOnlyList<OptionContractDto> Best,
    IReadOnlyList<OptionContractDto> Unusual);

/// <summary>A compact row for the opportunity scanner / top-opportunities list.</summary>
public sealed record OpportunityDto(
    string Symbol,
    decimal Price,
    SignalDirection Direction,
    int Score,
    ConfidenceLevel Confidence,
    string Headline);

/// <summary>Bias of a single index / benchmark used for market context.</summary>
public sealed record IndexSnapshotDto(
    string Symbol,
    string Name,
    FactorBias Bias,
    double BullishProbability,
    string Detail);

/// <summary>Overall market context. A symbol is never analysed in isolation.</summary>
public sealed record MarketRegimeDto(
    string Regime,
    RiskLevel Risk,
    string Note,
    IReadOnlyList<IndexSnapshotDto> Indices,
    DateTime AsOfUtc);

/// <summary>Reply from the grounded assistant: an answer plus suggested follow-up questions.</summary>
public sealed record AssistantReplyDto(
    string Answer,
    IReadOnlyList<string> Suggestions);

/// <summary>Historical accuracy of the technical signal over one horizon.</summary>
public sealed record HorizonAccuracyDto(string Label, double Accuracy, int Sample);

/// <summary>Out-of-sample model accuracy for one symbol.</summary>
public sealed record SymbolAccuracyDto(string Symbol, double Accuracy, int Sample);

/// <summary>
/// System accuracy dashboard: how well predictions have actually done historically, by horizon
/// and by symbol. Publishing real accuracy is what separates a tool from "AI predicts stocks".
/// </summary>
public sealed record AccuracyDashboardDto(
    double OverallAccuracy,
    int TotalSample,
    IReadOnlyList<HorizonAccuracyDto> ByHorizon,
    IReadOnlyList<SymbolAccuracyDto> BySymbol);


/// <summary>Platform metadata: which data provider is active and the required disclaimer.</summary>
public sealed record MetaDto(
    string Provider,
    bool Synthetic,
    string Disclaimer);

/// <summary>A scheduled calendar event: a macro release (CPI, FOMC, NFP…) or a symbol's earnings.</summary>
public sealed record EconomicEventDto(
    string Title,
    EventKind Kind,
    EventImportance Importance,
    DateTime WhenUtc,
    double HoursUntil,
    string Detail,
    string? Symbol = null);

/// <summary>
/// Event-risk assessment for a symbol: how close and how important the nearest events are, and
/// whether the responsible action is to avoid new positions. A good platform knows when NOT to trade.
/// </summary>
public sealed record EventRiskDto(
    RiskLevel Level,
    bool ForcesNoTrade,
    string Message,
    IReadOnlyList<EconomicEventDto> Events);
