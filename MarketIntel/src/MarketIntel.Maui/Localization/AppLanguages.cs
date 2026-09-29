namespace MarketIntel.Maui.Localization;

/// <summary>A selectable language: its two-letter code and its native display name.</summary>
public sealed record LanguageOption(string Code, string NativeName)
{
    public override string ToString() => NativeName;
}

/// <summary>
/// Maps language codes to native names and exposes the languages the app actually ships
/// (those with an embedded strings.&lt;lang&gt;.json). Add a JSON file and, if needed, a name here;
/// the picker updates automatically.
/// </summary>
public static class AppLanguages
{
    // Native names for every language we plan to ship. Only those with a JSON catalog are offered.
    private static readonly IReadOnlyDictionary<string, string> NativeNames = new Dictionary<string, string>
    {
        ["en"] = "English",
        ["es"] = "Español",
        ["pt"] = "Português",
        ["fr"] = "Français",
        ["de"] = "Deutsch",
        ["it"] = "Italiano",
        ["nl"] = "Nederlands",
        ["sv"] = "Svenska",
        ["no"] = "Norsk",
        ["da"] = "Dansk",
        ["fi"] = "Suomi",
        ["pl"] = "Polski",
        ["ru"] = "Русский",
        ["tr"] = "Türkçe",
        ["el"] = "Ελληνικά",
        ["zh"] = "中文（简体）",
        ["zt"] = "中文（繁體）",
        ["ja"] = "日本語",
        ["ko"] = "한국어",
        ["hi"] = "हिन्दी",
        ["ta"] = "தமிழ்",
        ["bn"] = "বাংলা",
        ["id"] = "Bahasa Indonesia",
        ["ms"] = "Bahasa Melayu",
        ["th"] = "ไทย",
        ["vi"] = "Tiếng Việt",
        ["fil"] = "Filipino",
        ["ar"] = "العربية",
        ["he"] = "עברית",
        ["fa"] = "فارسی",
        ["ur"] = "اردو",
        ["af"] = "Afrikaans",
        ["sw"] = "Kiswahili",
    };

    /// <summary>Languages currently available (have an embedded catalog), with native names.</summary>
    public static IReadOnlyList<LanguageOption> Available { get; } =
        LocalizationResourceManager.Instance.AvailableLanguages
            .Select(code => new LanguageOption(code, NativeName(code)))
            .OrderBy(o => o.NativeName, StringComparer.CurrentCulture)
            .ToList();

    public static string NativeName(string code)
        => NativeNames.TryGetValue(code, out var n) ? n : code.ToUpperInvariant();
}
