using MarketIntel.Api.Localization;
using MarketIntel.Api.MarketData;
using MarketIntel.Shared.Contracts;

namespace MarketIntel.Api.Ml;

/// <summary>A trained model plus its honest out-of-sample accuracy.</summary>
public sealed record TrainedModel(LogisticRegressionModel Model, double TestAccuracy, int TestSize);

/// <summary>
/// Trains and caches a per-symbol ML model. Data is split chronologically (train on the past,
/// test on the most recent slice) so the reported accuracy is out-of-sample, not a fit-to-noise
/// figure. Retrains once per day (matches the synthetic feed's daily reseed).
/// </summary>
public sealed class ModelService
{
    private readonly IMarketDataProvider _data;
    private readonly FeatureExtractor _features;
    private readonly BacktestEngine _backtest;
    private readonly WalkForwardValidator _validator;

    public const int Horizon = 5;      // predict direction 5 bars (≈1 week) ahead
    private const int BarCount = 320;
    private const double TrainFraction = 0.70;
    private const double L2 = 1e-3;    // weight decay to curb overfitting

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Dictionary<string, (TrainedModel Model, int Day)> _cache = new();

    public ModelService(IMarketDataProvider data, FeatureExtractor features, BacktestEngine backtest, WalkForwardValidator validator)
    {
        _data = data;
        _features = features;
        _backtest = backtest;
        _validator = validator;
    }

    public async Task<TrainedModel?> GetOrTrainAsync(string symbol, CancellationToken ct = default)
    {
        symbol = symbol.ToUpperInvariant();
        int day = (int)(DateTime.UtcNow.Date.ToBinary() % int.MaxValue);

        if (_cache.TryGetValue(symbol, out var hit) && hit.Day == day) return hit.Model;

        await _lock.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(symbol, out hit) && hit.Day == day) return hit.Model;

            var candles = await _data.GetCandlesAsync(symbol, Timeframe.D1, BarCount, ct);
            var (x, y) = _features.BuildDataset(candles, Horizon);
            if (x.Count < 40) return null; // not enough history to train/test meaningfully

            // Honest out-of-sample estimate via walk-forward (purge + embargo), not a single split.
            var wf = _validator.Evaluate(x, y, Horizon, l2: L2);

            // Deploy a model trained on ALL available history (regularized) for live prediction.
            var model = new LogisticRegressionModel();
            model.Train(x, y, l2: L2);

            // Report the walk-forward accuracy. If the series was too short to walk forward, fall
            // back to a single chronological holdout (still out-of-sample, never in-sample).
            double acc;
            int sample;
            if (wf.Sample > 0)
            {
                acc = wf.Accuracy;
                sample = wf.Sample;
            }
            else
            {
                int split = (int)(x.Count * TrainFraction);
                var holdout = new LogisticRegressionModel();
                holdout.Train(x.Take(split).ToList(), y.Take(split).ToList(), l2: L2);
                var xTest = x.Skip(split).ToList();
                acc = Accuracy(holdout, xTest, y.Skip(split).ToList());
                sample = xTest.Count;
            }

            var trained = new TrainedModel(model, acc, sample);
            _cache[symbol] = (trained, day);
            return trained;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<MlPredictionDto?> PredictAsync(string symbol, CancellationToken ct = default)
    {
        var trained = await GetOrTrainAsync(symbol, ct);
        if (trained is null) return null;

        var candles = await _data.GetCandlesAsync(symbol.ToUpperInvariant(), Timeframe.D1, BarCount, ct);
        var features = _features.ExtractLatest(candles);
        double proba = trained.Model.PredictBullishProbability(features);

        return new MlPredictionDto(
            BullishProbability: Math.Round(proba, 3),
            TestAccuracy: Math.Round(trained.TestAccuracy, 3),
            Sample: trained.TestSize,
            ModelName: trained.Model.Name);
    }

    /// <summary>
    /// Out-of-sample backtest of the ML strategy on the most recent (test) region. Trains a
    /// dedicated holdout model on the first <see cref="TrainFraction"/> of history and backtests on
    /// the untouched remainder — NOT the deployed model (which has seen all bars), so there is no leakage.
    /// </summary>
    public async Task<BacktestResultDto?> GetBacktestAsync(string symbol, CancellationToken ct = default)
    {
        symbol = symbol.ToUpperInvariant();
        var candles = await _data.GetCandlesAsync(symbol, Timeframe.D1, BarCount, ct);
        var (x, y) = _features.BuildDataset(candles, Horizon);
        if (x.Count < 40) return null;

        int split = (int)(x.Count * TrainFraction);
        var holdout = new LogisticRegressionModel();
        holdout.Train(x.Take(split).ToList(), y.Take(split).ToList(), l2: L2);

        int startIndex = FeatureExtractor.Warmup + split; // candle index where out-of-sample begins
        return _backtest.Run(symbol, Loc.T("Model.AIStrategy"), candles, holdout, Horizon, startIndex);
    }

    private static double Accuracy(LogisticRegressionModel model, IReadOnlyList<double[]> x, IReadOnlyList<int> y)
    {
        if (x.Count == 0) return 0;
        int correct = 0;
        for (int i = 0; i < x.Count; i++)
        {
            int pred = model.PredictProba(x[i]) >= 0.5 ? 1 : 0;
            if (pred == y[i]) correct++;
        }
        return (double)correct / x.Count;
    }
}
