using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace OpenTabletArtist.Services;

/// <summary>Issue #14: macOS keeps old permission records for earlier builds of the app, and then refuses to
/// let the toggles stay on for the new build. This clears our own Input Monitoring and Accessibility records
/// with <c>tccutil reset</c> so the user can add the current version again.</summary>
public static class PermissionReset
{
    public const string AppBundleId = "com.freetabletapp.mac";
    public const string DriverBundleId = "com.freetabletapp.mac.daemon";

    /// <summary>The exact tccutil calls, in order (pure, unit-tested).</summary>
    public static IReadOnlyList<string[]> Commands() => new[]
    {
        new[] { "reset", "ListenEvent", AppBundleId },
        new[] { "reset", "Accessibility", AppBundleId },
        new[] { "reset", "ListenEvent", DriverBundleId },
        new[] { "reset", "Accessibility", DriverBundleId },
    };

    /// <summary>True when the app runs from a disk image or from macOS's App Translocation copy (quarantined
    /// app opened from Downloads). Permissions granted there do not stick (pure, unit-tested).</summary>
    public static bool IsRunningFromDiskImageOrTranslocated(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        return path.Contains("/AppTranslocation/", StringComparison.Ordinal)
            || path.StartsWith("/Volumes/", StringComparison.Ordinal)
            || path.StartsWith("/private/var/folders/", StringComparison.Ordinal);
    }

    public static bool IsRunningFromDiskImageOrTranslocated()
        => OperatingSystem.IsMacOS() && IsRunningFromDiskImageOrTranslocated(AppContext.BaseDirectory);

    /// <summary>Runs the resets. Returns true when every call succeeded. No-op (false) off macOS.</summary>
    public static bool Run()
    {
        if (!OperatingSystem.IsMacOS()) return false;
        bool ok = true;
        foreach (var args in Commands())
        {
            try
            {
                var psi = new ProcessStartInfo("/usr/bin/tccutil") { UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var a in args) psi.ArgumentList.Add(a);
                using var p = Process.Start(psi)!;
                p.WaitForExit(10000);
                var output = (p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd()).Trim();
                AppLog.Info($"Permission reset: tccutil {string.Join(' ', args)} -> exit {p.ExitCode} {output}");
                // tccutil exits non-zero when there was simply no record; that is fine.
            }
            catch (Exception ex) { ok = false; AppLog.Warn("Permission reset failed.", ex); }
        }
        return ok;
    }
}
