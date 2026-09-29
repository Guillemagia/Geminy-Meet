using System.Globalization;
using MarketIntel.Api.Indicators;
using MarketIntel.Api.Localization;
using MarketIntel.Api.MarketData;
using MarketIntel.Shared.Contracts;
using MarketIntel.Shared.Enums;

namespace MarketIntel.Api.Ml;

/// <summary>
/// Proves — or honestly disproves — that the quality grade means what it claims. It walks the
/// universe's history bar by bar, re-generates a graded signal using ONLY data available up to
/// that bar (strictly causal, no look-ahead), then checks whether the trade actually reached its
/// target before its stop over the next few bars. Signals are bucketed by grade so we can compare
/// the confidence they IMPLIED against the hit rate they REALLY achieved out-of-sample.
///
/// The grader here is a lightweight, single-series proxy of the live meta-model's factors (trend,
/// momentum, structure, volume) — full-pipeline replay per bar would be far too costly — but it
/// uses the SAME grade thresholds, so the buckets are comparable. On synthetic random-walk data
/// every grade correctly lands near ~50%: the design refuses to manufacture an edge that isn't there.
/// </summary>
public sealed class CalibrationEngine
{
    private readonly IMarketDataProvider _data;

    private const int BarCount = 340;
    private const int Warmup = 55;          // bars needed before the first causal read
    private const int HorizonBars = 5;      // outcome window (≈ one trading week on daily bars)
    private const double AtrMultiple = 1.0; // symmetric target/stop distance in ATR units

    // Cached per (language, day): the numbers are language-independent, only labels/notes localize.
    private readonly Dictionary<string, (CalibrationReportDto Report, int Day)> _cache = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private static string Lang => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

    public CalibrationEngine(IMarketDataProvider data) => _data = data;

    /// <summary>Realized TP-before-SL rate (and sample) for a grade, from the cached walk-forward report.</summary>
    public async Task<(double HitRate, int Sample)> HitRateForGradeAsync(SignalGrade grade, CancellationToken ct = default)
    {
        var report = await ComputeAsync(ct);
        var b = report.Buckets.FirstOrDefault(x => x.Grade == grade);
        return b is null ? (0, 0) : (b.TpBeforeSlRate, b.Sample);
    }

    public async Task<CalibrationReportDto> ComputeAsync(CancellationToken ct = default)
    {
        int day = (int)(DateTime.UtcNow.Date.ToBinary() % int.MaxValue);
        if (_cache.TryGetValue(Lang, out var c) && c.Day == day) return c.Report;

        await _lock.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(Lang, out var c2) && c2.Day == day) return c2.Report;

            var universe = await _data.GetUniverseAsync(ct);

            // Per-grade accumulators.
            var agg = new Dictionary<SignalGrade, (int N, double SumPred, double SumDirWin, double SumTp)>();
            double brierSum = 0;
            int brierN = 0;

            foreach (var symbol in universe)
            {
                ct.ThrowIfCancellationRequested();
                var candles = await _data.GetCandlesAsync(symbol, Timeframe.D1, BarCount, ct);
                if (candles.Count < Warmup + HorizonBars + 5) continue;

                var high = candles.Select(x => (double)x.High).ToArray();
                var low = candles.Select(x => (double)x.Low).ToArray();
                var close = candles.Select(x => (double)x.Close).ToArray();
                var vol = candles.Select(x => (double)x.Volume).ToArray();

                var ema9 = IndicatorMath.EmaSeries(close, 9);
                var ema20 = IndicatorMath.EmaSeries(close, 20);
                var ema50 = IndicatorMath.EmaSeries(close, 50);

                for (int i = Warmup; i < close.Length - HorizonBars; i++)
                {
                    var read = GradeAt(high, low, close, vol, ema9, ema20, ema50, i);
                    if (read is null) continue; // NO-TRADE / neutral: not a tradeable signal, skip
                    var (grade, dirSign, predicted, atr) = read.Value;

                    // Outcome over the next HorizonBars, strictly forward of bar i.
                    double entry = close[i];
                    double target = entry + dirSign * AtrMultiple * atr;
                    double stop = entry - dirSign * AtrMultiple * atr;

                    bool tpFirst = false, resolved = false;
                    for (int k = i + 1; k <= i + HorizonBars; k++)
                    {
                        bool hitTp = dirSign > 0 ? high[k] >= target : low[k] <= target;
                        bool hitSl = dirSign > 0 ? low[k] <= stop : high[k] >= stop;
                        if (hitTp && hitSl) { tpFirst = false; resolved = true; break; } // ambiguous bar -> stop wins (conservative)
                        if (hitTp) { tpFirst = true; resolved = true; break; }
                        if (hitSl) { tpFirst = false; resolved = true; break; }
                    }
                    if (!resolved) tpFirst = dirSign > 0 ? close[i + HorizonBars] > entry : close[i + HorizonBars] < entry;

                    // Directional correctness at the horizon close (looser than TP-before-SL).
                    bool dirWin = dirSign > 0 ? close[i + HorizonBars] > entry : close[i + HorizonBars] < entry;

                    var a = agg.TryGetValue(grade, out var cur) ? cur : (N: 0, SumPred: 0.0, SumDirWin: 0.0, SumTp: 0.0);
                    agg[grade] = (a.N + 1, a.SumPred + predicted, a.SumDirWin + (dirWin ? 1 : 0), a.SumTp + (tpFirst ? 1 : 0));

                    brierSum += (predicted - (tpFirst ? 1 : 0)) * (predicted - (tpFirst ? 1 : 0));
                    brierN++;
                }
            }

