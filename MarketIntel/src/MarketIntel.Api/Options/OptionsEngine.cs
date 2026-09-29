using MarketIntel.Api.Localization;
using MarketIntel.Api.MarketData;
using MarketIntel.Shared.Contracts;
using MarketIntel.Shared.Enums;

namespace MarketIntel.Api.Options;

/// <summary>
/// Turns an options chain into an actionable view: the market's expected move (ATM IV × √T),
/// a liquidity/quality score per contract, the best contracts, and unusual-volume flags.
/// Unusual activity is flagged, never auto-interpreted as bullish or bearish.
/// </summary>
public sealed class OptionsEngine
{
    private readonly IMarketDataProvider _data;
    private readonly IOptionsProvider _options;

    public OptionsEngine(IMarketDataProvider data, IOptionsProvider options)
    {
        _data = data;
        _options = options;
    }

    public async Task<OptionsAnalysisDto?> AnalyzeAsync(string symbol, CancellationToken ct = default)
    {
        symbol = symbol.ToUpperInvariant();
        decimal spot = await _data.GetLastPriceAsync(symbol, ct);
        if (spot <= 0) return null;

        var chain = await _options.GetChainAsync(symbol, spot, ct);
        if (chain.Count == 0) return null;

        // Score every contract for liquidity/quality.
        var scored = chain.Select(c => c with { QualityScore = Quality(c) }).ToList();

        // Expected move from the nearest expiration's ATM implied volatility.
        int nearDays = scored.Min(c => c.DaysToExpiration);
        var nearAtm = scored
            .Where(c => c.DaysToExpiration == nearDays)
            .OrderBy(c => Math.Abs(c.Strike - spot))
            .Take(2)
            .ToList();
        double atmIv = nearAtm.Count > 0 ? nearAtm.Average(c => c.Iv) : 0.4;
        double emAbs = (double)spot * atmIv * Math.Sqrt(nearDays / 365.0);
        double emPct = (double)spot > 0 ? emAbs / (double)spot * 100 : 0;

        var best = scored
            .Where(c => c.DaysToExpiration <= 21)
            .OrderByDescending(c => c.QualityScore)
            .Take(4)
            .ToList();

        var unusual = scored
            .Where(c => c.VolumeOiRatio >= 2.0 && c.Volume >= 500)
            .OrderByDescending(c => c.VolumeOiRatio)
            .Take(4)
            .ToList();

        return new OptionsAnalysisDto(
            Symbol: symbol,
            Spot: spot,
            ExpectedMove: (decimal)Math.Round(emAbs, 2),
            ExpectedMovePercent: Math.Round(emPct, 2),
            ExpectedMoveLabel: Loc.T("Opt.ExpectedMoveLabel", emAbs, emPct, nearDays),
            Best: best,
            Unusual: unusual);
    }

    private static int Quality(OptionContractDto c)
    {
        double mid = (double)c.Mid;
        double spreadPct = mid > 0 ? (double)(c.Ask - c.Bid) / mid : 1;

        double liq = Math.Clamp(c.OpenInterest / 3000.0 + c.Volume / 2000.0, 0, 1);
        double spreadScore = Math.Clamp(1 - spreadPct / 0.15, 0, 1);
        double deltaScore = 1 - Math.Min(1, Math.Abs(Math.Abs(c.Delta) - 0.5) / 0.5); // best near 0.5 delta

        double q = 0.40 * liq + 0.35 * spreadScore + 0.25 * deltaScore;
        return (int)Math.Round(q * 100);
    }
}
