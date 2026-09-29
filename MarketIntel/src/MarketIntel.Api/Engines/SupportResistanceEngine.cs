using MarketIntel.Shared.Contracts;
using MarketIntel.Shared.Enums;

namespace MarketIntel.Api.Engines;

/// <summary>
/// Detects support/resistance ZONES (not single lines) from swing pivots. A swing high/low
/// is a bar that stands above/below its neighbours; nearby pivots are clustered into a band,
/// and the band's strength reflects how many times price reacted there.
/// </summary>
public sealed class SupportResistanceEngine
{
    private const int Window = 3;      // bars required on each side to confirm a pivot
    private const int MaxPerSide = 3;  // how many supports / resistances to return

    public IReadOnlyList<PriceZoneDto> Compute(IReadOnlyList<CandleDto> candles, decimal price, double atr)
    {
        if (candles.Count < Window * 2 + 1 || price <= 0) return [];

        var highs = candles.Select(c => (double)c.High).ToArray();
        var lows = candles.Select(c => (double)c.Low).ToArray();

        // Collect confirmed swing pivots.
        var pivots = new List<double>();
        for (int i = Window; i < candles.Count - Window; i++)
        {
            if (IsSwingHigh(highs, i)) pivots.Add(highs[i]);
            if (IsSwingLow(lows, i)) pivots.Add(lows[i]);
        }
        // Always consider the extreme of the whole range as a pivot.
        pivots.Add(highs.Max());
        pivots.Add(lows.Min());
        if (pivots.Count == 0) return [];

        // Cluster pivots that sit within a tolerance band into zones.
        double tol = Math.Max(atr > 0 ? atr * 0.6 : (double)price * 0.006, (double)price * 0.004);
        pivots.Sort();

        var zones = new List<(double Low, double High, int Count)>();
        double zLow = pivots[0], zHigh = pivots[0];
        int count = 1;
        for (int i = 1; i < pivots.Count; i++)
        {
            if (pivots[i] - zHigh <= tol)
            {
                zHigh = pivots[i];
                count++;
            }
            else
            {
                zones.Add((zLow, zHigh, count));
                zLow = zHigh = pivots[i];
                count = 1;
            }
        }
        zones.Add((zLow, zHigh, count));

        double pxSeriesMax = highs.Max(), pxSeriesMin = lows.Min();
        double p = (double)price;

        var supports = new List<PriceZoneDto>();
        var resistances = new List<PriceZoneDto>();
        foreach (var z in zones)
        {
            double mid = (z.Low + z.High) / 2.0;
            double distPct = (mid - p) / p * 100.0;
            int strength = Math.Clamp(z.Count, 1, 5);

            if (z.High < p) // fully below -> support
            {
                string label = z.Low <= pxSeriesMin + tol ? "Mín. reciente" : "Soporte";
                supports.Add(new PriceZoneDto(ZoneKind.Support,
                    Round((decimal)z.Low), Round((decimal)z.High), strength, label, Math.Round(distPct, 2)));
            }
            else if (z.Low > p) // fully above -> resistance
            {
                string label = z.High >= pxSeriesMax - tol ? "Máx. reciente" : "Resistencia";
                resistances.Add(new PriceZoneDto(ZoneKind.Resistance,
                    Round((decimal)z.Low), Round((decimal)z.High), strength, label, Math.Round(distPct, 2)));
            }
            // Zones straddling the current price are effectively "at price" and skipped.
        }

        // Nearest first on each side, capped.
        var result = new List<PriceZoneDto>();
        result.AddRange(resistances.OrderBy(z => Math.Abs(z.DistancePercent)).Take(MaxPerSide));
        result.AddRange(supports.OrderBy(z => Math.Abs(z.DistancePercent)).Take(MaxPerSide));
        return result;
    }

    private static bool IsSwingHigh(double[] highs, int i)
    {
        for (int k = 1; k <= Window; k++)
            if (highs[i] < highs[i - k] || highs[i] < highs[i + k]) return false;
        return true;
    }

    private static bool IsSwingLow(double[] lows, int i)
    {
        for (int k = 1; k <= Window; k++)
            if (lows[i] > lows[i - k] || lows[i] > lows[i + k]) return false;
        return true;
    }

    private static decimal Round(decimal v) => Math.Round(v, 2);
}
