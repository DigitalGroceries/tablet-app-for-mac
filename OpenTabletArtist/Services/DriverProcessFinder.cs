using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenTabletArtist.Services;

/// <summary>What state a driver process is in, as far as the operating system can tell.</summary>
public enum DriverProcessState
{
    /// <summary>Alive, with a readable executable: it can answer the app (or should be able to).</summary>
    Running,

    /// <summary>Still in the process table but already on its way out: its executable is gone (macOS reports no
    /// path for it) or it is a zombie. It can't answer the app, but it may still hold the driver's
    /// "only one driver" lock, so a new driver refuses to start (TASKS #74).</summary>
    Closing,
}

/// <summary>One OpenTabletDriver daemon process.</summary>
/// <param name="Pid">Process id.</param>
/// <param name="Path">Its executable, or null when the system can't say (a closing process).</param>
/// <param name="StartTime">When it started (local time), if known.</param>
/// <param name="State">Running or closing.</param>
public sealed record DriverProcessInfo(int Pid, string? Path, DateTime? StartTime, DriverProcessState State);

/// <summary>
/// Finds the OpenTabletDriver daemon processes on this machine, including one that is closing (TASKS #74).
/// </summary>
/// <remarks>
/// <para>
/// Why not <see cref="Process.GetProcessesByName(string)"/> on macOS: .NET names a macOS process after the file
/// name of its executable path (<c>proc_pidpath</c>), and falls back to the 15-character <c>pbi_comm</c> only when
/// that fails (dotnet/runtime, ProcessManager.OSX.cs). For a driver that is closing, the kernel has already let go
/// of its executable (<c>p_textvp</c> is null, so <c>proc_pidpath</c> fails, XNU bsd/kern/proc_info.c) and its task
/// is gone (no threads, no memory: what Activity Monitor showed for the stuck driver on 13 Nov), so .NET sees a
/// nameless process and the lookup misses it. The app then said "Driver process: not running" while that process
/// still held the driver lock.
/// </para>
/// <para>
/// This finder reads <c>proc_pidinfo(PROC_PIDTBSDINFO)</c> instead, which XNU also answers for a closing or zombie
/// process. Its <c>pbi_name</c> is the executable's file name kept at exec time (up to 31 characters,
/// XNU bsd/kern/kern_exec.c), so "OpenTabletDriver.Daemon" (23) fits whole and needs no path.
/// </para>
/// </remarks>
public static class DriverProcessFinder
{
    /// <summary>The daemon's executable name.</summary>
    public const string ProcessName = "OpenTabletDriver.Daemon";

    /// <summary>Exit code the patched daemon uses when another driver already holds its lock (TASKS #74; see
    /// external/OpenTabletDriver/OpenTabletDriver.Daemon/Program.cs). Older drivers exit 0 in that case.</summary>
    public const int AlreadyRunningExitCode = 3;

    /// <summary>Every daemon process, running or closing. Never throws: a failure is logged and gives an empty list.</summary>
    public static IReadOnlyList<DriverProcessInfo> FindAll()
    {
        try
        {
            return OperatingSystem.IsMacOS() ? FindAllMac() : FindAllPortable();
        }
        catch (Exception ex)
        {
            AppLog.Warn("Driver process lookup failed; reporting no driver processes.", ex);
            return Array.Empty<DriverProcessInfo>();
        }
    }

