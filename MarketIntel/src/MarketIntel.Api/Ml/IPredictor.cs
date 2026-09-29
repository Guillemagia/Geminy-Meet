namespace MarketIntel.Api.Ml;

/// <summary>
/// A model that maps a feature vector to a bullish probability. The in-process logistic model
/// implements this today; a heavier model trained offline (XGBoost/LSTM/Transformer) can be
/// exported to ONNX and dropped in behind the same interface (see <c>OnnxPredictor</c>).
/// </summary>
public interface IPredictor
{
    string Name { get; }
    double PredictBullishProbability(double[] features);
}
