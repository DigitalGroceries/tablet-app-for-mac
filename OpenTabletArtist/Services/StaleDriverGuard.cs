using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Services;

/// <summary>
/// Version 15 (Zak, 27 Oct 2026): after an update the app showed "User-supplied OTD Daemon" and macOS asked
/// for Device Control and Data Access again, until "Quit and stop the daemon" and a reopen fixed it. Cause:
/// an update replaces the app, but the driver process the old app started keeps running the old copy —
/// either from the old app that the in-app updater moved aside, or (after a drag-install) from a file that
/// has since been replaced on disk.
/// <para>
/// At start, before the app connects, this stops such a leftover. It only ever touches a driver that an app
/// like this one ships inside its bundle (<c>…/Contents/MacOS/Daemon/&lt;arch&gt;/</c>), never an
/// OpenTabletDriver the artist installed themselves.
/// </para>
/// </summary>
internal static class StaleDriverGuard
{
    const string BundleDriverFolder = "/Contents/MacOS/Daemon/";

    /// <summary>Pure decision, unit-tested: should the running driver at <paramref name="processPath"/>,
    /// started at <paramref name="processStartUtc"/>, be stopped in favour of this app's own copy at
    /// <paramref name="bundledPath"/> (written at <paramref name="bundledWriteUtc"/>)?</summary>
    public static bool IsStale(string? processPath, DateTime processStartUtc, string bundledPath, DateTime bundledWriteUtc)
    {
        if (string.IsNullOrEmpty(processPath) || string.IsNullOrEmpty(bundledPath)) return false;
        if (!processPath.Contains(BundleDriverFolder, StringComparison.Ordinal)) return false;   // not an app-bundled copy
        if (!string.Equals(Path.GetFileName(processPath), DaemonExePaths.DaemonExeName, StringComparison.Ordinal)) return false;
        // An older app's copy (moved aside by the updater, or another install of this app).
        if (!string.Equals(processPath, bundledPath, StringComparison.Ordinal)) return true;
        // Our own path, but the file was replaced after the process started (drag-install over a running driver).
        return processStartUtc < bundledWriteUtc - TimeSpan.FromSeconds(2);
    }

    /// <summary>Version 15: is the process at <paramref name="processPath"/> the driver shipped inside the app at
    /// <paramref name="appPath"/>? Pure, unit-tested.</summary>
    public static bool IsDriverOf(string? processPath, string? appPath)
    {
        if (string.IsNullOrEmpty(processPath) || string.IsNullOrEmpty(appPath)) return false;
        return processPath.StartsWith(appPath.TrimEnd('/') + BundleDriverFolder, StringComparison.Ordinal)
            && string.Equals(Path.GetFileName(processPath), DaemonExePaths.DaemonExeName, StringComparison.Ordinal);
    }

    /// <summary>Version 15: process ids of the driver running from inside the app at <paramref name="appPath"/>
    /// (macOS only). The updater stops these once the app has quit, so the version it installs — newer or
    /// earlier — starts with its own driver instead of finding the old one still running.</summary>
    public static int[] DriverPidsOf(string appPath)
    {
        if (!OperatingSystem.IsMacOS()) return Array.Empty<int>();
        var pids = new System.Collections.Generic.List<int>();
        try
        {
            foreach (var p in Process.GetProcesses())
            {
                try { if (p.Id != Environment.ProcessId && IsDriverOf(PathOf(p.Id), appPath)) pids.Add(p.Id); }
                catch { }
                finally { p.Dispose(); }
            }
        }
        catch (Exception ex) { AppLog.Debug("Driver: couldn't list the running driver.", ex); }
        return pids.ToArray();
    }

    [DllImport("/usr/lib/libSystem.dylib")]
    static extern int proc_pidpath(int pid, byte[] buffer, uint size);

    static string? PathOf(int pid)
    {
        try
        {
            var buf = new byte[4096];
            int n = proc_pidpath(pid, buf, (uint)buf.Length);
            return n > 0 ? Encoding.UTF8.GetString(buf, 0, n) : null;
        }
        catch { return null; }
    }

    /// <summary>Stops any leftover bundled driver from an older version. Returns how many were stopped.</summary>
    public static int StopIfStale()
    {
        if (!OperatingSystem.IsMacOS()) return 0;
        int stopped = 0;
        try
        {
            var bundled = DaemonExePaths.BundledPath(AppContext.BaseDirectory);
            if (!File.Exists(bundled)) return 0;
            var written = File.GetLastWriteTimeUtc(bundled);
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == Environment.ProcessId) continue;
                    var path = PathOf(p.Id);
                    if (path == null || !path.EndsWith("/" + DaemonExePaths.DaemonExeName, StringComparison.Ordinal)) continue;
                    if (!IsStale(path, p.StartTime.ToUniversalTime(), bundled, written)) continue;
                    AppLog.Info($"Driver: stopping a leftover driver from an older version ({path}, started {p.StartTime:u}).");
                    p.Kill();
                    p.WaitForExit(5000);
                    stopped++;
                }
                catch (Exception ex) { AppLog.Debug("Driver: couldn't check or stop one process.", ex); }
                finally { p.Dispose(); }
            }
        }
        catch (Exception ex) { AppLog.Warn("Driver: the leftover-driver check failed.", ex); }
        return stopped;
    }
}
