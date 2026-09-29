using System.Globalization;
using MarketIntel.Api.Calendar;
using MarketIntel.Api.Engines;
using MarketIntel.Api.Localization;
using MarketIntel.Api.MarketData;
using MarketIntel.Api.Ml;
using MarketIntel.Api.News;
using MarketIntel.Shared.Contracts;
using MarketIntel.Shared.Enums;

namespace MarketIntel.Api.Services;

/// <summary>
/// Orchestrates the pipeline: market data -> per-timeframe indicator snapshots -> market
/// regime -> per-symbol signal -> scanner. This is the single entry point the API endpoints
/// call, so the HTTP layer stays thin.
/// </summary>
public sealed class MarketAnalysisService
{
    private readonly IMarketDataProvider _data;
    private readonly IndicatorEngine _indicators;
    private readonly SignalEngine _signals;
    private readonly MarketRegimeEngine _regime;
    private readonly EnsembleEngine _ensemble;
    private readonly StackingEnsembleEngine _stacking;
    private readonly RegimeStackingEngine _regimeStacking;
    private readonly SupportResistanceEngine _levels;
    private readonly PricePatternEngine _patterns;
    private readonly MarketStructureEngine _structure;
    private readonly IEventCalendar _calendar;
    private readonly EventRiskEngine _eventRisk;
    private readonly ModelService _model;
    private readonly NewsEngine _news;
    private readonly SignalQualityEngine _quality;
    private readonly CalibrationEngine _calibration;
    private readonly SignalRegistry _registry;

    // Timeframes the signal engine consumes (one per horizon + context).
    private static readonly Timeframe[] SignalTimeframes =
        [Timeframe.M15, Timeframe.M30, Timeframe.H1, Timeframe.D1, Timeframe.W1];

    private const int BarCount = 260; // enough to warm up EMA200

    // Short-lived cache of the market regime (the scanner reuses it across symbols).
    // Keyed by language: the regime text is localized, so a Spanish caller must not get a
    // cached English regime and vice-versa.
    private readonly SemaphoreSlim _regimeLock = new(1, 1);
    private readonly Dictionary<string, (MarketRegimeDto Regime, double Bias, DateTime At)> _regimeCache = new();
    private static readonly TimeSpan RegimeTtl = TimeSpan.FromSeconds(20);

    private static string Lang => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

    public MarketAnalysisService(
        IMarketDataProvider data,
        IndicatorEngine indicators,
        SignalEngine signals,
        MarketRegimeEngine regime,
        EnsembleEngine ensemble,
        StackingEnsembleEngine stacking,
        RegimeStackingEngine regimeStacking,
        SupportResistanceEngine levels,
        PricePatternEngine patterns,
        MarketStructureEngine structure,
        IEventCalendar calendar,
        EventRiskEngine eventRisk,
        ModelService model,
        NewsEngine news,
        SignalQualityEngine quality,
        CalibrationEngine calibration,
        SignalRegistry registry)
    {
        _data = data;
        _indicators = indicators;
        _signals = signals;
        _regime = regime;
        _ensemble = ensemble;
        _stacking = stacking;
        _regimeStacking = regimeStacking;
        _levels = levels;
        _patterns = patterns;
        _structure = structure;
        _calendar = calendar;
        _eventRisk = eventRisk;
        _model = model;
        _news = news;
        _quality = quality;
        _calibration = calibration;
        _registry = registry;
    }

    public string ProviderName => _data.Name;
    public bool IsSyntheticData => _data.IsSynthetic;

    public async Task<MarketRegimeDto> GetRegimeAsync(CancellationToken ct = default)
        => (await GetRegimeAndBiasAsync(ct)).Regime;

