using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace MarketIntel.Maui.Localization;

/// <summary>
/// Runtime string catalog for the app's own UI text. Strings live in embedded per-language JSON
/// files (Localization/strings.&lt;lang&gt;.json). The current language can change at runtime: XAML binds
/// to the indexer (via <c>loc:Translate</c>) and every binding refreshes when the language changes.
/// </summary>
public sealed class LocalizationResourceManager : INotifyPropertyChanged
{
    public const string DefaultLanguage = "en";
    private const string PrefKey = "app_language";

    public static LocalizationResourceManager Instance { get; } = new();

    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _catalogs = new();
    private IReadOnlyDictionary<string, string> _current = new Dictionary<string, string>();
    private IReadOnlyDictionary<string, string> _fallback = new Dictionary<string, string>();

    private LocalizationResourceManager()
    {
        AvailableLanguages = DiscoverLanguages();
        _fallback = Catalog(DefaultLanguage);
        Apply(ResolveInitialLanguage(), persist: false);
    }

    /// <summary>Two-letter codes that ship with the app (an embedded strings.&lt;lang&gt;.json exists).</summary>
    public IReadOnlyList<string> AvailableLanguages { get; }

    public string CurrentLanguage { get; private set; } = DefaultLanguage;

    /// <summary>Indexer used by XAML bindings. Falls back to English, then the raw key.</summary>
    public string this[string key]
        => _current.TryGetValue(key, out var v) ? v
         : _fallback.TryGetValue(key, out var f) ? f
         : key;

    public string Get(string key) => this[key];

    public string Format(string key, params object?[] args)
        => string.Format(CultureInfo.CurrentCulture, this[key], args);

    /// <summary>Switch language, persist the choice and refresh every bound string.</summary>
    public void SetLanguage(string lang)
    {
        if (string.IsNullOrWhiteSpace(lang) || lang == CurrentLanguage) return;
        Apply(lang, persist: true);
        // Null/empty member name tells XAML bindings (including the [key] indexer) to re-read.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // ---- internals ----

    private void Apply(string lang, bool persist)
    {
        lang = AvailableLanguages.Contains(lang) ? lang : DefaultLanguage;
        _current = Catalog(lang);
        CurrentLanguage = lang;

        var ci = CultureInfo.GetCultureInfo(lang);
        CultureInfo.CurrentCulture = ci;
        CultureInfo.CurrentUICulture = ci;
        CultureInfo.DefaultThreadCurrentCulture = ci;
        CultureInfo.DefaultThreadCurrentUICulture = ci;

        if (persist)
            try { Preferences.Set(PrefKey, lang); } catch { /* preferences unavailable in some hosts */ }
    }

    private static string ResolveInitialLanguage()
    {
        try
        {
            var saved = Preferences.Get(PrefKey, "");
            if (!string.IsNullOrWhiteSpace(saved)) return saved;
        }
        catch { /* ignore */ }
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
    }

    private IReadOnlyDictionary<string, string> Catalog(string lang)
    {
        if (_catalogs.TryGetValue(lang, out var c)) return c;
        var loaded = Load(lang);
        _catalogs[lang] = loaded;
        return loaded;
    }

    private static IReadOnlyDictionary<string, string> Load(string lang)
    {
        var asm = typeof(LocalizationResourceManager).Assembly;
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith($"strings.{lang}.json", StringComparison.OrdinalIgnoreCase));
        if (name is null) return new Dictionary<string, string>();
        using var stream = asm.GetManifestResourceStream(name)!;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new Dictionary<string, string>();
    }

    private static IReadOnlyList<string> DiscoverLanguages()
        => typeof(LocalizationResourceManager).Assembly.GetManifestResourceNames()
            .Select(n => n.Split('.'))
            .Where(p => p.Length >= 3 && p[^1].Equals("json", StringComparison.OrdinalIgnoreCase)
                        && p[^3].Equals("strings", StringComparison.OrdinalIgnoreCase))
            .Select(p => p[^2].ToLowerInvariant())
            .Distinct().OrderBy(x => x).ToList();
}
