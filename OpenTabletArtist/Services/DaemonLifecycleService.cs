using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using OpenTabletArtist.Domain;
using OtdInterop;

namespace OpenTabletArtist.Services;

/// <summary>
/// Owns the OTD daemon process lifecycle: locating the built exe, launching, stopping,
/// and resolving process paths. Extracted from <c>MainViewModel</c> so the process/
/// filesystem seam is isolated behind an interface (it can be faked in view-model tests,
/// and the daemon-process concerns live in one place).
/// </summary>
/// <remarks>
/// Also the library's <see cref="IDaemonProcessLocator"/>: the two facts OtdInterop needs about a running
/// daemon -- which executable a process id belongs to, and which one is running when the connection
/// cannot say -- are already here, and they are facts rather than decisions. What stays on this side of
/// the line is everything around them: which executable the app expects, whether the app installed it,
/// and whether the user is asked before it is stopped.
/// </remarks>
public interface IDaemonLifecycleService : IDaemonProcessLocator
{
    /// <summary>Preferred daemon executable, resolved once at application startup. Null if none is present.</summary>
    string? ExpectedExePath();

    /// <summary>Daemon exe to launch: the expected build, falling back to a running instance's path. Null if none found.</summary>
    string? FindExe();

    /// <summary>True when <paramref name="path"/> is a daemon this project produced (bundled release copy
    /// or submodule dev build), as opposed to an OTD installed on the system that we merely drive. An
    /// adopted install is a daemon the user owns, so destructive actions on it stay behind a confirmation
    /// even though we resolved and launched it. See docs/design/official-otd-release.md.</summary>
    bool IsAppManaged(string? path);

    /// <summary>True if any OTD daemon process is currently running.</summary>
    bool IsRunning();

    /// <summary>True when a daemon is bundled with this build of the app (<c>&lt;app&gt;/Daemon/</c>), i.e.
    /// when "use the bundled daemon instead" is an offer we can actually honour. False on macOS today,
    /// where nothing is bundled.</summary>
    bool HasBundledDaemon();

    /// <summary>Launches the daemon with no window, if an exe can be found. Waits briefly to catch the
    /// case where it starts and exits immediately — a daemon that is present but can't run (a missing
    /// .NET runtime for a framework-dependent build, a broken install) otherwise shows up only as a
    /// silent 30-second connect timeout, which says nothing about what went wrong.</summary>
    /// <returns>The problem to report, or null when the daemon was launched and is still alive (or when
    /// there was nothing to launch — the caller already reports that as "exe missing").</returns>
    /// <param name="executablePath">The pinned connected executable for a restart, or null for the startup selection.</param>
    string? Launch(string? executablePath = null);

    /// <summary>
    /// Kills ONE daemon process by id (best effort) — the one we are connected to. Prefer this over
    /// <see cref="StopAll"/> wherever the pid is known: Stop should stop the daemon this app is talking
    /// to, not every process that happens to share its name (#601).
    /// </summary>
    /// <returns>True if the process is gone (killed, or already not running); false if it could not be
    /// stopped, or if the pid turned out not to be a daemon at all.</returns>
    bool Stop(int processId);

    /// <summary>Kills all running OTD daemon processes (best effort). The fallback for when the connected
    /// daemon's pid isn't known — off-Windows, where the pipe→pid lookup is unavailable and the daemon is
    /// effectively a singleton anyway.</summary>
    void StopAll();

    /// <summary>TASKS #74: <see cref="Launch"/>, and when the new driver refuses to start because another one
    /// still holds the "only one driver" lock but can't answer (a driver left closing by another copy of the
    /// app), stop that one and start ours once more. A driver that is running normally is connected to instead,
    /// and an OpenTabletDriver the user installed is never stopped without asking. Every step is logged.</summary>
    /// <returns>The problem to report, or null when a driver is up (ours, or a running one to connect to).</returns>
    string? LaunchOrReplaceStuck(string? executablePath = null) => Launch(executablePath);

    /// <summary>TASKS #74: stops every driver process that came from a copy of Tablet App for Mac (or is already
    /// closing), so a fresh one can start. Used when the driver the app attached to never answered. Returns false
    /// when there was one it wasn't allowed to stop (the user's own install) or one that wouldn't go away.</summary>
    bool StopUnansweringDrivers() => false;
}

/// <inheritdoc />
public class DaemonLifecycleService : IDaemonLifecycleService
{
    private const string ProcessName = DriverProcessFinder.ProcessName;

    private readonly Func<IReadOnlyList<DriverProcessInfo>> _find;
    private readonly Func<int, int, int> _signal;
    private readonly Action<TimeSpan> _sleep;

