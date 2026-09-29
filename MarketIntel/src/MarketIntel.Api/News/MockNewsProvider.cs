using MarketIntel.Api.Localization;
using MarketIntel.Shared.Enums;

namespace MarketIntel.Api.News;

/// <summary>A raw headline before sentiment analysis.</summary>
public sealed record RawHeadline(string Text, string Source, EventImportance Importance, DateTime WhenUtc);

/// <summary>Source of raw headlines. Swap the mock for a real news/NLP feed later.</summary>
public interface INewsProvider
{
    bool IsSynthetic { get; }
    Task<IReadOnlyList<RawHeadline>> GetHeadlinesAsync(string symbol, CancellationToken ct = default);
}

/// <summary>
/// Deterministic synthetic news. Generates plausible financial headlines per symbol; the
/// sentiment is NOT tagged here — it is derived later by <see cref="SentimentAnalyzer"/>, so the
/// whole NLP pipeline is exercised. Not real news.
/// </summary>
public sealed class MockNewsProvider : INewsProvider
{
    public bool IsSynthetic => true;

    private static readonly string[] Banks = ["Morgan Stanley", "Goldman Sachs", "JP Morgan", "Barclays", "UBS"];

    // Resource keys resolved to the request language in GetHeadlinesAsync.
    private static readonly (string TemplateKey, string SourceKey, EventImportance Imp)[] Templates =
    [
        ("NewsTpl.1", "NewsSrc.Earnings", EventImportance.High),
        ("NewsTpl.2", "NewsSrc.Analysts", EventImportance.Medium),
        ("NewsTpl.3", "NewsSrc.Press", EventImportance.Medium),
        ("NewsTpl.4", "NewsSrc.Press", EventImportance.Medium),
        ("NewsTpl.5", "NewsSrc.Earnings", EventImportance.High),
        ("NewsTpl.6", "NewsSrc.Analysts", EventImportance.Medium),
        ("NewsTpl.7", "NewsSrc.Regulatory", EventImportance.Critical),
        ("NewsTpl.8", "NewsSrc.Press", EventImportance.High),
        ("NewsTpl.9", "NewsSrc.Agenda", EventImportance.Low),
        ("NewsTpl.10", "NewsSrc.Agenda", EventImportance.Low),
    ];

    public Task<IReadOnlyList<RawHeadline>> GetHeadlinesAsync(string symbol, CancellationToken ct = default)
    {
        symbol = symbol.ToUpperInvariant();
        int dayBucket = (int)(DateTime.UtcNow.Date.ToBinary() % int.MaxValue);
        var rng = new Random(StableHash($"{symbol}|news|{dayBucket}"));

        int count = 4 + rng.Next(3); // 4..6 headlines
        var chosen = Templates.OrderBy(_ => rng.Next()).Take(count);

        var list = new List<RawHeadline>();
        foreach (var (templateKey, sourceKey, imp) in chosen)
        {
            string text = Loc.T(templateKey, symbol, Banks[rng.Next(Banks.Length)]);
            var when = DateTime.UtcNow - TimeSpan.FromHours(rng.Next(1, 48)) - TimeSpan.FromMinutes(rng.Next(60));
            list.Add(new RawHeadline(text, Loc.T(sourceKey), imp, when));
        }

        return Task.FromResult<IReadOnlyList<RawHeadline>>(list.OrderByDescending(h => h.WhenUtc).ToList());
    }

    private static int StableHash(string s)
    {
        unchecked
        {
            const uint fnvPrime = 16777619;
            uint hash = 2166136261;
            foreach (char c in s) { hash ^= c; hash *= fnvPrime; }
            return (int)(hash & 0x7FFFFFFF);
        }
    }
}
