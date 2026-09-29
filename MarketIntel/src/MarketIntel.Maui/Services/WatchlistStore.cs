using System.Text.Json;
using MarketIntel.Maui.Models;

namespace MarketIntel.Maui.Services;

/// <summary>Persists the user's watchlist and alert rules locally on the device (Preferences).</summary>
public sealed class WatchlistStore
{
    private const string WatchlistKey = "watchlist.symbols";
    private const string RulesKey = "watchlist.rules";

    private static readonly string[] DefaultWatchlist = ["NVDA", "TSLA", "AAPL"];

    public List<string> GetSymbols()
        => Load<List<string>>(WatchlistKey) ?? [.. DefaultWatchlist];

    public void SaveSymbols(IEnumerable<string> symbols)
    {
        var clean = symbols
            .Select(s => s.Trim().ToUpperInvariant())
            .Where(s => s.Length is > 0 and <= 6)
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
