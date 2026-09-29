namespace MarketIntel.Api.Ml;

/// <summary>Aggregated out-of-sample result of a walk-forward evaluation.</summary>
public sealed record WalkForwardResult(double Accuracy, double Brier, int Folds, int Sample);

/// <summary>
/// Evaluates a model the honest way: anchored/expanding walk-forward. The data is walked
/// chronologically in several sequential test blocks; for each block the model is retrained ONLY
/// on the past, then scored on the unseen block. Two guards prevent the subtle leakage that makes
/// naive backtests look better than reality:
///   • PURGE  — because a label at bar i looks <c>horizon</c> bars into the future, the last
///              <c>horizon</c> training samples before a test block would peek into it; they are dropped.
///   • EMBARGO — a few extra samples are dropped after the purge so serial correlation across the
///              boundary can't leak either.
/// The reported accuracy is therefore a robust estimate of true out-of-sample performance, not a
/// single lucky split. On synthetic random-walk data it correctly lands near ~50%.
/// </summary>
public sealed class WalkForwardValidator
{
    /// <param name="x">Causal feature rows, chronological.</param>
    /// <param name="y">Binary labels (1 = up after horizon).</param>
    /// <param name="horizon">Label look-ahead in bars — also the purge width.</param>
    public WalkForwardResult Evaluate(
        IReadOnlyList<double[]> x, IReadOnlyList<int> y, int horizon,
        int folds = 5, int embargo = 2, double l2 = 1e-3)
    {
        int n = x.Count;
        if (n < 60) return new WalkForwardResult(0, 0, 0, 0);

        // Reserve the back half of the series for testing, split into `folds` sequential blocks.
        int minTrain = Math.Max(40, n / 2);
        int testTotal = n - minTrain;
        if (testTotal < folds) return new WalkForwardResult(0, 0, 0, 0);
        int block = testTotal / folds;

        int correct = 0, total = 0;
        double brierSum = 0;

        for (int k = 0; k < folds; k++)
        {
            int testStart = minTrain + k * block;
            int testEnd = (k == folds - 1) ? n : testStart + block; // last fold soaks up the remainder
            int trainEnd = testStart - horizon - embargo;           // purge + embargo before the block
            if (trainEnd < 30) continue;

            var xTrain = new List<double[]>(trainEnd);
            var yTrain = new List<int>(trainEnd);
            for (int i = 0; i < trainEnd; i++) { xTrain.Add(x[i]); yTrain.Add(y[i]); }

            var model = new LogisticRegressionModel();
            model.Train(xTrain, yTrain, l2: l2);

            for (int i = testStart; i < testEnd; i++)
            {
                double p = model.PredictProba(x[i]);
                if ((p >= 0.5 ? 1 : 0) == y[i]) correct++;
                brierSum += (p - y[i]) * (p - y[i]);
                total++;
            }
        }

        if (total == 0) return new WalkForwardResult(0, 0, 0, 0);
        return new WalkForwardResult(
            Math.Round((double)correct / total, 4),
            Math.Round(brierSum / total, 4),
            folds, total);
    }
}