    public async Task<SignalDto> GetSignalAsync(string symbol, CancellationToken ct = default)
    {
        symbol = symbol.ToUpperInvariant();
        var (_, bias, _) = await GetRegimeAndBiasAsync(ct);
        var snaps = await BuildSnapshotsAsync(symbol, ct);
        decimal price = await ResolvePriceAsync(symbol, snaps, ct);
        var eventRisk = await BuildEventRiskAsync(symbol, ct);

        var signal = _signals.Build(symbol, price, snaps, bias, eventRisk);

        // Only build the display list when the primary snapshot is usable (avoids NaN rows).
        if (snaps[Timeframe.H1].Completeness >= 0.3)
            signal = signal with { Indicators = _indicators.ToDisplay(snaps[Timeframe.H1]) };

        // Support/resistance zones and price-action patterns from daily candles (best-effort).
        try
        {
            var dailyCandles = await _data.GetCandlesAsync(symbol, Timeframe.D1, 150, ct);
            double atr = snaps.TryGetValue(Timeframe.D1, out var d) && !double.IsNaN(d.Atr)
                ? d.Atr : (double)price * 0.02;

            var levels = _levels.Compute(dailyCandles, price, atr);
            var patterns = _patterns.Compute(dailyCandles);
            var structure = _structure.Compute(dailyCandles, price, atr);
            signal = signal with
            {
                Levels = levels.Count > 0 ? levels : signal.Levels,
                Patterns = patterns.Count > 0 ? patterns : signal.Patterns,
                Structure = structure ?? signal.Structure
            };
        }
        catch { /* levels/patterns are optional */ }

        // ML model opinion (V2): attach it and let agreement modulate confidence.
        try
        {
            var ml = await _model.PredictAsync(symbol, ct);
            if (ml is not null)
                signal = ApplyMlConfidence(signal with { Ml = ml }, ml);
        }
        catch { /* ML is optional */ }

        // Ensemble (prediction engine): combine the independent models into a consensus.
        if (snaps[Timeframe.H1].Completeness >= 0.3)
        {
            double? newsProb = null;
            try { newsProb = await _news.BullishProbabilityAsync(symbol, ct); } catch { }
            signal = signal with { Ensemble = _ensemble.Build(snaps[Timeframe.H1], bias, signal.Ml?.BullishProbability, newsProb) };

            // Learned (stacked) ensemble: a meta-model that learned how to combine the base models
            // out-of-sample, instead of the hand-set weights above.
            try
            {
                var learned = await _stacking.BuildAsync(symbol, ct);
                if (learned is not null) signal = signal with { LearnedEnsemble = learned };
            }
            catch { /* stacking is optional */ }

            // Regime-conditional weighting: weights learned for the regime the symbol is in now.
            try
            {
                var regimeWeighting = await _regimeStacking.BuildAsync(symbol, ct);
                if (regimeWeighting is not null) signal = signal with { RegimeWeighting = regimeWeighting };
            }
            catch { /* regime stacking is optional */ }
        }

        // Meta-model: distil everything into a quality grade (A+…D) + recommended action,
        // then attach the out-of-sample hit rate calibration measured for that grade.
        if (snaps[Timeframe.H1].Completeness >= 0.3)
        {
            var quality = _quality.Build(signal, snaps[Timeframe.H1], bias);
            try
            {
                var (hitRate, sample) = await _calibration.HitRateForGradeAsync(quality.Grade, ct);
                if (sample > 0)
                    quality = quality with { CalibratedHitRate = hitRate, CalibrationSample = sample };
            }
            catch { /* calibration is best-effort; the raw grade still stands */ }
            signal = signal with { Quality = quality };

            // Log actionable grades to the live registry (forward-only proof; dedup per day inside).
            try { await _registry.RecordAsync(signal, ct); } catch { /* registry is best-effort */ }
        }

        return signal;
    }

    /// <summary>When the ML model (if trustworthy) disagrees with the technical direction, shave confidence.</summary>
    private static SignalDto ApplyMlConfidence(SignalDto s, MlPredictionDto ml)
    {
        if (s.IsNoTrade || s.Direction == SignalDirection.Neutral || ml.TestAccuracy < 0.52) return s;

        bool mlBull = ml.BullishProbability >= 0.55;
        bool mlBear = ml.BullishProbability <= 0.45;
        bool disagree = (s.Direction == SignalDirection.Bullish && mlBear) ||
                        (s.Direction == SignalDirection.Bearish && mlBull);

        if (disagree && s.Confidence > ConfidenceLevel.Low)
            return s with { Confidence = (ConfidenceLevel)((int)s.Confidence - 1) };
        return s;
    }

