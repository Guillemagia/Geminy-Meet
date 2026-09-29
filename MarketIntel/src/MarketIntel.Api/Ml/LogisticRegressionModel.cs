using MarketIntel.Api.Localization;

namespace MarketIntel.Api.Ml;

/// <summary>
/// A logistic-regression classifier trained in-process by gradient descent, with feature
/// standardization. This is a real (if simple) trained model — the same feature vector can
/// later feed a heavier model exported to ONNX (see <see cref="IPredictor"/>).
/// </summary>
public sealed class LogisticRegressionModel : IPredictor
{
    // Localized at access time (on the request thread), so the name matches the caller's language.
    public string Name => Loc.T("Model.LogReg.Name");

    private double[] _weights = [];
    private double _bias;
    private double[] _mean = [];
    private double[] _std = [];
    private bool _trained;

    public bool IsTrained => _trained;

    /// <summary>
    /// The learned per-feature weights (on standardized features). Their relative magnitude is a
    /// readable importance: how much the model leans on each input. Empty until trained.
    /// </summary>
    public IReadOnlyList<double> Weights => _weights;

    /// <param name="l2">L2 penalty (weight decay). Shrinks weights toward zero to curb overfitting,
    /// which matters most when a symbol has few training bars. 0 disables it.</param>
    public void Train(IReadOnlyList<double[]> x, IReadOnlyList<int> y, int epochs = 400, double lr = 0.1, double l2 = 0.0)
    {
        int n = x.Count;
        if (n == 0) return;
        int d = x[0].Length;

        // Standardize features (z-score) so gradient descent converges cleanly.
        _mean = new double[d];
        _std = new double[d];
        for (int j = 0; j < d; j++)
        {
            double sum = 0;
            for (int i = 0; i < n; i++) sum += x[i][j];
            _mean[j] = sum / n;
            double sq = 0;
            for (int i = 0; i < n; i++) { double dev = x[i][j] - _mean[j]; sq += dev * dev; }
            _std[j] = Math.Sqrt(sq / n);
            if (_std[j] < 1e-9) _std[j] = 1;
        }

        _weights = new double[d];
        _bias = 0;

        for (int epoch = 0; epoch < epochs; epoch++)
        {
            var gradW = new double[d];
            double gradB = 0;
            for (int i = 0; i < n; i++)
            {
                double z = _bias;
                for (int j = 0; j < d; j++) z += _weights[j] * Standardize(x[i][j], j);
                double p = Sigmoid(z);
                double err = p - y[i];
                for (int j = 0; j < d; j++) gradW[j] += err * Standardize(x[i][j], j);
                gradB += err;
            }
            // Gradient step with L2 weight decay (the penalty is not applied to the bias).
            for (int j = 0; j < d; j++) _weights[j] -= lr * (gradW[j] / n + l2 * _weights[j]);
            _bias -= lr * gradB / n;
        }
        _trained = true;
    }

    public double PredictProba(double[] features)
    {
        if (!_trained) return 0.5;
        double z = _bias;
        for (int j = 0; j < _weights.Length; j++) z += _weights[j] * Standardize(features[j], j);
        return Sigmoid(z);
    }

    public double PredictBullishProbability(double[] features) => PredictProba(features);

    private double Standardize(double v, int j) => (v - _mean[j]) / _std[j];
    private static double Sigmoid(double z) => 1.0 / (1.0 + Math.Exp(-z));
}
