using MarketIntel.Api.Indicators;
using MarketIntel.Shared.Contracts;

namespace MarketIntel.Api.Ml;

/// <summary>
/// Turns candles into numeric feature vectors for the ML model. Every feature at bar i is
/// computed from data up to and including i (no look-ahead), so datasets are causal.
/// </summary>
public sealed class FeatureExtractor
{
    /// <summary>Number of features produced.</summary>
    public const int FeatureCount = 12;

    /// <summary>Bars of history required before features are meaningful (EMA50/ADX warm-up).</summary>
    public const int Warmup = 60;

    public double[] Extract(double[] close, double[] high, double[] low, double[] vol, int i)
    {
        var c = close.AsSpan(0, i + 1);
        var h = high.AsSpan(0, i + 1);
        var l = low.AsSpan(0, i + 1);
        var v = vol.AsSpan(0, i + 1);

        double price = close[i];
        double ema9 = IndicatorMath.Ema(c, 9);
        double ema20 = IndicatorMath.Ema(c, 20);
        double ema50 = IndicatorMath.Ema(c, 50);
        double rsi = IndicatorMath.Rsi(c);
        var (_, _, macdHist) = IndicatorMath.Macd(c);
        double atr = IndicatorMath.Atr(h, l, c);
        double vwap = IndicatorMath.Vwap(h, l, c, v);
        double relVol = IndicatorMath.RelativeVolume(v);
        double adx = IndicatorMath.Adx(h, l, c);
        var (bbU, bbM, _) = IndicatorMath.Bollinger(c);

        double ret5 = i >= 5 ? close[i] / close[i - 5] - 1 : 0;
        double ret10 = i >= 10 ? close[i] / close[i - 10] - 1 : 0;

        var f = new double[FeatureCount];
        f[0] = Safe((price - ema9) / price);
        f[1] = Safe((price - ema20) / price);
        f[2] = Safe((price - ema50) / price);
        f[3] = Safe((ema9 - ema20) / price);
        f[4] = Safe((rsi - 50) / 50);
        f[5] = atr > 0 ? Clamp(macdHist / atr, -3, 3) : 0;
        f[6] = vwap > 0 ? Safe((price - vwap) / vwap) : 0;
        f[7] = Clamp(Safe(relVol - 1), -2, 4);
        f[8] = Safe((adx - 20) / 20);
        f[9] = bbU - bbM > 0 ? Clamp((price - bbM) / (bbU - bbM), -3, 3) : 0;
        f[10] = Clamp(ret5, -0.5, 0.5);
        f[11] = Clamp(ret10, -0.5, 0.5);
        return f;
    }

    /// <summary>Feature vector for the most recent bar.</summary>
    public double[] ExtractLatest(IReadOnlyList<CandleDto> candles)
    {
        var (close, high, low, vol) = ToArrays(candles);
        return Extract(close, high, low, vol, candles.Count - 1);
    }

    /// <summary>Causal labeled dataset: features at i, label = did close rise after <paramref name="horizon"/> bars.</summary>
    public (List<double[]> X, List<int> Y) BuildDataset(IReadOnlyList<CandleDto> candles, int horizon)
    {
        var (close, high, low, vol) = ToArrays(candles);
        var x = new List<double[]>();
        var y = new List<int>();
        for (int i = Warmup; i < candles.Count - horizon; i++)
        {
            x.Add(Extract(close, high, low, vol, i));
            y.Add(close[i + horizon] > close[i] ? 1 : 0);
        }
        return (x, y);
    }

    private static (double[] c, double[] h, double[] l, double[] v) ToArrays(IReadOnlyList<CandleDto> candles)
        => (candles.Select(c => (double)c.Close).ToArray(),
            candles.Select(c => (double)c.High).ToArray(),
            candles.Select(c => (double)c.Low).ToArray(),
            candles.Select(c => (double)c.Volume).ToArray());

    private static double Safe(double v) => double.IsNaN(v) || double.IsInfinity(v) ? 0 : v;
    private static double Clamp(double v, double lo, double hi) => Math.Clamp(Safe(v), lo, hi);
}