    /// <summary>Best-effort current price: live last-price, else a finite snapshot close.</summary>
    private async Task<decimal> ResolvePriceAsync(
        string symbol, IReadOnlyDictionary<Timeframe, IndicatorSnapshot> snaps, CancellationToken ct)
    {
        try
        {
            var last = await _data.GetLastPriceAsync(symbol, ct);
            if (last > 0) return last;
        }
        catch { /* fall through to snapshot */ }

        foreach (var tf in SignalTimeframes)
            if (snaps.TryGetValue(tf, out var s) && !double.IsNaN(s.Price))
                return (decimal)s.Price;
        return 0m;
    }

    public async Task<IReadOnlyList<OpportunityDto>> GetTopOpportunitiesAsync(int take = 5, CancellationToken ct = default)
    {
        var universe = await _data.GetUniverseAsync(ct);
        var (_, bias, _) = await GetRegimeAndBiasAsync(ct);

        var results = new List<(OpportunityDto Op, int Strength)>();
        foreach (var symbol in universe)
        {
            ct.ThrowIfCancellationRequested();
            IReadOnlyDictionary<Timeframe, IndicatorSnapshot> snaps;
            try { snaps = await BuildSnapshotsAsync(symbol, ct); }
            catch { continue; } // skip a symbol the provider couldn't serve

            decimal price = await ResolvePriceAsync(symbol, snaps, ct);
            var eventRisk = await BuildEventRiskAsync(symbol, ct);
            var sig = _signals.Build(symbol, price, snaps, bias, eventRisk);
            if (sig.IsNoTrade) continue;

            string headline = BuildHeadline(sig, snaps[Timeframe.H1]);
            results.Add((
                new OpportunityDto(sig.Symbol, sig.Price, sig.Direction, sig.Score, sig.Confidence, headline),
                Math.Abs(sig.Score - 50)));
        }

        return results
            .OrderByDescending(r => r.Strength)
            .ThenByDescending(r => r.Op.Score)
            .Take(take)
            .Select(r => r.Op)
            .ToList();
    }

    public Task<IReadOnlyList<CandleDto>> GetCandlesAsync(string symbol, Timeframe tf, int count, CancellationToken ct = default)
        => _data.GetCandlesAsync(symbol.ToUpperInvariant(), tf, count, ct);

    /// <summary>Compact quotes (score/direction) for an arbitrary set of symbols (watchlist).</summary>
    public async Task<IReadOnlyList<OpportunityDto>> GetQuotesAsync(IEnumerable<string> symbols, CancellationToken ct = default)
    {
        var (_, bias, _) = await GetRegimeAndBiasAsync(ct);
        var list = new List<OpportunityDto>();
        foreach (var raw in symbols)
        {
            ct.ThrowIfCancellationRequested();
            var symbol = raw.Trim().ToUpperInvariant();
            if (symbol.Length == 0) continue;
            try
            {
                var snaps = await BuildSnapshotsAsync(symbol, ct);
                var price = await ResolvePriceAsync(symbol, snaps, ct);
                var eventRisk = await BuildEventRiskAsync(symbol, ct);
                var sig = _signals.Build(symbol, price, snaps, bias, eventRisk);
                var headline = snaps.TryGetValue(Timeframe.H1, out var h) ? BuildHeadline(sig, h) : $"Score {sig.Score}/100";
                list.Add(new OpportunityDto(sig.Symbol, sig.Price, sig.Direction, sig.Score, sig.Confidence, headline));
            }
            catch { /* skip a symbol the provider couldn't serve */ }
        }
        return list;
    }

