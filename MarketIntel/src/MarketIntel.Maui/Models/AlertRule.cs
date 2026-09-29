using MarketIntel.Maui.Localization;

namespace MarketIntel.Maui.Models;

/// <summary>A user alert rule: fires when a symbol's score reaches a threshold.
/// An empty <see cref="Symbol"/> means "any symbol".</summary>
public sealed class AlertRule
{
    public string Symbol { get; set; } = "";
    public int MinScore { get; set; } = 85;

    public string Describe()
        => LocalizationResourceManager.Instance.Format("Alert_Rule",
            string.IsNullOrWhiteSpace(Symbol) ? LocalizationResourceManager.Instance.Get("Alert_Any") : Symbol,
            MinScore);

    /// <summary>Display text for binding (records/plain classes can't call methods in XAML).</summary>
    public string Text => Describe();
}
