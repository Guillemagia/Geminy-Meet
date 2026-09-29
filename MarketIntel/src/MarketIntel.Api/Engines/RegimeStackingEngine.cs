using MarketIntel.Api.Indicators;
using MarketIntel.Api.Localization;
using MarketIntel.Api.MarketData;
using MarketIntel.Api.Ml;
using MarketIntel.Shared.Contracts;

namespace MarketIntel.Api.Engines;

/// <summary>
/// Regime-conditional stacking. A model's edge is not constant: trend and momentum tend to pay in
/// trending regimes, volatility and mean-reversion in choppy or wild ones. So instead of one set of
/// learned weights, this engine learns a SEPARATE meta-model for each local regime (Trending /
/// Ranging / High-volatility, all derived causally from the symbol's own bars) and, at inference,
/// applies the weights learned for the regime the symbol is in right now.
///
/// Honesty is preserved with a REGIME-ROUTED walk-forward: for every past test bar the prediction
/// uses only a model trained on earlier bars OF THE SAME REGIME (with purge + embargo), so the
/// reported accuracy is a true out-of-sample estimate of the regime-aware scheme — not a fit to noise.
/// </summary>
public sealed class RegimeStackingEngine
{
    private readonly IMarketDataProvider _data;

    private const int Horizon = 5;
    private const int BarCount = 320;
    private const int Warmup = 55;
    private const int Embargo = 2;
    private const int Folds = 5;
    private const double L2 = 1e-3;
    private const int MinRegimeRows = 20; // fewer than this in a regime -> fall back to the all-regime model

    private static readonly string[] BaseIds = ["Technical", "Momentum", "Volume", "Volatility"];
    private static readonly string[] RegimeKeys = ["Regime.Local.Trending", "Regime.Local.Ranging", "Regime.Local.HighVol"];
    private const int RegimeCount = 3;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Dictionary<string, (RegimeWeightingDto Dto, int Day)> _cache = new();

    public RegimeStackingEngine(IMarketDataProvider data) => _data = data;

    public async Task<RegimeWeightingDto?> BuildAsync(string symbol, CancellationToken ct = default)
    {
        symbol = symbol.ToUpperInvariant();
        int day = (int)(DateTime.UtcNow.Date.ToBinary() % int.MaxValue);
        if (_cache.TryGetValue(symbol, out var c) && c.Day == day) return c.Dto;

        await _lock.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(symbol, out c) && c.Day == day) return c.Dto;

            var candles = await _data.GetCandlesAsync(symbol, Timeframe.D1, BarCount, ct);
            if (candles.Count < Warmup + Horizon + 40) return null;

            var close = candles.Select(x => (double)x.Close).ToArray();
            var high = candles.Select(x => (double)x.High).ToArray();
            var low = candles.Select(x => (double)x.Low).ToArray();
            var vol = candles.Select(x => (double)x.Volume).ToArray();
            var ema9 = IndicatorMath.EmaSeries(close, 9);
            var ema20 = IndicatorMath.EmaSeries(close, 20);
            var ema50 = IndicatorMath.EmaSeries(close, 50);

            // Causal rows: base-model vector, the regime in force at that bar, and the outcome.
            var rows = new List<(double[] X, int Regime, int Y)>();
            for (int i = Warmup; i < close.Length - Horizon; i++)
            {
                var (vec, regime) = VectorAndRegime(close, high, low, vol, ema9, ema20, ema50, i);
                rows.Add((vec, regime, close[i + Horizon] > close[i] ? 1 : 0));
            }
            if (rows.Count < 60) return null;

            var (acc, sample) = RoutedWalkForward(rows);

            // Current regime and the model trained for it (fall back to all-regime if that regime is thin).
            var (curVec, curRegime) = VectorAndRegime(close, high, low, vol, ema9, ema20, ema50, close.Length - 1);
            var regimeRows = rows.Where(r => r.Regime == curRegime).ToList();
            var trainRows = regimeRows.Count >= MinRegimeRows ? regimeRows : rows;

            var model = new LogisticRegressionModel();
            model.Train(trainRows.Select(r => r.X).ToList(), trainRows.Select(r => r.Y).ToList(), l2: L2);
            double prob = model.PredictProba(curVec);

            var w = model.Weights;
            double absSum = 0;
            for (int k = 0; k < BaseIds.Length && k < w.Count; k++) absSum += Math.Abs(w[k]);

            var models = new List<ModelOpinionDto>();
            for (int k = 0; k < BaseIds.Length; k++)
            {
                double weight = absSum > 0 && k < w.Count ? Math.Abs(w[k]) / absSum : 0;
                models.Add(new ModelOpinionDto(Loc.T($"Model.{BaseIds[k]}"), Math.Round(curVec[k], 2), Math.Round(weight, 2)));
            }