    /// <summary>True when <paramref name="path"/> is the driver inside a copy of Tablet App for Mac
    /// (<c>…/X.app/Contents/MacOS/Daemon/OpenTabletDriver.Daemon</c>, any copy, any name) rather than an
    /// OpenTabletDriver the user installed themselves. A driver of ours that has stopped answering may be replaced
    /// without asking; the user's own may not.</summary>
    public static bool IsTabletAppDriverPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var p = path.Replace('\\', '/');
        // Universal builds kept one driver per architecture in Daemon/arm64 and Daemon/x64 (Version 7).
        return p.EndsWith(".app/Contents/MacOS/Daemon/" + ProcessName, StringComparison.Ordinal)
            || p.EndsWith(".app/Contents/MacOS/Daemon/arm64/" + ProcessName, StringComparison.Ordinal)
            || p.EndsWith(".app/Contents/MacOS/Daemon/x64/" + ProcessName, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- portable (Linux, Windows)

    private static IReadOnlyList<DriverProcessInfo> FindAllPortable()
    {
        var list = new List<DriverProcessInfo>();
        foreach (var p in Process.GetProcessesByName(ProcessName))
        {
            using (p)
            {
                string? path = null;
                DateTime? start = null;
                try { path = p.MainModule?.FileName; } catch { /* unreadable (elevated or foreign): path stays unknown */ }
                try { start = p.StartTime; } catch { /* same */ }
                list.Add(new DriverProcessInfo(p.Id, path, start, DriverProcessState.Running));
            }
        }
        return list;
    }

    // ---------------------------------------------------------------- macOS

    /// <summary>Size of <c>struct proc_bsdinfo</c> (XNU bsd/sys/proc_info.h): 12 × uint32, pbi_comm[16],
    /// pbi_name[32], 6 × 32-bit, 2 × uint64.</summary>
    internal const int BsdInfoSize = 136;
    private const int ProcPidTbsdInfo = 3;          // PROC_PIDTBSDINFO
    private const int OffsetStatus = 4;             // pbi_status (p_stat)
    private const int OffsetComm = 48;              // pbi_comm[MAXCOMLEN = 16]
    private const int OffsetName = 64;              // pbi_name[2 * MAXCOMLEN = 32]
    private const int OffsetStartSec = 120;         // pbi_start_tvsec
    internal const uint StatusZombie = 5;           // SZOMB (XNU bsd/sys/proc.h)

    /// <summary>The fields this finder needs from a <c>proc_bsdinfo</c>.</summary>
    internal readonly record struct BsdInfo(uint Status, string Comm, string Name, long StartSeconds);

    /// <summary>Reads a raw <c>proc_bsdinfo</c> (little-endian, as on every Mac this app runs on). Null when the
    /// buffer is too short.</summary>
    internal static BsdInfo? ParseBsdInfo(ReadOnlySpan<byte> raw)
    {
        if (raw.Length < BsdInfoSize) return null;
        uint status = BitConverter.ToUInt32(raw.Slice(OffsetStatus, 4));
        string comm = CString(raw.Slice(OffsetComm, 16));
        string name = CString(raw.Slice(OffsetName, 32));
        long start = (long)BitConverter.ToUInt64(raw.Slice(OffsetStartSec, 8));
        return new BsdInfo(status, comm, name, start);
    }

    /// <summary>True when <c>pbi_name</c> (the whole executable file name, set at every exec) is the daemon's.
    /// The 15-character <c>pbi_comm</c> is never used: "OpenTabletDrive" can't be told apart from OpenTabletDriver's
    /// own app ("OpenTabletDriver"), and stopping a user's own program by mistake is worse than missing a process.</summary>
    internal static bool IsDriverName(string name) =>
        string.Equals(name, ProcessName, StringComparison.OrdinalIgnoreCase);

    private static string CString(ReadOnlySpan<byte> bytes)
    {
        int n = bytes.IndexOf((byte)0);
        if (n < 0) n = bytes.Length;
        return Encoding.UTF8.GetString(bytes[..n]);
    }

    [DllImport("libproc", SetLastError = true)]
    private static extern int proc_listallpids(int[]? buffer, int buffersize);

    [DllImport("libproc", SetLastError = true)]
    private static extern int proc_pidinfo(int pid, int flavor, ulong arg, byte[] buffer, int buffersize);

    [DllImport("libproc", SetLastError = true)]
    private static extern int proc_pidpath(int pid, byte[] buffer, uint buffersize);

    private static IReadOnlyList<DriverProcessInfo> FindAllMac()
    {
        // proc_listallpids returns a count of pids; ask for the count first, then leave room for new processes.
        int count = proc_listallpids(null, 0);
        if (count <= 0) throw new InvalidOperationException($"proc_listallpids returned {count} (errno {Marshal.GetLastPInvokeError()})");
        var pids = new int[count + 64];
        int got = proc_listallpids(pids, pids.Length * sizeof(int));
        if (got <= 0) throw new InvalidOperationException($"proc_listallpids returned {got} (errno {Marshal.GetLastPInvokeError()})");

        var list = new List<DriverProcessInfo>();
        var buf = new byte[BsdInfoSize];
        var pathBuf = new byte[4 * 1024]; // PROC_PIDPATHINFO_MAXSIZE = 4 * MAXPATHLEN
        for (int i = 0; i < Math.Min(got, pids.Length); i++)
        {
            int pid = pids[i];
            if (pid <= 0) continue;
            Array.Clear(buf);
            if (proc_pidinfo(pid, ProcPidTbsdInfo, 0, buf, buf.Length) < BsdInfoSize) continue; // gone, or not ours to read
            if (ParseBsdInfo(buf) is not { } info || !IsDriverName(info.Name)) continue;

            string? path = null;
            int len = proc_pidpath(pid, pathBuf, (uint)pathBuf.Length);
            if (len > 0) path = Encoding.UTF8.GetString(pathBuf, 0, len);

            var state = info.Status == StatusZombie || path == null ? DriverProcessState.Closing : DriverProcessState.Running;
            DateTime? start = info.StartSeconds > 0 ? DateTimeOffset.FromUnixTimeSeconds(info.StartSeconds).LocalDateTime : null;
            list.Add(new DriverProcessInfo(pid, path, start, state));
        }
        return list;
    }
}
