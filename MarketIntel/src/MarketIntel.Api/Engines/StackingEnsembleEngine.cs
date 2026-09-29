using MarketIntel.Api.Indicators;
using MarketIntel.Api.Localization;
using MarketIntel.Api.MarketData;
using MarketIntel.Api.Ml;
using MarketIntel.Shared.Contracts;

namespace MarketIntel.Api.Engines;

/// <summary>
/// The LEARNED ensemble. The static <see cref="EnsembleEngine"/> combines the base models with
/// weights fixed by hand; this engine instead trains a meta-model (logistic regression) on the base
/// models' own historical outputs so the combination is LEARNED from what actually preceded up moves.
/// This is the models genuinely "working together": each base model votes, and a meta-model learns
/// how much to trust each vote — the rigorous, measurable cousin of asking several assistants and
/// synthesising their answers.
///
/// It reports its own walk-forward accuracy (via <see cref="WalkForwardValidator"/>) so we can tell
/// whether learning the weights actually beats the fixed ones, and it exposes the learned weight of
/// every base model so the result stays explainable. On synthetic data it lands near chance — the
/// learned weights, not a manufactured edge, are the honest output.
/// </summary>
public sealed class StackingEnsembleEngine
{
    private readonly IMarketDataProvider _data;
    private readonly WalkForwardValidator _validator;

    private const int Horizon = 5;
    private const int BarCount = 320;
    private const int Warmup = 55;
    private const double L2 = 1e-3;

    // Base models the meta-learner combines, in the exact column order of the feature vector.
    private static readonly string[] BaseIds = ["Technical", "Momentum", "Volume", "Volatility"];

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Dictionary<string, (LearnedEnsembleDto Dto, int Day)> _cache = new();

    public StackingEnsembleEngine(IMarketDataProvider data, WalkForwardValidator validator)
    {
        _data = data;
        _validator = validator;
    }

    public async Task<LearnedEnsembleDto?> BuildAsync(string symbol, CancellationToken ct = default)
    {
        symbol = symbol.ToUpperInvariant();
        int day = (int)(DateTime.UtcNow.Date.ToBinary() % int.MaxValue);
        if (_cache.TryGetValue(symbol, out var c) && c.Day == day) return c.Dto;

        await _lock.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(symbol, out c) && c.Day == day) return c.Dto;

            var candles = await _data.GetCandlesAsync(symbol, Timeframe.D1, BarCount, ct);
            if (candles.Count < Warmup + Horizon + 20) return null;

            var close = candles.Select(x => (double)x.Close).ToArray();
            var high = candles.Select(x => (double)x.High).ToArray();
            var low = candles.Select(x => (double)x.Low).ToArray();
            var vol = candles.Select(x => (double)x.Volume).ToArray();
            var ema9 = IndicatorMath.EmaSeries(close, 9);
            var ema20 = IndicatorMath.EmaSeries(close, 20);
            var ema50 = IndicatorMath.EmaSeries(close, 50);

            // Causal dataset: each row is the base models' probabilities at bar i; label = up after horizon.
            var x = new List<double[]>();
            var y = new List<int>();
            for (int i = Warmup; i < close.Length - Horizon; i++)
            {
                x.Add(BaseVector(close, high, low, vol, ema9, ema20, ema50, i));
                y.Add(close[i + Horizon] > close[i] ? 1 : 0);
            }
            if (x.Count < 40) return null;

            // Honest out-of-sample accuracy of the LEARNED combination (same purge+embargo discipline).
            var wf = _validator.Evaluate(x, y, Horizon, l2: L2);

            // Train the meta-learner on all rows and read off the current combined probability.
            var meta = new LogisticRegressionModel();
            meta.Train(x, y, l2: L2);

            var latest = BaseVector(close, high, low, vol, ema9, ema20, ema50, close.Length - 1);
            double prob = meta.PredictProba(latest);

            // Learned weight of each base model = its standardized-weight magnitude, normalized to sum 1.
            var w = meta.Weights;
            double absSum = 0;
            for (int k = 0; k < BaseIds.Length && k < w.Count; k++) absSum += Math.Abs(w[k]);

            var models = new List<ModelOpinionDto>();
            for (int k = 0; k < BaseIds.Length; k++)
            {
                double learnedWeight = absSum > 0 && k < w.Count ? Math.Abs(w[k]) / absSum : 0;
                models.Add(new ModelOpinionDto(Loc.T($"Model.{BaseIds[k]}"), Math.Round(latest[k], 2), Math.Round(learnedWeight, 2)));
            }

            var dto = new LearnedEnsembleDto(
                Math.Round(prob, 3),
                wf.Accuracy,
                wf.Sample,
                Loc.T("Learned.Note"),
                models);

            _cache[symbol] = (dto, day);
            return dto;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// The base models' bullish probabilities at bar <paramref name="i"/>, using only data up to i.
    /// Order matches <see cref="BaseIds"/>: Technical, Momentum, Volume, Volatility.
    /// </summary>
    private static double[] BaseVector(
        double[] close, double[] high, double[] low, double[] vol,
        double[] ema9, double[] ema20, double[] ema50, int i)
    {
        int w = 40;
        int start = Math.Max(0, i - w + 1);
        int len = i - start + 1;
        var cw = close.AsSpan(start, len);
        var hw = high.AsSpan(start, len);
        var lw = low.AsSpan(start, len);
        var vw = vol.AsSpan(start, len);
        double price = close[i];

        // Technical: EMA alignment (same shape as the live EnsembleEngine).
        double trend = 0;
        trend += price > ema9[i] ? 1 : -1;
        trend += price > ema20[i] ? 1 : -1;
        trend += price > ema50[i] ? 1 : -1;
        trend += ema9[i] > ema20[i] ? 1 : -1;
        trend += ema20[i] > ema50[i] ? 1 : -1;
        trend /= 5.0;

        // Momentum: RSI + MACD histogram (normalized by ATR).
        double atr = IndicatorMath.Atr(hw, lw, cw);
        double rsi = IndicatorMath.Rsi(cw);
        var (_, _, macdHist) = IndicatorMath.Macd(cw);
        double mom = 0;
        if (!double.IsNaN(rsi)) mom += Math.Clamp((rsi - 50) / 50.0, -1, 1);
        if (!double.IsNaN(macdHist) && atr > 0) mom += Math.Clamp(macdHist / (atr * 0.5), -1, 1);
        mom = Math.Clamp(mom / 2.0, -1, 1);

        // Volume: relative volume in the current direction (vs EMA20 as an intraday-VWAP proxy).
        double relVol = IndicatorMath.RelativeVolume(vw);
        double volMag = double.IsNaN(relVol) ? 0 : Math.Clamp((relVol - 1.0) / 1.0, 0, 1);
        double volFactor = volMag * (price >= ema20[i] ? 1 : -1);

        // Volatility: position within the Bollinger band.
        var (bbU, bbM, _) = IndicatorMath.Bollinger(cw);
        double volatility = bbU - bbM > 0 ? Math.Clamp((price - bbM) / (bbU - bbM), -1, 1) : 0;

        return
        [
            ToProb(trend),
            ToProb(mom),
            ToProb(volFactor),
            ToProb(volatility)
        ];
    }

    private static double ToProb(double factor) => Math.Clamp(0.5 + 0.5 * factor, 0.02, 0.98);
}
