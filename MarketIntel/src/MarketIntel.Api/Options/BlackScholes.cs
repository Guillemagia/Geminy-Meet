using MarketIntel.Shared.Enums;

namespace MarketIntel.Api.Options;

/// <summary>Black-Scholes pricing and Greeks for European options. Time in years, rates annualised.</summary>
public static class BlackScholes
{
    /// <summary>Theoretical option price.</summary>
    public static double Price(OptionType type, double s, double k, double t, double sigma, double r = 0.04)
    {
        if (t <= 0 || sigma <= 0) return Math.Max(0, type == OptionType.Call ? s - k : k - s);
        double d1 = (Math.Log(s / k) + (r + 0.5 * sigma * sigma) * t) / (sigma * Math.Sqrt(t));
        double d2 = d1 - sigma * Math.Sqrt(t);
        return type == OptionType.Call
            ? s * Cdf(d1) - k * Math.Exp(-r * t) * Cdf(d2)
            : k * Math.Exp(-r * t) * Cdf(-d2) - s * Cdf(-d1);
    }

    /// <summary>(delta, gamma, theta-per-day, vega-per-1%-vol).</summary>
    public static (double Delta, double Gamma, double Theta, double Vega) Greeks(
        OptionType type, double s, double k, double t, double sigma, double r = 0.04)
    {
        if (t <= 0 || sigma <= 0) return (type == OptionType.Call ? 1 : -1, 0, 0, 0);
        double sqrtT = Math.Sqrt(t);
        double d1 = (Math.Log(s / k) + (r + 0.5 * sigma * sigma) * t) / (sigma * sqrtT);
        double d2 = d1 - sigma * sqrtT;
        double pdf = Pdf(d1);

        double delta = type == OptionType.Call ? Cdf(d1) : Cdf(d1) - 1;
        double gamma = pdf / (s * sigma * sqrtT);
        double vega = s * pdf * sqrtT / 100.0; // per 1% change in vol
        double thetaYear = type == OptionType.Call
            ? -(s * pdf * sigma) / (2 * sqrtT) - r * k * Math.Exp(-r * t) * Cdf(d2)
            : -(s * pdf * sigma) / (2 * sqrtT) + r * k * Math.Exp(-r * t) * Cdf(-d2);
        return (delta, gamma, thetaYear / 365.0, vega);
    }

    private static double Cdf(double x) => 0.5 * (1 + Erf(x / Math.Sqrt(2)));
    private static double Pdf(double x) => Math.Exp(-0.5 * x * x) / Math.Sqrt(2 * Math.PI);

    /// <summary>Abramowitz-Stegun 5-term error-function approximation.</summary>
    private static double Erf(double x)
    {
        double sign = Math.Sign(x);
        x = Math.Abs(x);
        double t = 1.0 / (1.0 + 0.3275911 * x);
        double poly = t * (0.254829592 + t * (-0.284496736 + t * (1.421413741 + t * (-1.453152027 + t * 1.061405429))));
        return sign * (1.0 - poly * Math.Exp(-x * x));
    }
}
