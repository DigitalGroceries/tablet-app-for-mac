using System;
using System.Linq;

namespace OpenTabletArtist.Services;

/// <summary>
/// Looks up the running OpenTabletDriver daemon process — independent of OTA's IPC connection — for the
/// DAEMON PROCESS card: whether one is running, its executable path, and when it started (→ uptime).
/// Best-effort: any failure reports "not running". TASKS #74: through <see cref="DriverProcessFinder"/>, which
/// also sees a driver that is closing (on macOS .NET's own process lookup misses it), so the card no longer says
/// "not running" while such a driver still blocks a new one.
/// </summary>
public static class DaemonProcess
{
    /// <summary>Snapshot of the daemon process. <paramref name="Closing"/>: only a closing driver is left (it
    /// can't answer, but it is still there).</summary>
    public sealed record Info(bool Running, DateTime? StartTime, string? Path, bool Closing = false);

    public static Info Query() => From(DriverProcessFinder.FindAll());

    /// <summary>Pure: picks what the card shows from the finder's list (unit-tested).</summary>
    internal static Info From(System.Collections.Generic.IReadOnlyList<DriverProcessInfo> all)
    {
        // The daemon is effectively a singleton; prefer one that can answer.
        var running = all.FirstOrDefault(p => p.State == DriverProcessState.Running);
        if (running != null) return new Info(true, running.StartTime, running.Path);
        var closing = all.FirstOrDefault();
        return closing != null ? new Info(false, closing.StartTime, null, Closing: true) : new Info(false, null, null);
    }
}
