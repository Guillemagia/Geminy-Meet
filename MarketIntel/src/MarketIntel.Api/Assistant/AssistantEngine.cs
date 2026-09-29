using System.Globalization;
using System.Text;
using MarketIntel.Api.Localization;
using MarketIntel.Shared.Contracts;
using MarketIntel.Shared.Enums;

namespace MarketIntel.Api.Assistant;

/// <summary>
/// A grounded assistant: it answers questions ONLY from the system's own computed data for a
/// symbol (score, factors, scenarios, levels, risk, ML, options…). It composes answers from real
/// numbers — it does not invent facts. Intent is matched by keywords (no external LLM required).
/// All user-facing text and the intent keyword lists are localized, so it works in any language.
/// </summary>
public sealed class AssistantEngine
{
    public AssistantReplyDto Answer(SignalDto s, OptionsAnalysisDto? opt, string question)
    {
        string q = Normalize(question);
        string dir = s.Direction switch
        {
            SignalDirection.Bullish => Loc.T("Word.Bullish"),
            SignalDirection.Bearish => Loc.T("Word.Bearish"),
            _ => Loc.T("Word.Neutral")
        };

        string answer =
            Has(q, "Kw.Invalidate") ? Invalidation(s) :
            Has(q, "Kw.Levels") ? Levels(s) :
            Has(q, "Kw.Scenario") ? Scenarios(q, s) :
            Has(q, "Kw.Options") ? OptionsAnswer(opt) :
            Has(q, "Kw.Events") ? Events(s) :
            Has(q, "Kw.Risk") ? Risk(s) :
            Has(q, "Kw.Model") ? Ml(s) :
            Has(q, "Kw.Horizon") ? Horizons(s) :
            Has(q, "Kw.Confidence") ? Confidence(s) :
            Has(q, "Kw.Ensemble") ? Ensemble(s) :
            Has(q, "Kw.Why") ? Why(s, dir) :
            Summary(s, dir);

        return new AssistantReplyDto(answer, Suggestions());
    }

    private static IReadOnlyList<string> Suggestions() =>
    [
        Loc.T("Assist.Sug.Why"),
        Loc.T("Assist.Sug.Levels"),
        Loc.T("Assist.Sug.Invalidate"),
        Loc.T("Assist.Sug.BearCase"),
        Loc.T("Assist.Sug.AI"),
        Loc.T("Assist.Sug.Options")
    ];

    // ---- intent answers (all grounded in the signal data) ----

    private static string Why(SignalDto s, string dir)
    {
        if (s.IsNoTrade)
            return Loc.T("Assist.Why.NoTrade", s.Symbol, string.Join("; ", s.NoTradeReasons));

        var top = s.ScoreBreakdown
            .OrderByDescending(c => Math.Abs(c.Contribution))
            .Take(3)
            .Select(c => $"{c.Name} ({c.Contribution:+0.0;-0.0}): {c.Detail.ToLowerInvariant()}");

        return Loc.T("Assist.Why.Body", s.Symbol, dir, s.Score, string.Join("\n• ", top), Loc.T($"Conf.{s.Confidence}"));
    }

    private static string Levels(SignalDto s)
    {
        if (s.Levels.Count == 0) return Loc.T("Assist.Levels.None", s.Symbol);
        var res = s.Levels.Where(l => l.Kind == ZoneKind.Resistance).Take(2)
            .Select(l => $"{l.Low:0.00}–{l.High:0.00} ({l.DistancePercent:+0.0;-0.0}%)");
        var sup = s.Levels.Where(l => l.Kind == ZoneKind.Support).Take(2)
            .Select(l => $"{l.Low:0.00}–{l.High:0.00} ({l.DistancePercent:+0.0;-0.0}%)");
        var sb = new StringBuilder(Loc.T("Assist.Levels.Intro", s.Symbol, s.Price)).Append('\n');
        if (res.Any()) sb.Append(Loc.T("Assist.Levels.Resistances")).AppendLine(string.Join("  |  ", res));
        if (sup.Any()) sb.Append(Loc.T("Assist.Levels.Supports")).AppendLine(string.Join("  |  ", sup));
        return sb.ToString().TrimEnd();
    }

    private static string Scenarios(string q, SignalDto s)
    {
        if (s.Scenarios.Count == 0) return Loc.T("Assist.Scen.None", s.Symbol);
        string bear = Normalize(Loc.T("Kw.Bearish"));
        string bull = Normalize(Loc.T("Kw.Bullish"));
        string bas = Normalize(Loc.T("Kw.Base"));

        ScenarioDto? pick = null;
        if (q.Contains(bear)) pick = s.Scenarios.FirstOrDefault(x => Normalize(x.Name).Contains(bear));
        else if (q.Contains(bull)) pick = s.Scenarios.FirstOrDefault(x => Normalize(x.Name).Contains(bull));
        else if (q.Contains(bas)) pick = s.Scenarios.FirstOrDefault(x => Normalize(x.Name).Contains(bas));

        if (pick is not null)
            return Loc.T("Assist.Scen.Pick", pick.Name, pick.Probability, pick.Description);

        return Loc.T("Assist.Scen.Header") + "\n" +
               string.Join("\n", s.Scenarios.Select(x => $"• {x.Name} ({x.Probability:P0}): {x.Description}"));
    }