            var dto = new RegimeWeightingDto(
                Loc.T(RegimeKeys[curRegime]),
                Math.Round(prob, 3),
                acc, sample,
                Loc.T("RegimeWeight.Note"),
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
    /// Regime-routed, purged walk-forward. Each test bar is predicted by a model trained only on
    /// earlier bars of the SAME regime; if that regime is too thin in the window, an all-regime model
    /// trained on the same window is used instead. Returns (accuracy, sample).
    /// </summary>
    private static (double Accuracy, int Sample) RoutedWalkForward(List<(double[] X, int Regime, int Y)> rows)
    {
        int n = rows.Count;
        int minTrain = Math.Max(60, n / 2);
        int testTotal = n - minTrain;
        if (testTotal < Folds) return (0, 0);
        int block = testTotal / Folds;

        int correct = 0, total = 0;

        for (int k = 0; k < Folds; k++)
        {
            int testStart = minTrain + k * block;
            int testEnd = (k == Folds - 1) ? n : testStart + block;
            int trainEnd = testStart - Horizon - Embargo; // purge + embargo
            if (trainEnd < 40) continue;

            // Train one model per regime on the past window, plus an all-regime fallback.
            var perRegime = new LogisticRegressionModel?[RegimeCount];
            for (int r = 0; r < RegimeCount; r++)
            {
                var xs = new List<double[]>();
                var ys = new List<int>();
                for (int i = 0; i < trainEnd; i++)
                    if (rows[i].Regime == r) { xs.Add(rows[i].X); ys.Add(rows[i].Y); }
                if (xs.Count >= MinRegimeRows)
                {
                    var m = new LogisticRegressionModel();
                    m.Train(xs, ys, l2: L2);
                    perRegime[r] = m;
                }
            }
            var fallback = new LogisticRegressionModel();
            fallback.Train(
                Enumerable.Range(0, trainEnd).Select(i => rows[i].X).ToList(),
                Enumerable.Range(0, trainEnd).Select(i => rows[i].Y).ToList(), l2: L2);

            for (int i = testStart; i < testEnd; i++)
            {
                var m = perRegime[rows[i].Regime] ?? fallback;
                double p = m.PredictProba(rows[i].X);
                if ((p >= 0.5 ? 1 : 0) == rows[i].Y) correct++;
                total++;
            }
        }

        return total == 0 ? (0, 0) : (Math.Round((double)correct / total, 4), total);
    }

    /// <summary>Base-model probabilities plus the causal local regime (0=Trending,1=Ranging,2=HighVol) at bar i.</summary>
    private static (double[] Vec, int Regime) VectorAndRegime(
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

        double trend = 0;
        trend += price > ema9[i] ? 1 : -1;
        trend += price > ema20[i] ? 1 : -1;
        trend += price > ema50[i] ? 1 : -1;
        trend += ema9[i] > ema20[i] ? 1 : -1;
        trend += ema20[i] > ema50[i] ? 1 : -1;
        trend /= 5.0;

        double atr = IndicatorMath.Atr(hw, lw, cw);
        double rsi = IndicatorMath.Rsi(cw);
        var (_, _, macdHist) = IndicatorMath.Macd(cw);
        double mom = 0;
        if (!double.IsNaN(rsi)) mom += Math.Clamp((rsi - 50) / 50.0, -1, 1);
        if (!double.IsNaN(macdHist) && atr > 0) mom += Math.Clamp(macdHist / (atr * 0.5), -1, 1);
        mom = Math.Clamp(mom / 2.0, -1, 1);

        double relVol = IndicatorMath.RelativeVolume(vw);
        double volMag = double.IsNaN(relVol) ? 0 : Math.Clamp((relVol - 1.0) / 1.0, 0, 1);
        double volFactor = volMag * (price >= ema20[i] ? 1 : -1);

        var (bbU, bbM, _) = IndicatorMath.Bollinger(cw);
        double volatility = bbU - bbM > 0 ? Math.Clamp((price - bbM) / (bbU - bbM), -1, 1) : 0;

        var vec = new[] { ToProb(trend), ToProb(mom), ToProb(volFactor), ToProb(volatility) };

        // --- causal local regime ---
        double adx = IndicatorMath.Adx(hw, lw, cw);
        // High volatility when ATR%% sits well above its trailing typical level.
        double atrPct = atr > 0 && price > 0 ? atr / price : 0;
        double medianAtrPct = TrailingMedianAtrPct(close, high, low, i, 30);
        int regime;
        if (medianAtrPct > 0 && atrPct > 1.4 * medianAtrPct) regime = 2;      // HighVol
        else if (!double.IsNaN(adx) && adx >= 22) regime = 0;                  // Trending
        else regime = 1;                                                       // Ranging
        return (vec, regime);
    }

    /// <summary>Median ATR%/price over the trailing window ending at i (causal), for a volatility baseline.</summary>
    private static double TrailingMedianAtrPct(double[] close, double[] high, double[] low, int i, int window)
    {
        int start = Math.Max(14, i - window + 1);
        var vals = new List<double>();
        for (int j = start; j <= i; j++)
        {
            int s = Math.Max(0, j - 14 + 1);
            int len = j - s + 1;
            double atr = IndicatorMath.Atr(high.AsSpan(s, len), low.AsSpan(s, len), close.AsSpan(s, len));
            if (!double.IsNaN(atr) && close[j] > 0) vals.Add(atr / close[j]);
        }
        if (vals.Count == 0) return 0;
        vals.Sort();
        return vals[vals.Count / 2];
    }

    private static double ToProb(double factor) => Math.Clamp(0.5 + 0.5 * factor, 0.02, 0.98);
}
