using MarketIntel.Shared.Contracts;
using MarketIntel.Shared.Enums;

namespace MarketIntel.Api.Options;

/// <summary>
/// Deterministic synthetic options chain. Prices/Greeks come from Black-Scholes with a volatility
/// smile; open interest concentrates near the money; volume occasionally spikes (unusual activity).
/// Not tradeable data — replace with a real chain provider later.
/// </summary>
public sealed class MockOptionsProvider : IOptionsProvider
{
    public bool IsSynthetic => true;

    private static readonly (int Days, string Label)[] Expirations =
        [(7, "7 días"), (21, "3 semanas"), (35, "1 mes")];

    public Task<IReadOnlyList<OptionContractDto>> GetChainAsync(string symbol, decimal spot, CancellationToken ct = default)
    {
        symbol = symbol.ToUpperInvariant();
        double s = (double)spot;
        if (s <= 0) return Task.FromResult<IReadOnlyList<OptionContractDto>>([]);

        int dayBucket = (int)(DateTime.UtcNow.Date.ToBinary() % int.MaxValue);
        var rng = new Random(StableHash($"{symbol}|opt|{dayBucket}"));

        double baseIv = 0.30 + (StableHash(symbol + "iv") % 1000 / 1000.0) * 0.45; // 0.30..0.75
        double step = NiceStep(s);
        double atm = Math.Round(s / step) * step;
        long baseOi = 800 + StableHash(symbol + "oi") % 4000;

        var list = new List<OptionContractDto>();
        foreach (var (days, label) in Expirations)
        {
            double t = days / 365.0;
            double expiryWeight = days <= 7 ? 1.0 : days <= 21 ? 0.7 : 0.5;

            for (int stepIdx = -5; stepIdx <= 5; stepIdx++)
            {
                double k = atm + stepIdx * step;
                if (k <= 0) continue;
                double moneyness = (k - s) / s;
                double iv = Math.Clamp(baseIv * (1 + 0.6 * Math.Abs(moneyness)), 0.1, 2.0);

                foreach (var type in new[] { OptionType.Call, OptionType.Put })
                {
                    double price = BlackScholes.Price(type, s, k, t, iv);
                    if (price < 0.02) continue;
                    var (delta, gamma, theta, vega) = BlackScholes.Greeks(type, s, k, t, iv);

                    double spreadPct = 0.02 + rng.NextDouble() * 0.06;
                    decimal mid = (decimal)Math.Round(price, 2);
                    decimal half = (decimal)Math.Round(Math.Max(0.01, price * spreadPct / 2), 2);
                    decimal bid = Math.Max(0.01m, mid - half);
                    decimal ask = mid + half;

                    // Open interest concentrates near the money.
                    double atmCloseness = Math.Exp(-Math.Pow(stepIdx / 2.5, 2));
                    long oi = (long)Math.Round(baseOi * expiryWeight * atmCloseness * (0.5 + rng.NextDouble()));
                    long volume = (long)Math.Round(oi * (0.2 + rng.NextDouble() * 0.5));
                    if (rng.NextDouble() < 0.06) volume = (long)Math.Round(oi * (2.0 + rng.NextDouble() * 4.0)); // unusual
                    double volOi = oi > 0 ? (double)volume / oi : 0;

                    list.Add(new OptionContractDto(
                        Type: type,
                        Strike: (decimal)Math.Round(k, 2),
                        Expiration: label,
                        DaysToExpiration: days,
                        Bid: bid,
                        Ask: ask,
                        Mid: mid,
                        Volume: volume,
                        OpenInterest: oi,
                        Iv: Math.Round(iv, 3),
                        Delta: Math.Round(delta, 3),
                        Gamma: Math.Round(gamma, 4),
                        Theta: Math.Round(theta, 3),
                        Vega: Math.Round(vega, 3),
                        VolumeOiRatio: Math.Round(volOi, 2),
                        QualityScore: 0)); // filled by the engine
                }
            }
        }

        return Task.FromResult<IReadOnlyList<OptionContractDto>>(list);
    }

    private static double NiceStep(double s) => s switch
    {
        < 25 => 1,
        < 100 => 2.5,
        < 250 => 5,
        < 600 => 10,
        _ => 25
    };

    private static int StableHash(string str)
    {
        unchecked
        {
            const uint fnvPrime = 16777619;
            uint hash = 2166136261;
            foreach (char c in str) { hash ^= c; hash *= fnvPrime; }
            return (int)(hash & 0x7FFFFFFF);
        }
    }
}
