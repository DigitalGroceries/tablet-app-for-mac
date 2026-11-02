using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

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

    /// <summary>How long one tccutil call may take before we give up on it (item 5 / #15: was 10 s, on the UI thread).</summary>
    public static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(5);

    /// <summary>One tccutil call: exit code and output, or null when it couldn't start or didn't finish in time.</summary>
    public delegate Task<(int ExitCode, string Output)?> Runner(string[] args, TimeSpan timeout);

    /// <summary>Runs the resets off the UI thread, so Home stays responsive. Returns true only when every call
    /// started and finished in time (a non-zero exit is fine: tccutil does that when there was no record).
    /// Off macOS: false.</summary>
    public static async Task<bool> RunAsync(Runner? runner = null)
    {
        if (runner == null && !OperatingSystem.IsMacOS()) return false;
        runner ??= RunTccutilAsync;
        bool ok = true;
        foreach (var args in Commands())
        {
            var result = await Task.Run(() => runner(args, CallTimeout)).ConfigureAwait(false);
            if (result is { } r)
                AppLog.Info($"Permission reset: tccutil {string.Join(' ', args)} -> exit {r.ExitCode} {r.Output}");
            else
            {
                ok = false;
                AppLog.Warn($"Permission reset: tccutil {string.Join(' ', args)} didn't start or timed out.");
            }
        }
        return ok;
    }

    private static async Task<(int, string)?> RunTccutilAsync(string[] args, TimeSpan timeout)
    {
        try
        {
            var psi = new ProcessStartInfo("/usr/bin/tccutil") { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p == null) return null;
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            using var cts = new CancellationTokenSource(timeout);
            try { await p.WaitForExitAsync(cts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                try { p.Kill(); } catch { /* already gone */ }
                return null;
            }
            return (p.ExitCode, ((await stdout) + (await stderr)).Trim());
        }
        catch (Exception ex) { AppLog.Warn("Permission reset failed.", ex); return null; }
    }
}
