using System.Text.Json;
using MarketIntel.Maui.Models;

namespace MarketIntel.Maui.Services;

/// <summary>
/// Local paper-trading book. Equity = starting capital + realized P&amp;L + unrealized P&amp;L.
/// A fixed notional per position keeps the simulation simple. Persisted in Preferences.
/// </summary>
public sealed class PaperTradingStore
{
    public const decimal StartingCapital = 10_000m;
    public const decimal NotionalPerTrade = 1_000m;

    private const string OpenKey = "paper.open";
    private const string ClosedKey = "paper.closed";

    public List<PaperPosition> GetOpen() => Load<List<PaperPosition>>(OpenKey) ?? [];
    public List<ClosedTrade> GetClosed() => Load<List<ClosedTrade>>(ClosedKey) ?? [];

    public decimal RealizedPnl() => GetClosed().Sum(t => t.Pnl);

    /// <summary>Open a position at <paramref name="price"/>. One per symbol; a duplicate is ignored.</summary>
    public bool Open(string symbol, bool isLong, decimal price)
    {
        symbol = symbol.Trim().ToUpperInvariant();
        if (price <= 0) return false;
        var open = GetOpen();
        if (open.Any(p => p.Symbol == symbol)) return false;

        open.Add(new PaperPosition
        {
            Symbol = symbol,
            IsLong = isLong,
            EntryPrice = price,
            Shares = Math.Round(NotionalPerTrade / price, 4),
            OpenedUtc = DateTime.UtcNow
        });
        Save(OpenKey, open);
        return true;
    }

    /// <summary>Close a position at <paramref name="exitPrice"/>, booking it to history.</summary>
    public void Close(string symbol, decimal exitPrice)
    {
        symbol = symbol.Trim().ToUpperInvariant();
        var open = GetOpen();
        var pos = open.FirstOrDefault(p => p.Symbol == symbol);
        if (pos is null) return;

        open.Remove(pos);
        Save(OpenKey, open);

        var closed = GetClosed();
        closed.Insert(0, new ClosedTrade
        {
            Symbol = pos.Symbol,
            IsLong = pos.IsLong,
            EntryPrice = pos.EntryPrice,
            ExitPrice = exitPrice,
            Shares = pos.Shares,
            ClosedUtc = DateTime.UtcNow
        });
        Save(ClosedKey, closed);
    }

    private static T? Load<T>(string key)
    {
        var json = Preferences.Get(key, string.Empty);
        if (string.IsNullOrEmpty(json)) return default;
        try { return JsonSerializer.Deserialize<T>(json); }
        catch { return default; }
    }

    private static void Save<T>(string key, T value) => Preferences.Set(key, JsonSerializer.Serialize(value));
}
