namespace MarketIntel.Api.MarketData;

/// <summary>
/// Configuration for the Alpaca market-data provider. Bound from the "Alpaca" configuration
/// section (appsettings, user-secrets or environment variables). Keys are NEVER committed.
/// </summary>
public sealed class AlpacaOptions
{
    public const string SectionName = "Alpaca";

    public string? ApiKeyId { get; set; }
    public string? ApiSecretKey { get; set; }

    /// <summary>Market-data host. Default is the standard Alpaca data API.</summary>
    public string DataBaseUrl { get; set; } = "https://data.alpaca.markets";

    /// <summary>Data feed. Free accounts use "iex"; paid plans can use "sip".</summary>
    public string Feed { get; set; } = "iex";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKeyId) && !string.IsNullOrWhiteSpace(ApiSecretKey);
}
