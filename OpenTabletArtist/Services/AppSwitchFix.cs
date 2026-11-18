using System;
using System.IO;

namespace OpenTabletArtist.Services;

/// <summary>
/// Version 29 (Zak, 17 Nov 2026; TASKS #85): the "App window switching fix" switch, Off by default.
/// While it is On, the driver asks macOS to bring the app under the pen to the front before a pen click
/// (OpenTabletDriver.Desktop.Interop.Input.MacOSAppActivator). Zak's recordings showed macOS does that by itself,
/// while the activator, where it runs, holds every click back about a second (mtr8080's report), so it is Off unless
/// a user needs it. The driver can't read the app's settings: the app keeps a flag file for it, the same way as the
/// radial menu's off file. File there = On; no file = Off (OpenTabletDriver.Desktop.Interop.Input.AppWindowSwitchingFix).
/// </summary>
public static class AppSwitchFix
{
    public const string Key = "AppWindowSwitchingFix";

    /// <summary>Same path as the driver's AppWindowSwitchingFix.FlagPath.</summary>
    public static string FlagPath { get; set; } = Path.Combine(Path.GetTempPath(), "OpenTabletArtist", "app-window-switching-fix");

    public static bool Enabled
    {
        get => AppSettings.Get(Key) == "true";
        set
        {
            if (value == Enabled) { Sync(); return; }
            AppSettings.Set(Key, value ? "true" : "false");
            AppLog.Info($"App window switching fix: switched {(value ? "on" : "off")}.");
            Sync();
        }
    }

    /// <summary>Make the driver's flag file match the switch. Called at start and on every change.
    /// Returns true when the file now matches; a failure is logged with its reason, never reported as done.</summary>
    public static bool Sync()
    {
        var on = Enabled;
        try
        {
            if (on)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FlagPath)!);
                File.WriteAllText(FlagPath, "on");
            }
            else if (File.Exists(FlagPath)) File.Delete(FlagPath);
            var ok = File.Exists(FlagPath) == on;
            if (!ok) AppLog.Warn($"App window switching fix: the flag file still doesn't match the switch ({(on ? "on" : "off")}): {FlagPath}");
            return ok;
        }
        catch (Exception e)
        {
            AppLog.Warn($"App window switching fix: couldn't {(on ? "write" : "remove")} the flag file {FlagPath}", e);
            return false;
        }
    }
}
