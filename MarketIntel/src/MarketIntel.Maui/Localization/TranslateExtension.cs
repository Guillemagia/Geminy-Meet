using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Xaml;

namespace MarketIntel.Maui.Localization;

/// <summary>
/// XAML markup extension: <c>{loc:Translate Some_Key}</c> resolves a UI string for the current
/// language. It binds the target property to the <see cref="LocalizationResourceManager"/> indexer
/// (<c>[key]</c>), so every translated string on screen refreshes automatically when the language
/// is switched at runtime (the manager raises PropertyChanged for the indexer on change).
///
/// Optional <c>StringFormat</c> wraps the resolved value, e.g. <c>{loc:Translate Foo, StringFormat='{0} %'}</c>.
/// For values that need runtime arguments, use <see cref="LocalizationResourceManager.Format"/> in code instead.
/// </summary>
[ContentProperty(nameof(Key))]
public sealed class TranslateExtension : IMarkupExtension<BindingBase>
{
    /// <summary>The resource key to look up (the positional argument of the extension).</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Optional .NET composite format applied to the resolved string.</summary>
    public string? StringFormat { get; set; }

    public BindingBase ProvideValue(IServiceProvider serviceProvider) => new Binding
    {
        Mode = BindingMode.OneWay,
        Path = $"[{Key}]",
        Source = LocalizationResourceManager.Instance,
        StringFormat = StringFormat,
    };

    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider)
        => ProvideValue(serviceProvider);
}
