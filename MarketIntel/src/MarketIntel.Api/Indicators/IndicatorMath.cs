namespace MarketIntel.Api.Indicators;

/// <summary>
/// Pure, side-effect-free technical-indicator functions. All operate on plain double arrays
/// (oldest first) so they are trivially unit-testable and reusable across engines.
/// </summary>
public static class IndicatorMath
{
    /// <summary>Simple moving average of the last <paramref name="period"/> values.</summary>
    public static double Sma(ReadOnlySpan<double> values, int period)
    {
        if (values.Length < period || period <= 0) return double.NaN;
        double sum = 0;
        for (int i = values.Length - period; i < values.Length; i++) sum += values[i];
        return sum / period;
    }

    /// <summary>Exponential moving average series (same length as input; leading values warm up).</summary>
    public static double[] EmaSeries(ReadOnlySpan<double> values, int period)
    {
        var result = new double[values.Length];
        if (values.Length == 0) return result;
        double k = 2.0 / (period + 1);
        double ema = values[0];
        result[0] = ema;
        for (int i = 1; i < values.Length; i++)
        {
            ema = values[i] * k + ema * (1 - k);
            result[i] = ema;
        }
        return result;
    }

    /// <summary>Latest EMA value.</summary>
    public static double Ema(ReadOnlySpan<double> values, int period)
    {
        var s = EmaSeries(values, period);
        return s.Length == 0 ? double.NaN : s[^1];
    }

    /// <summary>Wilder's RSI over <paramref name="period"/> (default 14). Range 0-100.</summary>
    public static double Rsi(ReadOnlySpan<double> closes, int period = 14)
    {
        if (closes.Length <= period) return double.NaN;
        double gain = 0, loss = 0;
        for (int i = 1; i <= period; i++)
        {
            double diff = closes[i] - closes[i - 1];
            if (diff >= 0) gain += diff; else loss -= diff;
        }
        double avgGain = gain / period, avgLoss = loss / period;
        for (int i = period + 1; i < closes.Length; i++)
        {
            double diff = closes[i] - closes[i - 1];
            double up = diff > 0 ? diff : 0;
            double down = diff < 0 ? -diff : 0;
            avgGain = (avgGain * (period - 1) + up) / period;
            avgLoss = (avgLoss * (period - 1) + down) / period;
        }
        if (avgLoss == 0) return 100;
        double rs = avgGain / avgLoss;
        return 100 - 100 / (1 + rs);
    }

    /// <summary>MACD: (line, signal, histogram) using the latest values.</summary>
    public static (double Macd, double Signal, double Histogram) Macd(
        ReadOnlySpan<double> closes, int fast = 12, int slow = 26, int signal = 9)
    {
        if (closes.Length < slow + signal) return (double.NaN, double.NaN, double.NaN);
        var emaFast = EmaSeries(closes, fast);
        var emaSlow = EmaSeries(closes, slow);
        var macdLine = new double[closes.Length];
        for (int i = 0; i < closes.Length; i++) macdLine[i] = emaFast[i] - emaSlow[i];
        var signalLine = EmaSeries(macdLine, signal);
        double m = macdLine[^1], s = signalLine[^1];
        return (m, s, m - s);
    }

    /// <summary>Average True Range (Wilder). Absolute price units.</summary>
    public static double Atr(ReadOnlySpan<double> high, ReadOnlySpan<double> low, ReadOnlySpan<double> close, int period = 14)
    {
        int n = close.Length;
        if (n <= period) return double.NaN;
        double atr = 0;
        for (int i = 1; i <= period; i++) atr += TrueRange(high[i], low[i], close[i - 1]);
        atr /= period;
        for (int i = period + 1; i < n; i++)
            atr = (atr * (period - 1) + TrueRange(high[i], low[i], close[i - 1])) / period;
        return atr;
    }

    private static double TrueRange(double h, double l, double prevClose)
        => Math.Max(h - l, Math.Max(Math.Abs(h - prevClose), Math.Abs(l - prevClose)));

    /// <summary>Session VWAP over the supplied bars (typical price weighted by volume).</summary>
    public static double Vwap(ReadOnlySpan<double> high, ReadOnlySpan<double> low, ReadOnlySpan<double> close, ReadOnlySpan<double> volume)
    {
        double pv = 0, vol = 0;
        for (int i = 0; i < close.Length; i++)
        {
            double typical = (high[i] + low[i] + close[i]) / 3.0;
            pv += typical * volume[i];
            vol += volume[i];
        }
        return vol == 0 ? double.NaN : pv / vol;
    }

    /// <summary>Bollinger Bands: (upper, middle, lower) using SMA +/- k*stddev.</summary>
    public static (double Upper, double Middle, double Lower) Bollinger(ReadOnlySpan<double> closes, int period = 20, double k = 2.0)
    {
        if (closes.Length < period) return (double.NaN, double.NaN, double.NaN);
        double mid = Sma(closes, period);
        double sumSq = 0;
        for (int i = closes.Length - period; i < closes.Length; i++)
        {
            double d = closes[i] - mid;
            sumSq += d * d;
        }
        double sd = Math.Sqrt(sumSq / period);
        return (mid + k * sd, mid, mid - k * sd);
    }

    /// <summary>Relative volume: latest bar volume divided by the average of the prior N bars.</summary>
    public static double RelativeVolume(ReadOnlySpan<double> volume, int period = 20)
    {
        if (volume.Length <= period) return double.NaN;
        double sum = 0;
        for (int i = volume.Length - period - 1; i < volume.Length - 1; i++) sum += volume[i];
        double avg = sum / period;
        return avg == 0 ? double.NaN : volume[^1] / avg;
    }

    /// <summary>ADX trend-strength (Wilder), 0-100. >25 typically means a real trend.</summary>
    public static double Adx(ReadOnlySpan<double> high, ReadOnlySpan<double> low, ReadOnlySpan<double> close, int period = 14)
    {
        int n = close.Length;
        if (n <= period * 2) return double.NaN;

        var dx = new List<double>();
        double smPlus = 0, smMinus = 0, smTr = 0;
        for (int i = 1; i < n; i++)
        {
            double up = high[i] - high[i - 1];
            double down = low[i - 1] - low[i];
            double plusDm = up > down && up > 0 ? up : 0;
            double minusDm = down > up && down > 0 ? down : 0;
            double tr = TrueRange(high[i], low[i], close[i - 1]);

            if (i <= period)
            {
                smPlus += plusDm; smMinus += minusDm; smTr += tr;
                if (i == period && smTr > 0)
                {
                    double pdi = 100 * smPlus / smTr;
                    double mdi = 100 * smMinus / smTr;
                    dx.Add(pdi + mdi == 0 ? 0 : 100 * Math.Abs(pdi - mdi) / (pdi + mdi));
                }
            }
            else
            {
                smPlus = smPlus - smPlus / period + plusDm;
                smMinus = smMinus - smMinus / period + minusDm;
                smTr = smTr - smTr / period + tr;
                if (smTr > 0)
                {
                    double pdi = 100 * smPlus / smTr;
                    double mdi = 100 * smMinus / smTr;
                    dx.Add(pdi + mdi == 0 ? 0 : 100 * Math.Abs(pdi - mdi) / (pdi + mdi));
                }
            }
        }

        if (dx.Count < period) return dx.Count > 0 ? dx.Average() : double.NaN;
        double adx = dx.Take(period).Average();
        for (int i = period; i < dx.Count; i++) adx = (adx * (period - 1) + dx[i]) / period;
        return adx;
    }
}
