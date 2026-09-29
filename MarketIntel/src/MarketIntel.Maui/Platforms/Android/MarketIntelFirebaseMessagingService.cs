#if USE_FIREBASE
using Android.App;
using Firebase.Messaging;
using MarketIntel.Maui.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MarketIntel.Maui.Platforms.Android;

/// <summary>
/// Receives Firebase Cloud Messaging pushes on Android. Registers the device token with the
/// backend and shows a notification for foreground messages. Only compiled when the app is built
/// with -p:UseFirebase=true (and google-services.json is present). See docs/FIREBASE_SETUP.md.
/// </summary>
[Service(Exported = false)]
[IntentFilter(new[] { "com.google.firebase.MESSAGING_EVENT" })]
public sealed class MarketIntelFirebaseMessagingService : FirebaseMessagingService
{
    public override void OnNewToken(string token)
    {
        base.OnNewToken(token);
        var api = IPlatformApplication.Current?.Services.GetService<MarketApiClient>();
        if (api is not null) _ = api.RegisterDeviceAsync(token);
    }

    public override void OnMessageReceived(RemoteMessage message)
    {
        var notification = message.GetNotification();
        if (notification is null) return;

        var notifier = IPlatformApplication.Current?.Services.GetService<ILocalNotifier>();
        notifier?.Show(
            notification.Title ?? "MarketIntel",
            notification.Body ?? string.Empty,
            (int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF));
    }
}
#endif
