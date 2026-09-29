using System.Net.Http.Json;
using Google.Apis.Auth.OAuth2;

namespace MarketIntel.Api.Notifications;

/// <summary>Configuration for Firebase Cloud Messaging (bound from the "Fcm" section).</summary>
public sealed class FcmOptions
{
    public const string SectionName = "Fcm";

    public string? ProjectId { get; set; }
    /// <summary>Path to the Firebase service-account JSON file (mounted as a secret in production).</summary>
    public string? ServiceAccountPath { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ProjectId) &&
        !string.IsNullOrWhiteSpace(ServiceAccountPath) &&
        File.Exists(ServiceAccountPath);
}

/// <summary>
/// Real push delivery via the Firebase Cloud Messaging HTTP v1 API. Sends to Android (and iOS via
/// APNs behind FCM) using a Google service-account credential. Enabled only when configured;
/// otherwise the app registers the logging sender instead.
/// </summary>
public sealed class FcmNotificationService : INotificationService
{
    private const string Scope = "https://www.googleapis.com/auth/firebase.messaging";

    private readonly HttpClient _http;
    private readonly FcmOptions _options;
    private readonly ILogger<FcmNotificationService> _log;
    private GoogleCredential? _credential;

    public FcmNotificationService(HttpClient http, FcmOptions options, ILogger<FcmNotificationService> log)
    {
        _http = http;
        _options = options;
        _log = log;
    }

    public async Task SendAsync(string deviceToken, string title, string body, CancellationToken ct = default)
    {
        string accessToken = await GetAccessTokenAsync(ct);

        var payload = new
        {
            message = new
            {
                token = deviceToken,
                notification = new { title, body },
                android = new { priority = "high" }
            }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post,
            $"https://fcm.googleapis.com/v1/projects/{_options.ProjectId}/messages:send");
        req.Headers.Authorization = new("Bearer", accessToken);
        req.Content = JsonContent.Create(payload);

        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
            _log.LogWarning("FCM send failed: {Status}", resp.StatusCode);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        _credential ??= GoogleCredential.FromFile(_options.ServiceAccountPath).CreateScoped(Scope);
        return await _credential.UnderlyingCredential.GetAccessTokenForRequestAsync(cancellationToken: ct);
    }
}
