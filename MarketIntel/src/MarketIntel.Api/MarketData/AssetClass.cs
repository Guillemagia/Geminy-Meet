namespace MarketIntel.Api.MarketData;

/// <summary>The kind of instrument a symbol represents. Drives data routing and event logic.</summary>
public enum AssetKind
{
    Equity = 0,
    Etf = 1,
    Crypto = 2
}

/// <summary>
/// Classifies a symbol as an equity, an ETF or a cryptocurrency, and normalizes crypto symbols.
///
/// Crypto is kept in a **slash-free canonical form** everywhere in the platform (e.g. "BTCUSD"),
/// so it travels safely through the URL routes ("/api/signals/{symbol}") and JSON. Only the Alpaca
/// crypto provider converts it back to the exchange pair form ("BTC/USD") at the edge.
/// </summary>
public static class AssetClass
{
    // Crypto base tickers available on Alpaca's US crypto data feed (vs USD).
    private static readonly HashSet<string> CryptoBases = new(StringComparer.OrdinalIgnoreCase)
    {
        "BTC", "ETH", "SOL", "DOGE", "AVAX", "LINK", "LTC", "BCH",
        "XRP", "ADA", "DOT", "UNI", "AAVE", "SHIB", "SUSHI", "XTZ", "MKR", "GRT"
    };

    // Well-known ETFs. Fetched exactly like equities (no special routing); listed so the platform
    // can label them and skip company-only logic such as earnings.
    private static readonly HashSet<string> EtfSymbols = new(StringComparer.OrdinalIgnoreCase)
    {
        "SPY", "QQQ", "IWM", "DIA", "VIXY", "VIX", "GLD", "SLV", "TLT", "HYG",
        "XLK", "XLF", "XLE", "XLV", "XLY", "SMH", "SOXX", "ARKK", "EEM", "VTI", "VOO", "VEA"
    };

    /// <summary>Default scanner universe: a spread of equities, ETFs and crypto.</summary>
    public static readonly IReadOnlyList<string> DefaultUniverse =
    [
        // Equities
        "NVDA", "TSLA", "AAPL", "AMD", "META", "MSFT", "GOOGL", "AMZN", "NFLX", "AVGO",
        // ETFs
        "SPY", "QQQ", "IWM", "GLD", "TLT", "XLK", "XLE", "SMH", "ARKK",
        // Crypto (slash-free canonical form)
        "BTCUSD", "ETHUSD", "SOLUSD", "DOGEUSD", "AVAXUSD", "LINKUSD"
    ];

    public static AssetKind Classify(string symbol)
        => IsCrypto(symbol) ? AssetKind.Crypto
         : IsEtf(symbol) ? AssetKind.Etf
         : AssetKind.Equity;

    public static bool IsCrypto(string symbol) => CryptoBases.Contains(CryptoBase(symbol));

    public static bool IsEtf(string symbol) => EtfSymbols.Contains(symbol.Trim().ToUpperInvariant());

    /// <summary>Company-style logic (earnings) only applies to single-name equities.</summary>
    public static bool HasEarnings(string symbol) => Classify(symbol) == AssetKind.Equity;

    /// <summary>Base ticker of a crypto symbol in any form: "BTC", "BTCUSD", "BTC/USD" -&gt; "BTC".</summary>
    public static string CryptoBase(string symbol)
    {
        var s = symbol.Trim().ToUpperInvariant();
        int sep = s.IndexOfAny(['/', '-']);
        if (sep > 0) return s[..sep];
        if (s.Length > 3 && s.EndsWith("USD", StringComparison.Ordinal)) return s[..^3];
        return s;
    }

    /// <summary>Alpaca crypto pair form used only at the provider edge: "BTCUSD" -&gt; "BTC/USD".</summary>
    public static string CryptoPair(string symbol) => $"{CryptoBase(symbol)}/USD";
}