    private static string Invalidation(SignalDto s)
    {
        string dirWord = s.Direction == SignalDirection.Bearish ? Loc.T("Word.Bearish") : Loc.T("Word.Bullish");
        var sb = new StringBuilder(Loc.T("Assist.Invalid.Header", dirWord, s.Symbol)).Append('\n');
        if (s.Risk.SuggestedStop is decimal stop)
            sb.AppendLine(Loc.T("Assist.Invalid.Stop", stop));
        var keyLevel = s.Direction == SignalDirection.Bullish
            ? s.Levels.FirstOrDefault(l => l.Kind == ZoneKind.Support)
            : s.Levels.FirstOrDefault(l => l.Kind == ZoneKind.Resistance);
        if (keyLevel is not null)
            sb.AppendLine(Loc.T("Assist.Invalid.Zone", keyLevel.Low, keyLevel.High));
        sb.AppendLine(Loc.T("Assist.Invalid.Market"));
        if (s.EventRisk is { Events.Count: > 0 })
            sb.AppendLine(Loc.T("Assist.Invalid.Event", s.EventRisk.Message));
        return sb.ToString().TrimEnd();
    }

    private static string Risk(SignalDto s)
    {
        var r = s.Risk;
        if (r.SuggestedStop is null)
            return Loc.T("Assist.Risk.NoTrade", s.Symbol, r.Note);
        return Loc.T("Assist.Risk.Body", s.Symbol, Loc.T($"Risk.{r.Level}"),
            r.SuggestedStop, r.SuggestedTarget, r.RiskReward, r.Note);
    }

    private static string Ml(SignalDto s)
    {
        if (s.Ml is null) return Loc.T("Assist.Ml.None", s.Symbol);
        return Loc.T("Assist.Ml.Body", s.Ml.BullishProbability, s.Symbol, s.Ml.TestAccuracy, s.Ml.Sample);
    }

    private static string Ensemble(SignalDto s)
    {
        if (s.Ensemble is null) return Loc.T("Assist.Ens.None", s.Symbol);
        var models = string.Join(", ", s.Ensemble.Models.Select(m => $"{m.Name} {m.BullishProbability:P0}"));
        return Loc.T("Assist.Ens.Body", s.Ensemble.Score, s.Ensemble.Consensus, models);
    }

    private static string Horizons(SignalDto s)
    {
        if (s.Horizons.Count == 0) return Loc.T("Assist.Hor.None", s.Symbol);
        return Loc.T("Assist.Hor.Header", s.Symbol) + "\n" +
               string.Join("\n", s.Horizons.Select(h =>
                   Loc.T("Assist.Hor.Line", h.HorizonLabel, h.BullishProbability, h.BearishProbability,
                       h.NeutralProbability, Loc.T($"Conf.{h.Confidence}"))));
    }

    private static string Confidence(SignalDto s)
        => Loc.T("Assist.Conf.Body", s.Symbol, Loc.T($"Conf.{s.Confidence}"), s.DataQuality) +
           (s.Confidence == ConfidenceLevel.Low ? Loc.T("Assist.Conf.LowTail") : "");

    private static string Events(SignalDto s)
    {
        if (s.EventRisk is null || s.EventRisk.Events.Count == 0)
            return Loc.T("Assist.Events.None", s.Symbol);
        return s.EventRisk.Message + "\n" +
               string.Join("\n", s.EventRisk.Events.Select(e => Loc.T("Assist.Events.Line", e.Title, e.HoursUntil)));
    }

    private static string OptionsAnswer(OptionsAnalysisDto? opt)
    {
        if (opt is null || opt.Best.Count == 0) return Loc.T("Assist.Opt.None");
        var best = opt.Best[0];
        string type = best.Type == OptionType.Call ? "CALL" : "PUT";
        return Loc.T("Assist.Opt.Body", opt.ExpectedMoveLabel, type, best.Strike, best.Expiration,
                   best.Delta, best.Iv, best.QualityScore) +
               (opt.Unusual.Count > 0 ? Loc.T("Assist.Opt.Unusual", opt.Unusual.Count) : "");
    }

    private static string Summary(SignalDto s, string dir)
        => s.IsNoTrade
            ? Loc.T("Assist.Summary.NoTrade", s.Symbol, string.Join("; ", s.NoTradeReasons))
            : Loc.T("Assist.Summary.Body", s.Symbol, s.Price, dir, s.Score, Loc.T($"Conf.{s.Confidence}"));

    // ---- helpers ----

    /// <summary>True when the normalized question contains any of the (localized) terms for this intent key.</summary>
    private static bool Has(string q, string kwKey)
        => Loc.T(kwKey).Split(',').Any(t => t.Length > 0 && q.Contains(t));

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