    /// <summary>Upcoming macro releases AND universe earnings within the given number of hours.</summary>
    public async Task<IReadOnlyList<EconomicEventDto>> GetUpcomingEventsAsync(int hours, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var window = TimeSpan.FromHours(hours);
        var list = new List<EconomicEventDto>(await _calendar.GetUpcomingMacroAsync(now, window, ct));

        foreach (var sym in await _data.GetUniverseAsync(ct))
        {
            var e = await _calendar.GetNextEarningsAsync(sym, now, ct);
            if (e is not null && e.HoursUntil <= hours) list.Add(e);
        }

        return list.OrderBy(e => e.WhenUtc).ToList();
    }

    // Event risk = imminent macro (cached per language, symbol-independent) + this symbol's next earnings.
    private readonly Dictionary<string, (IReadOnlyList<EconomicEventDto> Macro, DateTime At)> _macroCache = new();

    private async Task<EventRiskDto> BuildEventRiskAsync(string symbol, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var macro = await GetMacroAsync(now, ct);
        var earnings = await _calendar.GetNextEarningsAsync(symbol, now, ct);
        return _eventRisk.Evaluate(earnings, macro);
    }

    private async Task<IReadOnlyList<EconomicEventDto>> GetMacroAsync(DateTime now, CancellationToken ct)
    {
        if (_macroCache.TryGetValue(Lang, out var c) && now - c.At < TimeSpan.FromMinutes(5)) return c.Macro;
        var m = await _calendar.GetUpcomingMacroAsync(now, TimeSpan.FromHours(72), ct);
        _macroCache[Lang] = (m, now);
        return m;
    }

    // ---------------- internals ----------------

    private async Task<IReadOnlyDictionary<Timeframe, IndicatorSnapshot>> BuildSnapshotsAsync(string symbol, CancellationToken ct)
    {
        var dict = new Dictionary<Timeframe, IndicatorSnapshot>();
        foreach (var tf in SignalTimeframes)
        {
            var candles = await _data.GetCandlesAsync(symbol, tf, BarCount, ct);
            dict[tf] = _indicators.Compute(candles);
        }
        return dict;
    }

    private async Task<(MarketRegimeDto Regime, double Bias, DateTime At)> GetRegimeAndBiasAsync(CancellationToken ct)
    {
        if (_regimeCache.TryGetValue(Lang, out var c) && DateTime.UtcNow - c.At < RegimeTtl)
            return c;

        await _regimeLock.WaitAsync(ct);
        try
        {
            if (_regimeCache.TryGetValue(Lang, out var c2) && DateTime.UtcNow - c2.At < RegimeTtl)
                return c2;

            var indexSnaps = new Dictionary<string, IndicatorSnapshot>();
            foreach (var sym in new[] { "SPY", "QQQ", "IWM", "DIA", "VIX" })
            {
                try
                {
                    var candles = await _data.GetCandlesAsync(sym, Timeframe.H1, BarCount, ct);
                    var snap = _indicators.Compute(candles);
                    if (snap.Completeness > 0) indexSnaps[sym] = snap; // skip benchmarks with no data
                }
                catch { /* skip a benchmark the provider couldn't serve */ }
            }

            var (regime, bias) = _regime.Build(indexSnaps);
            var entry = (regime, bias, DateTime.UtcNow);
            _regimeCache[Lang] = entry;
            return entry;
        }
        finally
        {
            _regimeLock.Release();
        }
    }

    private static string BuildHeadline(SignalDto sig, IndicatorSnapshot s)
    {
        string dir = sig.Direction == SignalDirection.Bullish ? Loc.T("Word.Bullish")
            : sig.Direction == SignalDirection.Bearish ? Loc.T("Word.Bearish") : Loc.T("Word.Neutral");
        var parts = new List<string> { Loc.T("Headline.Signal", dir, sig.Score) };
        if (!double.IsNaN(s.RelativeVolume) && s.RelativeVolume >= 1.5) parts.Add(Loc.T("Headline.Volume", s.RelativeVolume));
        parts.Add(s.Price > s.Vwap ? Loc.T("Headline.AboveVwap") : Loc.T("Headline.BelowVwap"));
        if (!double.IsNaN(s.Adx) && s.Adx > 25) parts.Add(Loc.T("Headline.StrongTrend"));
        return string.Join(", ", parts) + ".";
    }
}
