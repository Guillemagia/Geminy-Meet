using System.Text.Json;
using MarketIntel.Api.Localization;
using MarketIntel.Api.MarketData;
using MarketIntel.Shared.Contracts;
using MarketIntel.Shared.Enums;

namespace MarketIntel.Api.Ml;

/// <summary>
/// The forward-only, unfalsifiable proof of edge. Every actionable graded signal the running
/// system emits is logged here with its entry, target and stop; later — once enough real bars
/// exist beyond the emit time — the outcome is resolved as target-before-stop or not. Stats are
/// aggregated by grade so the platform can eventually say, honestly, "of the last N A+ signals,
/// X% reached target before stop."
///
/// Unlike the walk-forward calibration this cannot be back-generated: it only accumulates as time
/// passes, which is exactly what makes it trustworthy. Backed by a plain JSON file so it needs no
/// database (mock-first); swap the store for a real DB when scaling.
/// </summary>
public sealed class SignalRegistry
{
    private readonly IMarketDataProvider _data;
    private readonly string _path;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private const int HorizonBars = 5;
    private const double AtrMultiple = 1.0;

    public SignalRegistry(IMarketDataProvider data, IHostEnvironment env)
    {
        _data = data;
        var dir = Path.Combine(env.ContentRootPath, "data");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "signal-registry.json");
    }

    private sealed class Record
    {
        public string Id { get; set; } = "";
        public string Symbol { get; set; } = "";
        public DateTime EmitUtc { get; set; }
        public int DirSign { get; set; }
        public int Grade { get; set; }
        public double Entry { get; set; }
        public double Target { get; set; }
        public double Stop { get; set; }
        public double Predicted { get; set; }
        public bool Resolved { get; set; }
        public bool TpFirst { get; set; }
        public bool DirWin { get; set; }
    }

    /// <summary>Log an actionable graded signal (A+/A/B). One record per symbol per day; best-effort.</summary>
    public async Task RecordAsync(SignalDto s, CancellationToken ct = default)
    {
        if (s.Quality is not { } q) return;
        if (q.Grade is not (SignalGrade.APlus or SignalGrade.A or SignalGrade.B)) return;
        if (s.Direction == SignalDirection.Neutral || s.IsNoTrade) return;
        if (s.Risk.SuggestedTarget is not decimal tgt || s.Risk.SuggestedStop is not decimal stp) return;

        int dirSign = s.Direction == SignalDirection.Bullish ? 1 : -1;

        await _lock.WaitAsync(ct);
        try
        {
            var records = Load();
            string dayKey = s.AsOfUtc.ToString("yyyyMMdd");
            if (records.Any(r => r.Symbol == s.Symbol && r.EmitUtc.ToString("yyyyMMdd") == dayKey)) return; // dedup per day

            records.Add(new Record
            {
                Id = Guid.NewGuid().ToString("N"),
                Symbol = s.Symbol,
                EmitUtc = s.AsOfUtc,
                DirSign = dirSign,
                Grade = (int)q.Grade,
                Entry = (double)s.Price,
                Target = (double)tgt,
                Stop = (double)stp,
                Predicted = q.RawConfidence
            });
            Save(records);
        }
        finally { _lock.Release(); }
    }

    /// <summary>Resolve whatever can be resolved, then aggregate by grade.</summary>
    public async Task<SignalRegistryStatsDto> GetStatsAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        List<Record> records;
        try { records = Load(); }
        finally { _lock.Release(); }

        // Resolve pending records whose outcome window has real bars beyond the emit time.
        bool changed = false;
        foreach (var r in records.Where(r => !r.Resolved))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var candles = await _data.GetCandlesAsync(r.Symbol, Timeframe.D1, 60, ct);
                var future = candles.Where(c => c.TimeUtc > r.EmitUtc).OrderBy(c => c.TimeUtc).Take(HorizonBars).ToList();
                if (future.Count < HorizonBars) continue; // not enough forward data yet -> stays pending

                bool tpFirst = false, done = false;
                foreach (var c in future)
                {
                    bool hitTp = r.DirSign > 0 ? (double)c.High >= r.Target : (double)c.Low <= r.Target;
                    bool hitSl = r.DirSign > 0 ? (double)c.Low <= r.Stop : (double)c.High >= r.Stop;
                    if (hitTp && hitSl) { tpFirst = false; done = true; break; }
                    if (hitTp) { tpFirst = true; done = true; break; }
                    if (hitSl) { tpFirst = false; done = true; break; }
                }
                double lastClose = (double)future[^1].Close;
                if (!done) tpFirst = r.DirSign > 0 ? lastClose > r.Entry : lastClose < r.Entry;

                r.TpFirst = tpFirst;
                r.DirWin = r.DirSign > 0 ? lastClose > r.Entry : lastClose < r.Entry;
                r.Resolved = true;
                changed = true;
            }
            catch { /* provider hiccup: leave pending */ }
        }

        if (changed)
        {
            await _lock.WaitAsync(ct);
            try { Save(records); }
            finally { _lock.Release(); }
        }

        var buckets = new List<CalibrationBucketDto>();
        foreach (var grade in new[] { SignalGrade.APlus, SignalGrade.A, SignalGrade.B })
        {
            var resolved = records.Where(r => r.Grade == (int)grade && r.Resolved).ToList();
            if (resolved.Count == 0) continue;
            buckets.Add(new CalibrationBucketDto(
                grade, Loc.T($"Grade.{grade}"),
                Math.Round(resolved.Average(r => r.Predicted), 3),
                Math.Round(resolved.Average(r => r.DirWin ? 1.0 : 0.0), 3),
                resolved.Count,
                Math.Round(resolved.Average(r => r.TpFirst ? 1.0 : 0.0), 3)));
        }

        int total = records.Count;
        int resolvedCount = records.Count(r => r.Resolved);
        return new SignalRegistryStatsDto(total, resolvedCount, total - resolvedCount, buckets, Loc.T("Registry.Note"));
    }

    // ---- file store ----

    private List<Record> Load()
    {
        if (!File.Exists(_path)) return [];
        try
        {
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<List<Record>>(json) ?? [];
        }
        catch { return []; }
    }

    private void Save(List<Record> records)
    {
        try { File.WriteAllText(_path, JsonSerializer.Serialize(records)); }
        catch { /* logging store is best-effort */ }
    }
}
