using System.Text.Json;
using MarketIntel.Maui.Models;

namespace MarketIntel.Maui.Services;

/// <summary>Persists the user's watchlist and alert rules locally on the device (Preferences).</summary>
public sealed class WatchlistStore
{
    private const string WatchlistKey = "watchlist.symbols";
    private const string RulesKey = "watchlist.rules";

    // A mix out of the box: equities, an ETF and a crypto (slash-free canonical form).
    private static readonly string[] DefaultWatchlist = ["NVDA", "AAPL", "SPY", "BTCUSD", "ETHUSD"];

    public List<string> GetSymbols()
        => Load<List<string>>(WatchlistKey) ?? [.. DefaultWatchlist];

    public void SaveSymbols(IEnumerable<string> symbols)
    {
        var clean = symbols
            .Select(s => s.Trim().ToUpperInvariant())
            // Allow up to 12 chars so crypto symbols (e.g. AVAXUSD, LINKUSD) are not dropped.
            .Where(s => s.Length is > 0 and <= 12)
            .Distinct()
            .ToList();
        Save(WatchlistKey, clean);
    }

    public List<AlertRule> GetRules()
        => Load<List<AlertRule>>(RulesKey) ?? [new AlertRule { Symbol = "", MinScore = 80 }];

    public void SaveRules(IEnumerable<AlertRule> rules) => Save(RulesKey, rules.ToList());

    private static T? Load<T>(string key)
    {
        var json = Preferences.Get(key, string.Empty);
        if (string.IsNullOrEmpty(json)) return default;
        try { return JsonSerializer.Deserialize<T>(json); }
        catch { return default; }
    }

    private static void Save<T>(string key, T value)
        => Preferences.Set(key, JsonSerializer.Serialize(value));
}
