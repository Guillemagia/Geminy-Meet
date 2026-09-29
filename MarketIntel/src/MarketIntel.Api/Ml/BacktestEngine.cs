using MarketIntel.Shared.Contracts;

namespace MarketIntel.Api.Ml;

/// <summary>
/// Backtests a long-only strategy driven by a predictor: enter when the model's bullish
/// probability clears a threshold, hold for the horizon, and measure the trade outcomes.
/// Trades are non-overlapping so the equity curve (and drawdown) are well defined.
/// </summary>
public sealed class BacktestEngine
{
    private readonly FeatureExtractor _features;
    public BacktestEngine(FeatureExtractor features) => _features = features;

    private const double EntryThreshold = 0.55;

    public BacktestResultDto Run(
        string symbol, string strategy, IReadOnlyList<CandleDto> candles,
        IPredictor predictor, int horizon, int startIndex)
    {
        var close = candles.Select(c => (double)c.Close).ToArray();
        var high = candles.Select(c => (double)c.High).ToArray();
        var low = candles.Select(c => (double)c.Low).ToArray();
        var vol = candles.Select(c => (double)c.Volume).ToArray();

        var returns = new List<double>();
        for (int i = Math.Max(startIndex, FeatureExtractor.Warmup); i + horizon < candles.Count; i += horizon)
        {
            var f = _features.Extract(close, high, low, vol, i);
            if (predictor.PredictBullishProbability(f) < EntryThreshold) continue;
            returns.Add(close[i + horizon] / close[i] - 1.0);
        }

        if (returns.Count == 0)
            return new BacktestResultDto(strategy, symbol, 0, 0, 0, 0, 0, 0, 0, 0);

        int trades = returns.Count;
        var wins = returns.Where(r => r > 0).ToList();
        var losses = returns.Where(r => r <= 0).ToList();

        double winRate = (double)wins.Count / trades;
        double avgWin = wins.Count > 0 ? wins.Average() * 100 : 0;
        double avgLoss = losses.Count > 0 ? losses.Average() * 100 : 0;
        double grossWin = wins.Sum();
        double grossLoss = Math.Abs(losses.Sum());
        double profitFactor = grossLoss > 1e-9 ? grossWin / grossLoss : (grossWin > 0 ? 999 : 0);

        // Equity curve for total return and max drawdown.
        double equity = 1.0, peak = 1.0, maxDd = 0;
        foreach (var r in returns)
        {
            equity *= 1 + r;
            peak = Math.Max(peak, equity);
            maxDd = Math.Max(maxDd, (peak - equity) / peak);
        }
        double totalReturn = (equity - 1) * 100;

        // Annualised Sharpe from per-trade returns.
        double mean = returns.Average();
        double variance = returns.Sum(r => (r - mean) * (r - mean)) / trades;
        double std = Math.Sqrt(variance);
        double tradesPerYear = 252.0 / horizon;
        double sharpe = std > 1e-9 ? mean / std * Math.Sqrt(tradesPerYear) : 0;

        return new BacktestResultDto(
            strategy, symbol, trades,
            Math.Round(winRate, 3),
            Math.Round(avgWin, 2),
            Math.Round(avgLoss, 2),
            Math.Round(profitFactor, 2),
            Math.Round(maxDd * 100, 2),
            Math.Round(sharpe, 2),
            Math.Round(totalReturn, 2));
    }
}
