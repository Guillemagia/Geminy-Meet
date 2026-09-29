#if ANDROID
using Android.App;
using Android.Content;
using AndroidX.Core.App;
#endif

namespace MarketIntel.Maui.Services;

/// <summary>Shows a real on-device notification (Android). No-op on platforms without an impl.</summary>
public interface ILocalNotifier
{
    Task<bool> EnsurePermissionAsync();
    void Show(string title, string message, int id);
}

#if ANDROID
public sealed class LocalNotifier : ILocalNotifier
{
    private const string ChannelId = "marketintel_alerts";
    private bool _channelReady;

    private sealed class PostNotificationsPermission : Permissions.BasePlatformPermission
    {
        public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
            [("android.permission.POST_NOTIFICATIONS", true)];
    }

    public async Task<bool> EnsurePermissionAsync()
    {
        // Runtime notification permission is required on Android 13 (API 33)+.
        if (!OperatingSystem.IsAndroidVersionAtLeast(33)) return true;
        var status = await Permissions.RequestAsync<PostNotificationsPermission>();
        return status == PermissionStatus.Granted;
    }

    public void Show(string title, string message, int id)
    {
        var ctx = Android.App.Application.Context;
        if (ctx is null) return;
        if (ctx.GetSystemService(Context.NotificationService) is not NotificationManager manager) return;

        if (!_channelReady && OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            var loc = Localization.LocalizationResourceManager.Instance;
            var channel = new NotificationChannel(ChannelId, loc.Get("Notif_ChannelName"), NotificationImportance.High)
            {
                Description = loc.Get("Notif_ChannelDesc")
            };
            manager.CreateNotificationChannel(channel);
            _channelReady = true;
        }

        var builder = new NotificationCompat.Builder(ctx, ChannelId);
        builder.SetContentTitle(title);
        builder.SetContentText(message);
        builder.SetSmallIcon(Android.Resource.Drawable.IcDialogInfo);
        builder.SetAutoCancel(true);
        builder.SetPriority((int)NotificationPriority.High);

        var notification = builder.Build();
        if (notification is not null) manager.Notify(id, notification);
    }
}
#else
public sealed class LocalNotifier : ILocalNotifier
{
    public Task<bool> EnsurePermissionAsync() => Task.FromResult(false);
    public void Show(string title, string message, int id) { }
}
#endif