    public DaemonLifecycleService() : this(DriverProcessFinder.FindAll, null, null) { }

    /// <summary>Tests: a fake process table, signal sender (pid, signal) → 0 on success, and clock.</summary>
    internal DaemonLifecycleService(Func<IReadOnlyList<DriverProcessInfo>> find, Func<int, int, int>? signal,
        Action<TimeSpan>? sleep)
    {
        _find = find;
        _signal = signal ?? SendSignal;
        _sleep = sleep ?? Thread.Sleep;
    }

    private readonly string? _startupPath =
        DaemonExePaths.Candidates(AppContext.BaseDirectory).FirstOrDefault(File.Exists);
    public string? ExpectedExePath() => _startupPath;

    public bool IsAppManaged(string? path) => DaemonExePaths.IsAppManaged(AppContext.BaseDirectory, path);

    public string? FindExe()
    {
        var expected = ExpectedExePath();
        if (expected != null) return expected;

        // Fallback: if a daemon is already running, use its path.
        return _find().FirstOrDefault(p => p.State == DriverProcessState.Running && p.Path != null)?.Path;
    }

    /// <summary>A driver that can answer is running. A closing one (TASKS #74) doesn't count: it can't be
    /// connected to, so the app must start its own (and clear the closing one if it blocks that).</summary>
    public bool IsRunning() => _find().Any(p => p.State == DriverProcessState.Running);

    public bool HasBundledDaemon() => File.Exists(DaemonExePaths.BundledPath(AppContext.BaseDirectory));

    /// <summary>How long to watch a freshly started daemon before assuming it is alive. Long enough to
    /// catch an apphost bailing out (that happens in tens of milliseconds), short enough not to be felt
    /// on the Start button.</summary>
    private static readonly TimeSpan EarlyExitWindow = TimeSpan.FromSeconds(2);

    public string? Launch(string? executablePath = null)
    {
        var daemonPath = executablePath ?? FindExe();
        if (daemonPath == null) return null;   // "nothing to launch" is the caller's own message

        Process? proc;
        try
        {
            proc = Process.Start(new ProcessStartInfo(daemonPath)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(daemonPath) ?? "",
            });
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Couldn't start the daemon at {daemonPath}.", ex);
            return $"The driver at {daemonPath} couldn't be started: {ex.Message}";
        }

        if (proc == null) return $"The driver at {daemonPath} couldn't be started.";
        LastLaunchBlockers = Array.Empty<DriverProcessInfo>();

