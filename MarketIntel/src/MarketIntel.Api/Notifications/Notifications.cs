using System.Collections.Concurrent;

namespace MarketIntel.Api.Notifications;

/// <summary>
/// Server-side push abstraction. The logging implementation is the seam where a real provider
/// (Firebase Cloud Messaging for Android, APNs for iOS) plugs in for background delivery.
/// </summary>
public interface INotificationService
{
    Task SendAsync(string deviceToken, string title, string body, CancellationToken ct = default);
}

/// <summary>Dev implementation: logs the notification. Replace with an FCM/APNs sender in production.</summary>
public sealed class LoggingNotificationService : INotificationService
{
    private readonly ILogger<LoggingNotificationService> _log;
    public LoggingNotificationService(ILogger<LoggingNotificationService> log) => _log = log;

    public Task SendAsync(string deviceToken, string title, string body, CancellationToken ct = default)
    {
        _log.LogInformation("PUSH → {Token}: {Title} — {Body}", Truncate(deviceToken), title, body);
        return Task.CompletedTask;
    }

    private static string Truncate(string s) => s.Length <= 8 ? s : s[..8] + "…";
}

/// <summary>In-memory registry of device push tokens. Backed by a database in production.</summary>
public sealed class DeviceRegistry
{
    private readonly ConcurrentDictionary<string, byte> _tokens = new();

    public void Register(string token) { if (!string.IsNullOrWhiteSpace(token)) _tokens[token] = 1; }
    public IReadOnlyCollection<string> Tokens => _tokens.Keys.ToList();
    public int Count => _tokens.Count;
}
