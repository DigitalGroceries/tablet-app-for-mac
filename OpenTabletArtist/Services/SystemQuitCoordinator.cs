using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenTabletArtist.Services;

/// <summary>
/// R11: handles a quit the system asks for (Cmd+Q, Dock Quit, AppleScript quit, logout, shutdown, restart).
/// It never cancels the request (cancelling is what macOS reports as "User cancelled" / "interrupted
/// shutdown"), never shows a dialog, writes unsaved edits to a snapshot (never the real settings) within a bounded budget, closes the
/// session and stops a daemon this app started, then lets the exit proceed.
/// </summary>
internal sealed class SystemQuitCoordinator
{
    public static readonly TimeSpan SaveBudget = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan SequenceBudget = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan TotalBudget = TimeSpan.FromSeconds(6);

    private readonly Action _allowClose;
    private readonly Func<Task<bool>>? _autoSave;
    private readonly Func<TimeSpan, Task<bool>>? _closeSession;
    private readonly Func<Func<Task>?>? _prepareStop;
    private readonly Action<Func<Task>, TimeSpan> _waitBlocking;
    private readonly Action<string> _warn;
    private int _started;

    public SystemQuitCoordinator(Action allowClose, Func<Task<bool>>? autoSave,
        Func<TimeSpan, Task<bool>>? closeSession, Func<Func<Task>?>? prepareStop,
        Action<Func<Task>, TimeSpan> waitBlocking, Action<string> warn)
    {
        _allowClose = allowClose; _autoSave = autoSave; _closeSession = closeSession;
        _prepareStop = prepareStop; _waitBlocking = waitBlocking; _warn = warn;
    }

    public bool HasRun => Volatile.Read(ref _started) != 0;

    /// <summary>Called from ShutdownRequested. Returns the value Cancel must have: always false.</summary>
    public bool OnShutdownRequested()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) return false;
        try { _allowClose(); } catch (Exception ex) { _warn($"System quit: allow close failed: {ex}"); }
        try { _waitBlocking(RunAsync, TotalBudget); }
        catch (Exception ex) { _warn($"System quit: shutdown work failed: {ex}"); }
        return false;
    }

    private async Task RunAsync()
    {
        if (_autoSave != null)
        {
            try
            {
                var save = _autoSave();
                if (await Task.WhenAny(save, Task.Delay(SaveBudget)).ConfigureAwait(true) != save)
                    _warn("System quit: unsaved-changes snapshot did not finish in time; unsaved changes discarded.");
                else if (!await save.ConfigureAwait(true))
                    _warn("System quit: unsaved-changes snapshot failed; unsaved changes discarded.");
            }
            catch (Exception ex) { _warn($"System quit: unsaved-changes snapshot threw; unsaved changes discarded: {ex}"); }
        }

        Func<Task>? stop = null;
        try { stop = _prepareStop?.Invoke(); }
        catch (Exception ex) { _warn($"System quit: preparing daemon stop failed: {ex}"); }

        await QuitSequence.RunAsync(_closeSession, stop, _warn, SequenceBudget).ConfigureAwait(true);
    }

    /// <summary>Waits for <paramref name="work"/> on the UI thread by pumping a nested dispatcher loop,
    /// bounded by <paramref name="limit"/>, so async steps that need the UI thread can still finish.</summary>
    public static void PumpUiUntil(Func<Task> work, TimeSpan limit)
    {
        using var cts = new CancellationTokenSource(limit);
        Task task;
        try { task = work(); } catch { return; }
        if (task.IsCompleted) return;
        task.ContinueWith(_ => { try { cts.Cancel(); } catch (ObjectDisposedException) { } },
            TaskScheduler.Default);
        Avalonia.Threading.Dispatcher.UIThread.MainLoop(cts.Token);
    }
}
