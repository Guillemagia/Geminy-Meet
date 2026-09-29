using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MarketIntel.Maui.Localization;
using MarketIntel.Shared.Contracts;

namespace MarketIntel.Maui.Services;

/// <summary>Typed client over the MarketIntel API. Shares the exact DTO contracts with the backend.</summary>
public sealed class MarketApiClient
{
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public MarketApiClient(HttpClient http)
    {
        _http = http;
        _http.BaseAddress = new Uri(ApiConfig.BaseUrl);
        _http.Timeout = TimeSpan.FromSeconds(30);

        // Tell the backend which language to generate analysis text in; keep it in sync with the
        // user's language choice so a mid-session switch is reflected on the next request.
        ApplyLanguageHeader();
        LocalizationResourceManager.Instance.PropertyChanged += (_, _) => ApplyLanguageHeader();
    }

    private void ApplyLanguageHeader()
    {
        _http.DefaultRequestHeaders.Remove("Accept-Language");
        _http.DefaultRequestHeaders.Add("Accept-Language", LocalizationResourceManager.Instance.CurrentLanguage);
    }

    /// <summary>Register this device's FCM push token with the backend.</summary>
    public async Task RegisterDeviceAsync(string token, CancellationToken ct = default)
    {
        try { await _http.PostAsync($"/api/devices?token={Uri.EscapeDataString(token)}", null, ct); }
        catch { /* best-effort */ }
    }

    public Task<MetaDto?> GetMetaAsync(CancellationToken ct = default)
        => _http.GetFromJsonAsync<MetaDto>("/api/meta", Json, ct);

    public Task<MarketRegimeDto?> GetRegimeAsync(CancellationToken ct = default)
        => _http.GetFromJsonAsync<MarketRegimeDto>("/api/market/regime", Json, ct);

    public async Task<AccuracyDashboardDto?> GetAccuracyAsync(CancellationToken ct = default)
    {
        try { return await _http.GetFromJsonAsync<AccuracyDashboardDto>("/api/accuracy", Json, ct); }
        catch { return null; }
    }

    public async Task<CalibrationReportDto?> GetCalibrationAsync(CancellationToken ct = default)
    {
        try { return await _http.GetFromJsonAsync<CalibrationReportDto>("/api/calibration", Json, ct); }
        catch { return null; }
    }

    public async Task<SignalRegistryStatsDto?> GetRegistryAsync(CancellationToken ct = default)
    {
        try { return await _http.GetFromJsonAsync<SignalRegistryStatsDto>("/api/registry", Json, ct); }
        catch { return null; }
    }

    public async Task<IReadOnlyList<EconomicEventDto>> GetUpcomingEventsAsync(int hours = 120, CancellationToken ct = default)
        => await _http.GetFromJsonAsync<List<EconomicEventDto>>($"/api/calendar/upcoming?hours={hours}", Json, ct) ?? [];

    public async Task<IReadOnlyList<OpportunityDto>> GetTopOpportunitiesAsync(int take = 5, CancellationToken ct = default)
        => await _http.GetFromJsonAsync<List<OpportunityDto>>($"/api/scanner/top?take={take}", Json, ct) ?? [];

    public async Task<IReadOnlyList<OpportunityDto>> GetWatchlistAsync(IEnumerable<string> symbols, CancellationToken ct = default)
    {
        var csv = Uri.EscapeDataString(string.Join(",", symbols));
        if (string.IsNullOrEmpty(csv)) return [];
        return await _http.GetFromJsonAsync<List<OpportunityDto>>($"/api/watchlist?symbols={csv}", Json, ct) ?? [];
    }

    public Task<SignalDto?> GetSignalAsync(string symbol, CancellationToken ct = default)
        => _http.GetFromJsonAsync<SignalDto>($"/api/signals/{symbol}", Json, ct);

    public async Task<BacktestResultDto?> GetBacktestAsync(string symbol, CancellationToken ct = default)
    {
        try { return await _http.GetFromJsonAsync<BacktestResultDto>($"/api/backtest/{symbol}", Json, ct); }
        catch { return null; } // 404 when there isn't enough history
    }

    public async Task<OptionsAnalysisDto?> GetOptionsAsync(string symbol, CancellationToken ct = default)
    {
        try { return await _http.GetFromJsonAsync<OptionsAnalysisDto>($"/api/options/{symbol}", Json, ct); }
        catch { return null; }
    }

    public Task<AssistantReplyDto?> AskAsync(string symbol, string question, CancellationToken ct = default)
        => _http.GetFromJsonAsync<AssistantReplyDto>($"/api/assistant/{symbol}?q={Uri.EscapeDataString(question)}", Json, ct);

    public async Task<NewsAnalysisDto?> GetNewsAsync(string symbol, CancellationToken ct = default)
    {
        try { return await _http.GetFromJsonAsync<NewsAnalysisDto>($"/api/news/{symbol}", Json, ct); }
        catch { return null; }
    }
}
