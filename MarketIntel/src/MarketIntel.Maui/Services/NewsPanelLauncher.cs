using System.Diagnostics;

namespace MarketIntel.Maui.Services;

/// <summary>
/// Opens the user's separate desktop app "Panel Trading" (pre-market news panel). It is a local
/// Windows executable, not a web page, so this entry point only exists on the Windows build; on
/// Android/iOS/Mac there is nothing to launch and the button stays hidden.
/// </summary>
public static class NewsPanelLauncher
{
    /// <summary>Where the user's shortcut pointed on their PC.</summary>
    public const string DefaultExePath = @"D:\Trading study\Panel Trading-win32-x64\Panel Trading.exe";

    private const string PrefKey = "newsPanel.exePath";

    /// <summary>Path to the panel's .exe. Overridable (persisted) in case the app is moved.</summary>
    public static string ExePath
    {
        get
        {
            try
            {
                var saved = Preferences.Get(PrefKey, string.Empty);
                return string.IsNullOrWhiteSpace(saved) ? DefaultExePath : saved;
            }
            catch { return DefaultExePath; }
        }
        set
        {
            try { Preferences.Set(PrefKey, value); } catch { /* preferences unavailable */ }
        }
    }

    /// <summary>True only on the Windows desktop build.</summary>
    public static bool IsSupported => DeviceInfo.Platform == DevicePlatform.WinUI;

    /// <summary>
    /// Starts the panel. Returns false (with the path it tried) when it isn't supported on this
    /// platform or the .exe isn't there; throws only if Windows itself refuses to start it.
    /// </summary>
    public static bool TryLaunch(out string path)
    {
        path = ExePath;
#if WINDOWS
        if (!File.Exists(path)) return false;
        Process.Start(new ProcessStartInfo(path)
        {
            UseShellExecute = true,
            // Electron apps resolve their resources relative to their own folder.
            WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty,
        });
        return true;
#else
        return false;
#endif
    }
}