        // Deliberately no stdout/stderr redirection: the pipes would outlive this call for a healthy
        // daemon, and tearing them down when OTA quits can break a daemon that was working fine. The exit
        // code plus the path is enough to stop the failure being invisible.
        try
        {
            if (!proc.WaitForExit(EarlyExitWindow)) return null;   // still running — the normal path

            AppLog.Warn($"The daemon at {daemonPath} exited immediately (exit code {proc.ExitCode}).");

            // The host has a specific exit code for "I found no runtime to run this", so say that
            // outright instead of offering it as one possibility among several (#786, D2). This is the
            // authoritative signal: it comes from the thing that actually resolves the runtime, and it
            // is right even when a directory listing suggests otherwise.
            if (Domain.DotnetRuntime.IsMissingRuntimeExit(proc.ExitCode))
            {
                return $"The driver at {daemonPath} needs the .NET {Domain.DotnetRuntime.DaemonMajor} "
                     + "runtime, and this machine doesn't have it. Install \".NET Runtime "
                     + $"{Domain.DotnetRuntime.DaemonMajor}.0\" (x64) from Microsoft and try again. If "
                     + "you have another OpenTabletDriver that runs, start it yourself and Tablet App for Mac will "
                     + "connect to it.";
            }

            // TASKS #74: "another driver already holds the lock". Our patched driver says so with its own exit
            // code; older drivers exit 0 after printing it, so 0 counts when a driver process is still there.
            var others = _find();
            if (proc.ExitCode == DriverProcessFinder.AlreadyRunningExitCode || (proc.ExitCode == 0 && others.Count > 0))
            {
                LastLaunchBlockers = others;
                AppLog.Warn($"The new driver refused to start: another driver holds the driver lock ({Describe(others)}).");
                return AlreadyRunningMessage(others);
            }

            return $"The driver at {daemonPath} started and exited immediately (exit code {proc.ExitCode}). "
                 + "It may be an incomplete install. Reinstall or repair Tablet App for Mac, or start an "
                 + "OpenTabletDriver yourself and Tablet App for Mac will connect to it.";
        }
        catch (Exception ex)
        {
            // Couldn't observe it (permissions, already reaped) — don't turn a working start into an error.
            AppLog.Debug($"Couldn't watch the daemon at {daemonPath} for an early exit.", ex);
            return null;
        }
        finally { proc.Dispose(); }
    }

    public bool Stop(int processId)
    {
        try
        {
            using var proc = Process.GetProcessById(processId);
            // Process ids get recycled. Between the pipe→pid lookup and this call the daemon may have
            // exited and something else inherited its number, so confirm what we are about to kill really
            // is a daemon. Without this, a stale pid turns "Stop the daemon" into "kill an arbitrary
            // process", which is worse than the kill-by-name behaviour this replaces.
            if (!string.Equals(proc.ProcessName, ProcessName, StringComparison.OrdinalIgnoreCase))
            {
                AppLog.Warn($"Not stopping pid {processId}: it is \"{proc.ProcessName}\", not {ProcessName}.");
                return false;
            }
            GracefulProcessStop.Stop(proc);   // SIGTERM first (#73): the driver lets go of held mouse buttons, then kill
            return true;
        }
        catch (ArgumentException)
        {
            // No process with that id — it has already exited, which is the outcome Stop wanted.
            return true;
        }
        catch (Exception ex)
        {
            // A failed Kill means the Stop didn't fully take — worth a Warn, not a silent no-op (#21).
            AppLog.Warn($"Couldn't stop daemon process (pid {processId}).", ex);
            return false;
        }
    }

    public void StopAll()
    {
        // TASKS #74: the finder also sees a driver that is closing, which Process.GetProcessesByName misses on macOS.
        var all = _find();
        if (all.Count == 0) return;
        var left = StopPids(all.Select(p => p.Pid).ToList());
        // A failed stop means the Stop didn't fully take — worth a Warn, not a silent no-op (#21).
        if (left.Count > 0) AppLog.Warn($"Couldn't stop driver process(es) {string.Join(", ", left)}.");
    }

    /// <summary>The drivers <see cref="Launch"/> found holding the lock when it last refused to start (TASKS #74).</summary>
    internal IReadOnlyList<DriverProcessInfo> LastLaunchBlockers { get; private set; } = Array.Empty<DriverProcessInfo>();

    public string? LaunchOrReplaceStuck(string? executablePath = null)
    {
        var problem = Launch(executablePath);
        var blockers = LastLaunchBlockers;
        if (problem == null || blockers.Count == 0) return problem;

        // A driver that is running normally appeared meanwhile (another copy of the app started it): connect to
        // it rather than stopping a working driver. If it never answers, Fix replaces it (StopUnansweringDrivers).
        if (blockers.Any(b => b.State == DriverProcessState.Running))
        {
            AppLog.Info($"Another driver is running ({Describe(blockers)}); connecting to it instead of starting ours.");
            return null;
        }

        // Only closing drivers left: they can't answer, but they hold the lock. Clear them and start ours.
        AppLog.Info($"Replacing a driver that is closing and can't answer ({Describe(blockers)}): stopping it, then starting ours.");
        var left = StopPids(blockers.Select(b => b.Pid).ToList());
        if (left.Count > 0)
        {
            AppLog.Warn($"Couldn't stop the closing driver process(es) {string.Join(", ", left)}; the new driver can't start.");
            return $"An old tablet driver (process {string.Join(", ", left)}) is stuck while closing and couldn't be stopped, "
                 + "so a new one can't start. Restart your Mac to clear it.";
        }
        var again = Launch(executablePath);
        if (again == null) AppLog.Info("The stuck driver is gone; the new driver started.");
        else AppLog.Warn($"The stuck driver is gone, but the new driver still didn't start: {again}");
        return again;
    }

    public bool StopUnansweringDrivers()
    {
        var all = _find();
        if (all.Count == 0) { AppLog.Info("Unanswering driver: none running any more."); return true; }
        var notOurs = all.Where(p => p.State == DriverProcessState.Running && !IsOursToReplace(p)).ToList();
        if (notOurs.Count > 0)
        {
            AppLog.Info($"Unanswering driver: not stopping {Describe(notOurs)}: it isn't Tablet App for Mac's own driver.");
            return false;
        }
        AppLog.Info($"Unanswering driver: stopping {Describe(all)}.");
        var left = StopPids(all.Select(p => p.Pid).ToList());
        if (left.Count == 0) { AppLog.Info("Unanswering driver: stopped."); return true; }
        AppLog.Warn($"Unanswering driver: process(es) {string.Join(", ", left)} would not stop.");
        return false;
    }

    /// <summary>A driver we may stop without asking: closing (it is going anyway), the driver of this copy, or the
    /// driver inside any other copy of Tablet App for Mac.</summary>
    private bool IsOursToReplace(DriverProcessInfo p) =>
        p.State == DriverProcessState.Closing || IsAppManaged(p.Path) || DriverProcessFinder.IsTabletAppDriverPath(p.Path);

    /// <summary>How long a driver gets to exit after SIGTERM, and then after SIGKILL.</summary>
    internal static readonly TimeSpan StopGrace = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan StopPoll = TimeSpan.FromMilliseconds(200);
    private const int SigTerm = 15, SigKill = 9;

    /// <summary>Asks each process to exit (SIGTERM, so it can let go of a held mouse button, #73), waits, then
    /// kills what is left and waits again. Only processes the finder still lists as drivers are signalled, so a
    /// reused process id is never hit. Returns the ids still there at the end.</summary>
    internal IReadOnlyList<int> StopPids(IReadOnlyCollection<int> pids)
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (var pid in pids)
            {
                try { using var p = Process.GetProcessById(pid); p.Kill(); }
                catch (Exception ex) { AppLog.Warn($"Driver stop: couldn't stop process {pid}.", ex); }
            }
            return Remaining(pids);
        }

        var targets = Remaining(pids);
        foreach (var pid in targets)
        {
            int rc = _signal(pid, SigTerm);
            AppLog.Info(rc == 0 ? $"Driver stop: asked process {pid} to exit (SIGTERM)."
                                : $"Driver stop: SIGTERM to process {pid} failed (result {rc}).");
        }
        targets = WaitGone(targets);
        if (targets.Count == 0) { AppLog.Info("Driver stop: every driver exited after the polite request."); return targets; }

        foreach (var pid in targets)
        {
            int rc = _signal(pid, SigKill);
            AppLog.Info(rc == 0 ? $"Driver stop: process {pid} didn't exit in {StopGrace.TotalSeconds:0} s; killed it (SIGKILL)."
                                : $"Driver stop: SIGKILL to process {pid} failed (result {rc}).");
        }
        targets = WaitGone(targets);
        if (targets.Count == 0) AppLog.Info("Driver stop: every driver is gone.");
        return targets;
    }

    private List<int> Remaining(IReadOnlyCollection<int> pids)
    {
        var alive = _find().Select(p => p.Pid).ToHashSet();
        return pids.Where(alive.Contains).ToList();
    }

    private List<int> WaitGone(List<int> pids)
    {
        var waited = TimeSpan.Zero;
        var left = Remaining(pids);
        while (left.Count > 0 && waited < StopGrace)
        {
            _sleep(StopPoll);
            waited += StopPoll;
            left = Remaining(left);
        }
        return left;
    }

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int PosixKill(int pid, int signal);

    private static int SendSignal(int pid, int signal)
    {
        try { return PosixKill(pid, signal) == 0 ? 0 : Marshal.GetLastPInvokeError(); }
        catch (Exception ex) { AppLog.Warn($"Driver stop: couldn't send signal {signal} to process {pid}.", ex); return -1; }
    }

    internal static string Describe(IEnumerable<DriverProcessInfo> list) =>
        string.Join("; ", list.Select(p => $"process {p.Pid}, {(p.State == DriverProcessState.Closing ? "closing" : "running")}"
            + (p.Path != null ? $", {p.Path}" : ", no path")
            + (p.StartTime is { } t ? $", started {t:yyyy-MM-dd HH:mm:ss}" : "")));

    /// <summary>What the user reads when a new driver can't start because another one holds the lock.</summary>
    internal static string AlreadyRunningMessage(IReadOnlyList<DriverProcessInfo> others) => others.Count == 0
        ? "Another tablet driver is holding the driver lock, so a new one can't start, and it can't be found. "
          + "Restart your Mac to clear it."
        : $"Another tablet driver is already running (process {string.Join(", ", others.Select(o => o.Pid))}) but isn't "
          + "answering, so a new one can't start. Press Fix to stop it and start the driver again.";

    public string? PathOf(int processId)
    {
        try
        {
            using var proc = Process.GetProcessById(processId);
            return proc.MainModule?.FileName;
        }
        catch (Exception ex)
        {
            // Reading another process's module path can fail (e.g. an elevated daemon) — expected, Debug (#21).
            AppLog.Debug($"Couldn't read daemon exe path for pid {processId}.", ex);
            return null;
        }
    }

    public string? SingleRunningDaemonPath()
    {
        // Only when unambiguous — with multiple daemons we can't tell which the pipe connects to.
        var running = _find().Where(p => p.State == DriverProcessState.Running).ToList();
        return running.Count == 1 ? running[0].Path : null;
    }
}