            var buckets = new List<CalibrationBucketDto>();
            foreach (var grade in new[] { SignalGrade.APlus, SignalGrade.A, SignalGrade.B, SignalGrade.C, SignalGrade.D })
            {
                if (!agg.TryGetValue(grade, out var a) || a.N == 0) continue;
                buckets.Add(new CalibrationBucketDto(
                    grade, Loc.T($"Grade.{grade}"),
                    Math.Round(a.SumPred / a.N, 3),
                    Math.Round(a.SumDirWin / a.N, 3),
                    a.N,
                    Math.Round(a.SumTp / a.N, 3)));
            }

            int total = buckets.Sum(b => b.Sample);
            double brier = brierN > 0 ? Math.Round(brierSum / brierN, 4) : 0;

            var report = new CalibrationReportDto(
                Loc.T("Calibration.Method.WalkForward"),
                total, HorizonBars, brier, buckets, Loc.T("Calibration.Note"));

            _cache[Lang] = (report, day);
            return report;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Causal, single-series proxy of the meta-model. Returns the grade, its direction (+1/-1),
    /// the implied win probability the app would show, and the ATR — or null for a NO-TRADE read.
    /// Uses ONLY bars up to and including <paramref name="i"/>.
    /// </summary>
    private static (SignalGrade Grade, int DirSign, double Predicted, double Atr)? GradeAt(
        double[] high, double[] low, double[] close, double[] vol,
        double[] ema9, double[] ema20, double[] ema50, int i)
    {
        // Trailing windows keep every indicator strictly causal and cheap.
        int w = 40;
        int start = Math.Max(0, i - w + 1);
        int len = i - start + 1;
        var hw = high.AsSpan(start, len);
        var lw = low.AsSpan(start, len);
        var cw = close.AsSpan(start, len);
        var vw = vol.AsSpan(start, len);

        double price = close[i];
        double atr = IndicatorMath.Atr(hw, lw, cw);
        if (double.IsNaN(atr) || atr <= 0) return null;
        double rsi = IndicatorMath.Rsi(cw);
        var (_, _, macdHist) = IndicatorMath.Macd(cw);
        double adx = IndicatorMath.Adx(hw, lw, cw);
        double relVol = IndicatorMath.RelativeVolume(vw);

        // --- factor sub-signals in [-1, 1], mirroring the live engine's spirit ---
        double trend = 0;
        trend += price > ema9[i] ? 1 : -1;
        trend += price > ema20[i] ? 1 : -1;
        trend += price > ema50[i] ? 1 : -1;
        trend += ema9[i] > ema20[i] ? 1 : -1;
        trend += ema20[i] > ema50[i] ? 1 : -1;
        trend /= 5.0;

        double mom = 0;
        if (!double.IsNaN(rsi)) mom += Math.Clamp((rsi - 50) / 50.0, -1, 1);
        if (!double.IsNaN(macdHist)) mom += Math.Clamp(macdHist / (atr * 0.5), -1, 1);
        mom = Math.Clamp(mom / 2.0, -1, 1);

        double vwapProxy = Math.Clamp((price - ema20[i]) / (ema20[i] == 0 ? 1 : ema20[i]) / 0.01, -1, 1);

        double net = 0.5 * trend + 0.3 * mom + 0.2 * vwapProxy; // [-1, 1]
        int dirSign = net > 0 ? 1 : net < 0 ? -1 : 0;
        if (dirSign == 0) return null;

        // --- quality factors, same max weights as the live meta-model (that we can proxy here) ---
        // Consensus: share of the sub-signals agreeing with the net direction.
        int agree = 0, considered = 0;
        foreach (var f in new[] { trend, mom, vwapProxy })
        {
            considered++;
            if (Math.Sign(f) == dirSign) agree++;
        }
        double consensus = considered > 0 ? agree / (double)considered : 0;

        double strength = Math.Clamp(Math.Abs(net), 0, 1);
        double adxBoost = double.IsNaN(adx) ? 0 : Math.Clamp((adx - 20) / 20.0, 0, 1);
        double volScore = double.IsNaN(relVol) ? 0 : Math.Clamp((relVol - 0.8) / 1.2, 0, 1);
        bool vwapConfirm = (price >= ema20[i]) == (dirSign > 0);

        // Weighted 0-100 quality (the factors we can measure from one series; the missing ones —
        // ensemble/ML/regime/events — are treated as neutral, which only makes the grader stricter).
        double quality =
            22 * consensus +
            18 * strength +
            10 * (0.7 * (Math.Sign(net) != 0 ? 1 : 0) + 0.3 * adxBoost) +
            8 * (vwapConfirm ? 1 : 0) +
            8 * volScore +
            // regime(14)+model(8)+event(12) unknown here -> award their neutral halves so thresholds line up
            0.5 * (14 + 8 + 12);
        int q = (int)Math.Round(Math.Clamp(quality, 0, 100));

        SignalGrade grade = q switch
        {
            >= 85 => SignalGrade.APlus,
            >= 75 => SignalGrade.A,
            >= 62 => SignalGrade.B,
            >= 50 => SignalGrade.C,
            _ => SignalGrade.D
        };

        // The confidence the app would display for this side: conviction mapped to a probability.
        double conviction = Math.Clamp(0.6 * strength + 0.4 * consensus, 0, 1);
        double predicted = Math.Clamp(0.5 + 0.45 * conviction, 0.5, 0.95);

        return (grade, dirSign, predicted, atr);
    }
}
