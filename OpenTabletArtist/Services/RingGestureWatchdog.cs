using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;

namespace OpenTabletArtist.Services;

/// <summary>
/// Build J: the safety net for the EXPERIMENTAL smooth-rotate gesture, run by OpenTabletArtist (a separate
/// process from the driver, with its own Accessibility permission).
///
/// Every 250 ms it (1) writes its own heartbeat, which the plugin requires before it presses anything and
/// keeps checking during a gesture; and (2) reads the plugin's gesture heartbeat. If a gesture is marked
/// active and the driver process is gone, or the heartbeat is more than 1 s old, it posts R key-up and left
/// mouse-up itself, marks the gesture idle, stops a hung driver, logs it, and raises <see cref="Recovered"/>
/// (the app shows a message and starts the driver again).
/// </summary>
public sealed class RingGestureWatchdog : IDisposable
{
    public const int StaleMs = 1000;
    public const int PeriodMs = 250;

    /// <summary>Same files as the plugin's RingGestureFiles (per-user temp folder, inherited by the driver).</summary>
    public static string Dir { get; set; } = Path.Combine(Path.GetTempPath(), "OpenTabletArtist");
    public static string HeartbeatPath => Path.Combine(Dir, "ring-gesture");
    public static string WatchdogPath => Path.Combine(Dir, "ota-watchdog");

    /// <summary>Raised (on a timer thread) after input was released; the argument is the reason.</summary>
    public event Action<string>? Recovered;

    private Timer? _timer;
    private int _busy;

    public void Start()
    {
        if (!OperatingSystem.IsMacOS()) return;   // the gesture only exists on macOS
        try { Directory.CreateDirectory(Dir); }
        catch (Exception e) { AppLog.Warn("Ring gesture watchdog unavailable", e); return; }
        _timer = new Timer(_ => Tick(), null, 0, PeriodMs);
    }

    public static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>Pure decision: should input be rescued? <paramref name="heartbeat"/> is the plugin's file
    /// ("active|idle pid unixMs"), null if missing.</summary>
    public static (bool Recover, int Pid, string Reason) Decide(string? heartbeat, long nowMs, Func<int, bool> isAlive)
    {
        if (string.IsNullOrWhiteSpace(heartbeat)) return (false, 0, "");
        var parts = heartbeat.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 || parts[0] != "active") return (false, 0, "");
        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid)
            || !long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var at))
            return (true, 0, "the gesture state was unreadable");
        if (!isAlive(pid)) return (true, pid, "the driver stopped");
        if (nowMs - at > StaleMs) return (true, pid, "the driver stopped responding");
        return (false, pid, "");
    }

    private void Tick()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            long now = NowMs();
            WriteAtomically(WatchdogPath, now.ToString(CultureInfo.InvariantCulture));
            string? hb = null;
            try { hb = File.ReadAllText(HeartbeatPath); }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            catch (IOException) { return; }   // mid-replace: next tick
            var (recover, pid, reason) = Decide(hb, now, IsAlive);
            if (recover) Rescue(pid, reason, now);
        }
        catch (Exception e)
        {
            AppLog.Warn("Ring gesture watchdog tick failed", e);
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private void Rescue(int pid, string reason, long now)
    {
        // Let go first — that's the part that matters to the artist.
        MacDesktop.ReleaseRotateInput();
        WriteAtomically(HeartbeatPath, $"idle {pid} {now}");
        bool killed = false;
        if (pid > 0 && IsAlive(pid))
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                // Only ever stop the driver this gesture came from.
                if (p.ProcessName.Contains("OpenTabletDriver", StringComparison.OrdinalIgnoreCase))
                {
                    p.Kill();
                    killed = true;
                }
            }
            catch (Exception e) { AppLog.Warn("Couldn't stop the unresponsive driver", e); }
        }
        AppLog.Warn($"Touch ring smooth gesture rescued: {reason}. Released R, Control, Option and the mouse button"
                    + (killed ? "; stopped the unresponsive driver." : "."));
        Recovered?.Invoke(reason);
    }

    private static bool IsAlive(int pid)
    {
        if (pid <= 0) return false;
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch (Exception) { return false; }
    }

    private static void WriteAtomically(string path, string content)
    {
        var tmp = path + "." + Environment.ProcessId + ".tmp";
        File.WriteAllText(tmp, content);
        File.Move(tmp, path, overwrite: true);
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
        // Gone now: the plugin refuses new gestures and releases a running one at its next step.
        try { File.Delete(WatchdogPath); } catch (Exception) { }
    }
}
