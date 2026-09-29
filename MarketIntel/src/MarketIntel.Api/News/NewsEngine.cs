using System.Globalization;
using System.Text;
using MarketIntel.Api.Localization;
using MarketIntel.Shared.Contracts;
using MarketIntel.Shared.Enums;

namespace MarketIntel.Api.News;

/// <summary>Lexicon-based sentiment scoring. A stand-in for a real NLP/LLM model behind the same idea.</summary>
public static class SentimentAnalyzer
{
    // Multilingual lexicon: terms from every shipped language so localized headlines still score.
    private static readonly string[] Positive =
    [
        // es
        "supera", "record", "fuerte", "crecimiento", "acuerdo", "impulsa", "expansion",
        "suben", "sube", "beneficios", "mejora", "aprueba", "estrategico", "gana",
        // en
        "beats", "strong", "growth", "deal", "boosts", "raise", "raises", "closes", "strategic", "record"
    ];
    private static readonly string[] Negative =
    [
        // es
        "incumple", "rebaja", "debilidad", "investigacion", "recorte", "perdidas",
        "cae", "multa", "despidos", "retraso", "riesgo",
        // en
        "misses", "downgrade", "downgrades", "weak", "weakness", "investigation",
        "layoffs", "losses", "cut", "delay"
    ];

    /// <summary>Sentiment in [-1, 1] from the balance of positive/negative terms.</summary>
    public static double Score(string text)
    {
        string t = Normalize(text);
        int pos = Positive.Count(w => t.Contains(w));
        int neg = Negative.Count(w => t.Contains(w));
        if (pos + neg == 0) return 0;
        return Math.Clamp((double)(pos - neg) / (pos + neg), -1, 1);
    }

    public static NewsSentiment Classify(double score) => score switch
    {
        >= 0.6 => NewsSentiment.MuyPositivo,
        >= 0.15 => NewsSentiment.Positivo,
        <= -0.6 => NewsSentiment.MuyNegativo,
        <= -0.15 => NewsSentiment.Negativo,
        _ => NewsSentiment.Neutral
    };

    private static string Normalize(string s)
    {
        s = s.ToLowerInvariant();
        var sb = new StringBuilder();
        foreach (var c in s.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString();
    }
}

/// <summary>
/// Analyses a symbol's news: scores each headline's sentiment, weights by importance, and
/// aggregates into an overall sentiment. Exposes a bullish probability for the ensemble.
/// </summary>
public sealed class NewsEngine
{
    private readonly INewsProvider _news;
    public NewsEngine(INewsProvider news) => _news = news;

    public async Task<NewsAnalysisDto?> AnalyzeAsync(string symbol, CancellationToken ct = default)
    {
        symbol = symbol.ToUpperInvariant();
        var headlines = await _news.GetHeadlinesAsync(symbol, ct);
        if (headlines.Count == 0) return null;

        var items = new List<NewsItemDto>();
        double weightedSum = 0, weightTotal = 0;
        foreach (var h in headlines)
        {
            double score = SentimentAnalyzer.Score(h.Text);
            double w = Weight(h.Importance);
            weightedSum += score * w;
            weightTotal += w;
            items.Add(new NewsItemDto(h.Text, h.Source, SentimentAnalyzer.Classify(score), h.Importance, TimeAgo(h.WhenUtc)));
        }

        double overall = weightTotal > 0 ? weightedSum / weightTotal : 0;
        var overallSentiment = SentimentAnalyzer.Classify(overall);
        string summary = overallSentiment switch
        {
            NewsSentiment.MuyPositivo => Loc.T("News.Summary.VeryPositive"),
            NewsSentiment.Positivo => Loc.T("News.Summary.Positive"),
            NewsSentiment.MuyNegativo => Loc.T("News.Summary.VeryNegative"),
            NewsSentiment.Negativo => Loc.T("News.Summary.Negative"),
            _ => Loc.T("News.Summary.Mixed")
        };

        return new NewsAnalysisDto(symbol, overallSentiment, Math.Round(overall, 2), summary, items);
    }

    /// <summary>Bullish probability implied by news sentiment, for the ensemble.</summary>
    public async Task<double?> BullishProbabilityAsync(string symbol, CancellationToken ct = default)
    {
        var analysis = await AnalyzeAsync(symbol, ct);
        return analysis is null ? null : Math.Clamp(0.5 + 0.5 * analysis.SentimentScore, 0.05, 0.95);
    }

    private static double Weight(EventImportance imp) => imp switch
    {
        EventImportance.Critical => 4,
        EventImportance.High => 3,
        EventImportance.Medium => 2,
        _ => 1
    };

    private static string TimeAgo(DateTime whenUtc)
    {
        var span = DateTime.UtcNow - whenUtc;
        return span.TotalHours < 1 ? Loc.T("TimeAgo.Min", Math.Max(1, (int)span.TotalMinutes))
             : span.TotalHours < 24 ? Loc.T("TimeAgo.Hours", (int)span.TotalHours)
             : Loc.T("TimeAgo.Days", (int)span.TotalDays);
    }
}
