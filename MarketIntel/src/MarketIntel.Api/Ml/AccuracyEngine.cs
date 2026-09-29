using System.Globalization;
using MarketIntel.Api.Indicators;
using MarketIntel.Api.Localization;
using MarketIntel.Api.MarketData;
using MarketIntel.Shared.Contracts;

namespace MarketIntel.Api.Ml;

/// <summary>
/// Computes how well predictions have actually done historically: a causal technical rule's
/// directional accuracy by horizon (aggregated across the universe), plus each symbol's
/// out-of-sample ML model accuracy. Cached daily. All figures are backward-looking and honest.
/// </summary>
public sealed class AccuracyEngine
{
    private readonly IMarketDataProvider _data;
    private readonly ModelService _models;

    private static readonly (int Days, string LabelKey)[] Horizons = [(1, "Acc.1d"), (3, "Acc.3d"), (5, "Acc.1w")];
    private const int BarCount = 320;

    // Cached per (language, day): horizon labels are localized.
    private readonly Dictionary<string, (AccuracyDashboardDto Dto, int Day)> _cache = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private static string Lang => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

    public AccuracyEngine(IMarketDataProvider data, ModelService models)
    {
        _data = data;
        _models = models;
    }

    public async Task<AccuracyDashboardDto> ComputeAsync(CancellationToken ct = default)
    {
        int day = (int)(DateTime.UtcNow.Date.ToBinary() % int.MaxValue);
        if (_cache.TryGetValue(Lang, out var c) && c.Day == day) return c.Dto;

        await _lock.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(Lang, out var c2) && c2.Day == day) return c2.Dto;

            var universe = await _data.GetUniverseAsync(ct);
            var horizonHits = Horizons.ToDictionary(h => h.Days, _ => (Hits: 0, Total: 0));
            var bySymbol = new List<SymbolAccuracyDto>();

            foreach (var symbol in universe)
            {
                ct.ThrowIfCancellationRequested();
                var candles = await _data.GetCandlesAsync(symbol, Timeframe.D1, BarCount, ct);
                var closes = candles.Select(x => (double)x.Close).ToArray();
                if (closes.Length < 40) continue;

                // Causal technical rule: bullish when price is above its EMA20.
                var ema = IndicatorMath.EmaSeries(closes, 20);
                int maxH = Horizons.Max(h => h.Days);
                for (int i = 20; i < closes.Length - maxH; i++)
                {
                    bool signalUp = closes[i] > ema[i];
                    foreach (var (days, _) in Horizons)
                    {
                        bool outcomeUp = closes[i + days] > closes[i];
                        var agg = horizonHits[days];
                        horizonHits[days] = (agg.Hits + (signalUp == outcomeUp ? 1 : 0), agg.Total + 1);
                    }
                }

                var model = await _models.GetOrTrainAsync(symbol, ct);
                if (model is not null)
                    bySymbol.Add(new SymbolAccuracyDto(symbol, Math.Round(model.TestAccuracy, 3), model.TestSize));
            }

            var byHorizon = Horizons
                .Select(h =>
                {
                    var (hits, total) = horizonHits[h.Days];
                    return new HorizonAccuracyDto(Loc.T(h.LabelKey), total > 0 ? Math.Round((double)hits / total, 3) : 0, total);
                })
                .ToList();

            int totalSample = bySymbol.Sum(s => s.Sample);
            double overall = totalSample > 0
                ? Math.Round(bySymbol.Sum(s => s.Accuracy * s.Sample) / totalSample, 3)
                : 0;

            var dto = new AccuracyDashboardDto(overall, totalSample, byHorizon,
                bySymbol.OrderByDescending(s => s.Accuracy).ToList());
            _cache[Lang] = (dto, day);
            return dto;
        }
        finally
        {
            _lock.Release();
        }
    }
}
