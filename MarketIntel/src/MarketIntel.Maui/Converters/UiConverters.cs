using System.Globalization;
using MarketIntel.Maui.Localization;
using MarketIntel.Shared.Enums;

namespace MarketIntel.Maui.Converters;

file static class L
{
    public static string Get(string key) => LocalizationResourceManager.Instance.Get(key);
    public static string Fmt(string key, params object?[] a) => LocalizationResourceManager.Instance.Format(key, a);
}

file static class Palette
{
    public static readonly Color Bull = Color.FromArgb("#16A34A");
    public static readonly Color Bear = Color.FromArgb("#DC2626");
    public static readonly Color Neutral = Color.FromArgb("#6B7280");
    public static readonly Color Amber = Color.FromArgb("#D97706");
}

/// <summary>SignalDirection -> green/red/gray.</summary>
public sealed class DirectionToColorConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) => value switch
    {
        SignalDirection.Bullish => Palette.Bull,
        SignalDirection.Bearish => Palette.Bear,
        _ => Palette.Neutral
    };
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>SignalDirection -> localized label.</summary>
public sealed class DirectionToTextConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) => value switch
    {
        SignalDirection.Bullish => L.Get("Enum_Dir_Bullish"),
        SignalDirection.Bearish => L.Get("Enum_Dir_Bearish"),
        _ => L.Get("Enum_Dir_Neutral")
    };
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Formats a bound value into a localized template named by ConverterParameter, e.g. "over {0} predictions".</summary>
public sealed class LocFormatConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => L.Fmt(p as string ?? "", value);
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>RiskLevel -> localized label.</summary>
public sealed class RiskToTextConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) => value switch
    {
        RiskLevel.Low => L.Get("Enum_Risk_Low"),
        RiskLevel.High => L.Get("Enum_Risk_High"),
        _ => L.Get("Enum_Risk_Medium")
    };
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>ConfidenceLevel -> localized label.</summary>
public sealed class ConfidenceToTextConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) => value switch
    {
        ConfidenceLevel.High => L.Get("Enum_Conf_High"),
        ConfidenceLevel.Medium => L.Get("Enum_Conf_Medium"),
        _ => L.Get("Enum_Conf_Low")
    };
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>FactorBias -> green/red/gray.</summary>
public sealed class BiasToColorConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) => value switch
    {
        FactorBias.Bullish => Palette.Bull,
        FactorBias.Bearish => Palette.Bear,
        _ => Palette.Neutral
    };
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>0-100 score -> color band.</summary>
public sealed class ScoreToColorConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        int s = value is int i ? i : 50;
        return s >= 58 ? Palette.Bull : s <= 42 ? Palette.Bear : Palette.Neutral;
    }
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>RiskLevel -> color (Low green, Medium amber, High red).</summary>
public sealed class RiskToColorConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) => value switch
    {
        RiskLevel.Low => Palette.Bull,
        RiskLevel.High => Palette.Bear,
        _ => Palette.Amber
    };
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Probability 0..1 -> "68%".</summary>
public sealed class ProbabilityToPercentConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => value is double d ? $"{Math.Round(d * 100)}%" : "-";
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Collection count -> bool (true when non-empty). For section visibility.
/// Pass ConverterParameter="invert" to negate (true when null/empty).</summary>
public sealed class CountToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        bool nonEmpty = value is int i && i > 0;
        return (p as string) == "invert" ? !nonEmpty : nonEmpty;
    }
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Win rate maps to a color: green when reliable (55% or more), red when weak (35% or less), amber between.</summary>
public sealed class WinRateToColorConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        double r = value is double d ? d : 0;
        return r >= 0.55 ? Palette.Bull : r <= 0.35 ? Palette.Bear : Palette.Amber;
    }
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>SwingLabel -> green for bullish structure (HH/HL), red for bearish (LH/LL).</summary>
public sealed class SwingLabelToColorConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) => value switch
    {
        SwingLabel.HigherHigh or SwingLabel.HigherLow => Palette.Bull,
        _ => Palette.Bear
    };
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>SwingLabel -> abbreviation HH / HL / LH / LL.</summary>
public sealed class SwingLabelToTextConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) => value switch
    {
        SwingLabel.HigherHigh => "HH",
        SwingLabel.HigherLow => "HL",
        SwingLabel.LowerHigh => "LH",
        _ => "LL"
    };
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Trend text -> color (contains "alcista" green, "bajista" red, else amber).</summary>
public sealed class TrendToColorConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        string s = value as string ?? "";
        if (s.Contains("alcista", StringComparison.OrdinalIgnoreCase)) return Palette.Bull;
        if (s.Contains("bajista", StringComparison.OrdinalIgnoreCase)) return Palette.Bear;
        return Palette.Amber;
    }
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>ZoneKind -> red (resistance, above) or green (support, below).</summary>
public sealed class ZoneKindToColorConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => value is ZoneKind.Resistance ? Palette.Bear : Palette.Bull;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Strength 1-5 -> filled/empty dots, e.g. "●●●○○".</summary>
public sealed class StrengthToDotsConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        int s = Math.Clamp(value is int i ? i : 0, 0, 5);
        return new string('●', s) + new string('○', 5 - s);
    }
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>EventImportance -> color (Critical red, High amber, else gray).</summary>
public sealed class ImportanceToColorConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) => value switch
    {
        EventImportance.Critical => Palette.Bear,
        EventImportance.High => Palette.Amber,
        _ => Palette.Neutral
    };
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>HoursUntil (double) -> "en 35 min" / "en 41 h" / "en 3 días".</summary>
public sealed class HoursToWhenConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        double h = value is double d ? d : 0;
        if (h < 1) return L.Fmt("When_InMin", Math.Max(1, Math.Round(h * 60)));
        if (h < 48) return L.Fmt("When_InHours", Math.Round(h));
        return L.Fmt("When_InDays", Math.Round(h / 24));
    }
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>NewsSentiment -> green (positive), red (negative), gray (neutral).</summary>
public sealed class SentimentToColorConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) => value switch
    {
        NewsSentiment.MuyPositivo or NewsSentiment.Positivo => Palette.Bull,
        NewsSentiment.MuyNegativo or NewsSentiment.Negativo => Palette.Bear,
        _ => Palette.Neutral
    };
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>NewsSentiment -> Spanish label.</summary>
public sealed class SentimentToTextConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) => value switch
    {
        NewsSentiment.MuyPositivo => L.Get("Enum_Sent_VeryPositive"),
        NewsSentiment.Positivo => L.Get("Enum_Sent_Positive"),
        NewsSentiment.MuyNegativo => L.Get("Enum_Sent_VeryNegative"),
        NewsSentiment.Negativo => L.Get("Enum_Sent_Negative"),
        _ => L.Get("Enum_Sent_Neutral")
    };
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>OptionType -> green (call) or red (put).</summary>
public sealed class OptionTypeToColorConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => value is OptionType.Call ? Palette.Bull : Palette.Bear;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>OptionType -> "CALL" / "PUT".</summary>
public sealed class OptionTypeToTextConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => value is OptionType.Call ? "CALL" : "PUT";
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Signed number (decimal/double/int) -> green positive, red negative, gray zero.</summary>
public sealed class SignToColorConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        double d = value switch
        {
            decimal m => (double)m,
            double x => x,
            int i => i,
            _ => 0
        };
        return d > 0 ? Palette.Bull : d < 0 ? Palette.Bear : Palette.Neutral;
    }
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Signal grade -> color: A+/A green (trade), B amber (wait), C/D red (no trade).</summary>
public sealed class GradeToColorConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) => value switch
    {
        SignalGrade.APlus or SignalGrade.A => Palette.Bull,
        SignalGrade.B => Palette.Amber,
        _ => Palette.Bear
    };
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Favorable flag -> green (favorable factor) or muted gray (weak/against).</summary>
public sealed class FavorableToColorConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => value is true ? Palette.Bull : Palette.Neutral;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Boolean negation, for inverse IsVisible bindings.</summary>
public sealed class InvertBoolConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) => value is not true;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => v is not true;
}

/// <summary>Long/short flag -> green (long) or red (short).</summary>
public sealed class LongToColorConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => value is true ? Palette.Bull : Palette.Bear;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Chat bubble: user vs assistant background color.</summary>
public sealed class BubbleColorConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => value is true ? Color.FromArgb("#2563EB") : Color.FromArgb("#1E293B");
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Chat bubble: user aligns end, assistant aligns start.</summary>
public sealed class BubbleAlignConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => value is true ? LayoutOptions.End : LayoutOptions.Start;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Synthetic flag -> amber (demo data) or green (live data).</summary>
public sealed class SyntheticToColorConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => value is true ? Palette.Amber : Palette.Bull;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Signed contribution -> green when positive, red when negative.</summary>
public sealed class ContributionToColorConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        double d = value is double v ? v : 0;
        return d > 0 ? Palette.Bull : d < 0 ? Palette.Bear : Palette.Neutral;
    }
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}
