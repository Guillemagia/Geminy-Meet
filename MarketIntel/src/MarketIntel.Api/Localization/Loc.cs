using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace MarketIntel.Api.Localization;

/// <summary>
/// Lightweight, culture-aware string catalog for backend-generated text (analysis, notes,
/// assistant answers…). Strings live in embedded per-language JSON files (Localization/strings.&lt;lang&gt;.json)
/// keyed by a two-letter language code, so adding a language is just dropping in one JSON file.
///
/// The active language comes from <see cref="CultureInfo.CurrentUICulture"/>, which the request
/// localization middleware sets per request from the client's Accept-Language header.
/// </summary>
public static class Loc
{
    private const string DefaultLang = "en";

    // lang -> (key -> value). Loaded lazily from embedded resources.
    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> Catalogs = new();

    /// <summary>Two-letter language codes that ship with the app (an embedded strings.&lt;lang&gt;.json exists).</summary>
    public static IReadOnlyList<string> AvailableLanguages { get; } = DiscoverLanguages();

    /// <summary>Localized string for <paramref name="key"/> in the current UI culture (falls back to English, then the key).</summary>
    public static string T(string key)
    {
        var lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        if (Catalog(lang).TryGetValue(key, out var v)) return v;
        if (lang != DefaultLang && Catalog(DefaultLang).TryGetValue(key, out var en)) return en;
        return key;
    }

    /// <summary>Localized, formatted string. Numbers/dates use the current (formatting) culture.</summary>
    public static string T(string key, params object?[] args)
        => string.Format(CultureInfo.CurrentCulture, T(key), args);

    private static IReadOnlyDictionary<string, string> Catalog(string lang)
        => Catalogs.GetOrAdd(lang, Load);

    private static IReadOnlyDictionary<string, string> Load(string lang)
    {
        var asm = typeof(Loc).Assembly;
        var name = ResourceName(asm, lang);
        if (name is null) return new Dictionary<string, string>();
        using var stream = asm.GetManifestResourceStream(name)!;
        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
                   ?? new Dictionary<string, string>();
        return dict;
    }

    private static string? ResourceName(Assembly asm, string lang)
        => asm.GetManifestResourceNames()
              .FirstOrDefault(n => n.EndsWith($"strings.{lang}.json", StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<string> DiscoverLanguages()
    {
        var asm = typeof(Loc).Assembly;
        return asm.GetManifestResourceNames()
            .Select(n => n.Split('.'))
            .Where(p => p.Length >= 3 && p[^1].Equals("json", StringComparison.OrdinalIgnoreCase)
                        && p[^3].Equals("strings", StringComparison.OrdinalIgnoreCase))
            .Select(p => p[^2].ToLowerInvariant())
            .Distinct()
            .OrderBy(x => x)
            .ToList();
    }
}
