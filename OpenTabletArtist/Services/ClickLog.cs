using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace OpenTabletArtist.Services;

/// <summary>
/// Watches the mouse and click events macOS sees while "Record the problem" runs (TASKS #85, Digital Groceries 17 Nov 2026:
/// "record exactly what my mouse does when I click the same sequence of windows, then compare the recorded
/// mouse commands to the recorded pen click commands"). <see cref="Stop"/> returns the finished section for
/// recording.txt, or null when nothing could be watched.
/// </summary>
public interface IClickWatcher
{
    /// <summary>Starts watching. <paramref name="now"/> gives the time since the recording started.</summary>
    void Start(Func<TimeSpan> now);

    /// <summary>Stops watching and returns the section for recording.txt.</summary>
    string? Stop();
}

/// <summary>One mouse or tablet event as macOS delivered it (CGEvent fields, read in a listen-only event tap).</summary>
public readonly record struct MouseSample(
    TimeSpan At,
    int Type,
    double X,
    double Y,
    long Button,
    long ClickState,
    long Subtype,
    long EventNumber,
    double Pressure,
    long SourcePid,
    long SourceStateId,
    long SourceUserData,
    long TargetPid,
    long WindowUnder,
    long WindowThatCanHandle,
    long DeltaX,
    long DeltaY,
    ulong Flags,
    ulong Timestamp);

/// <summary>
/// The part of the click recording that needs no Mac: keeps mouse events (moves thinned out, clicks always),
/// the front app and front window as they change, and writes the comparison section of recording.txt with
/// one summary line per click: where it came from (the mouse itself or the pen's driver), which window was
/// under the pointer, and how long macOS took to bring that window and its app to the front.
/// </summary>
public sealed class ClickLog
{
    public const int MaxLines = 4000;
    public static readonly TimeSpan MoveGap = TimeSpan.FromMilliseconds(50);
    public static readonly TimeSpan FollowFor = TimeSpan.FromSeconds(3);

    public enum FrontKind { App, Window }

    private readonly record struct FrontChange(TimeSpan At, FrontKind Kind, int Pid, string Name);

    private readonly object _lock = new();
    private readonly List<MouseSample> _events = new();
    private readonly List<FrontChange> _front = new();
    private readonly Dictionary<(int Type, long Pid), MouseSample> _pending = new();
    private readonly Dictionary<(int Type, long Pid), TimeSpan> _lastKept = new();
    private readonly Dictionary<long, (int Pid, string Owner, int Layer)> _windows = new();
    private readonly Dictionary<long, string> _processes = new();
    private readonly List<string> _problems = new();
    private readonly List<string> _notes = new();
    private int _thinned, _dropped;

    public static bool IsDown(int type) => type is 1 or 3 or 25;
    public static bool IsUp(int type) => type is 2 or 4 or 26;
    private static bool IsClick(int type) => IsDown(type) || IsUp(type) || type == 24;

    public int ClickCount { get { lock (_lock) return _events.Count(e => IsDown(e.Type)); } }

    /// <summary>Window ids seen so far that have no owner yet (the watcher looks them up).</summary>
    public IReadOnlyList<long> UnnamedWindows()
    {
        lock (_lock)
            return _events.SelectMany(e => new[] { e.WindowUnder, e.WindowThatCanHandle })
                .Where(w => w > 0 && !_windows.ContainsKey(w)).Distinct().ToList();
    }

    /// <summary>Process ids seen so far that have no name yet.</summary>
    public IReadOnlyList<long> UnnamedProcesses()
    {
        lock (_lock)
            return _events.SelectMany(e => new[] { e.SourcePid, e.TargetPid })
                .Where(p => p > 0 && !_processes.ContainsKey(p)).Distinct().ToList();
    }

    /// <summary>The time of the newest kept click (down, up or proximity), or null.</summary>
    public TimeSpan? LastClickAt
    {
        get
        {
            lock (_lock)
                for (var i = _events.Count - 1; i >= 0; i--)
                    if (IsClick(_events[i].Type)) return _events[i].At;
            return null;
        }
    }

    public void Add(MouseSample s)
    {
        lock (_lock)
        {
            var key = (s.Type, s.SourcePid);
            if (IsClick(s.Type))
            {
                // The last move or drag before a click is always kept, so the click's lead-in can be compared.
                foreach (var k in _pending.Keys.Where(k => k.Pid == s.SourcePid).ToList())
                {
                    Keep(_pending[k]);
                    _lastKept[k] = _pending[k].At;
                    _pending.Remove(k);
                }
                Keep(s);
                return;
            }
            if (!_lastKept.TryGetValue(key, out var last) || s.At - last >= MoveGap)
            {
                _pending.Remove(key);
                _lastKept[key] = s.At;
                Keep(s);
            }
            else
            {
                if (_pending.ContainsKey(key)) _thinned++;
                _pending[key] = s;
            }
        }
    }

    private void Keep(MouseSample s)
    {
        if (_events.Count >= MaxLines) { _dropped++; return; }
        _events.Add(s);
    }

    /// <summary>Records the front app or front window; only changes are kept.</summary>
    public void Front(TimeSpan at, FrontKind kind, int pid, string? name)
    {
        lock (_lock)
        {
            var last = _front.LastOrDefault(f => f.Kind == kind);
            if (_front.Any(f => f.Kind == kind) && last.Pid == pid) return;
            _front.Add(new FrontChange(at, kind, pid, string.IsNullOrWhiteSpace(name) ? "?" : name!));
        }
    }

    public void NameWindow(long id, int pid, string? owner, int layer)
    {
        lock (_lock) _windows[id] = (pid, string.IsNullOrWhiteSpace(owner) ? "?" : owner!, layer);
    }

    public void NameProcess(long pid, string? name)
    {
        lock (_lock) _processes[pid] = string.IsNullOrWhiteSpace(name) ? "?" : name!;
    }

    public void Problem(string text) { lock (_lock) _problems.Add(text); }
    public void Note(string text) { lock (_lock) _notes.Add(text); }

    internal static string TypeName(int type) => type switch
    {
        1 => "LeftMouseDown", 2 => "LeftMouseUp", 3 => "RightMouseDown", 4 => "RightMouseUp", 5 => "MouseMoved",
        6 => "LeftMouseDragged", 7 => "RightMouseDragged", 22 => "ScrollWheel", 23 => "TabletPointer",
        24 => "TabletProximity", 25 => "OtherMouseDown", 26 => "OtherMouseUp", 27 => "OtherMouseDragged",
        _ => $"type {type}",
    };

    private static string StateName(long id) => id switch
    {
        -1 => "private",
        0 => "combined session",
        1 => "HID system",
        _ => id.ToString(CultureInfo.InvariantCulture),
    };

    private string Process(long pid)
    {
        if (pid == 0) return "no program (pid 0)";
        return _processes.TryGetValue(pid, out var n) ? $"{n} (pid {pid})" : $"pid {pid}";
    }

    private string Window(long id)
    {
        if (id <= 0) return "none";
        return _windows.TryGetValue(id, out var w) ? $"{id} ({w.Owner}, pid {w.Pid}, layer {w.Layer})" : $"{id} (closed or unknown)";
    }

    /// <summary>Who sent the event: the mouse or trackpad itself, or a program that posted it.</summary>
    private string Source(MouseSample s) =>
        s.SourcePid == 0 && s.SourceStateId == 1
            ? "the mouse or trackpad (HID, pid 0)"
            : $"posted by {Process(s.SourcePid)}, state {StateName(s.SourceStateId)}";

    private static string Ms(TimeSpan t) => $"{t.TotalMilliseconds:0} ms";
    private static string Sec(TimeSpan t) => $"+{t.TotalSeconds,8:0.000} s";

    internal string FormatEvent(MouseSample s) =>
        $"{Sec(s.At)}  {TypeName(s.Type)}  from {Source(s)}  at ({s.X:0.0}, {s.Y:0.0})  button {s.Button}  click {s.ClickState}"
        + $"  subtype {s.Subtype}  number {s.EventNumber}  pressure {s.Pressure:0.000}  delta {s.DeltaX},{s.DeltaY}"
        + $"  window under {Window(s.WindowUnder)}  window that can take it {Window(s.WindowThatCanHandle)}"
        + $"  target {Process(s.TargetPid)}  user data {s.SourceUserData}  flags 0x{s.Flags:X}  time {s.Timestamp}";

    public string Format()
    {
        lock (_lock)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Mouse and click events (what macOS saw)");
            sb.AppendLine("Every click and mouse event macOS delivered while recording, from the mouse or trackpad and from the pen (the");
            sb.AppendLine("tablet driver posts the pen's clicks). Moves and drags are thinned to one every "
                          + $"{MoveGap.TotalMilliseconds:0} ms per source; the last one before each click is always kept.");
            sb.AppendLine("Front app = the active app (menu bar); front window = the window on top. Both are checked every 20 ms after a click.");
            foreach (var n in _notes) sb.AppendLine(n);
            foreach (var p in _problems) sb.AppendLine("PROBLEM: " + p);
            if (_thinned > 0) sb.AppendLine($"Moves and drags thinned out: {_thinned}");
            if (_dropped > 0) sb.AppendLine($"Events not kept (over the {MaxLines} limit): {_dropped}");
            sb.AppendLine();

            var downs = _events.Where(e => IsDown(e.Type)).ToList();
            sb.AppendLine($"Clicks: {downs.Count}");
            var i = 0;
            foreach (var d in downs)
            {
                i++;
                sb.AppendLine($"  #{i} {Sec(d.At)}  {TypeName(d.Type)} from {Source(d)}  at ({d.X:0}, {d.Y:0})");
                sb.AppendLine($"       window under the pointer: {Window(d.WindowUnder)}");
                var up = _events.FirstOrDefault(e => IsUp(e.Type) && e.At >= d.At && e.SourcePid == d.SourcePid && e.Button == d.Button);
                sb.AppendLine("       " + (up.Type != 0 ? $"released after {Ms(up.At - d.At)}" : "no release recorded"));
                sb.AppendLine("       " + FrontAfter(FrontKind.App, d) + " · " + FrontAfter(FrontKind.Window, d));
            }
            sb.AppendLine();

            sb.AppendLine("Events:");
            var all = _events.Select(e => (e.At, Line: FormatEvent(e)))
                .Concat(_front.Select(f => (f.At, Line: $"{Sec(f.At)}  front {(f.Kind == FrontKind.App ? "app" : "window")}: {f.Name} (pid {f.Pid})")))
                .OrderBy(x => x.At);
            foreach (var x in all) sb.AppendLine(x.Line);
            return sb.ToString();
        }
    }

    /// <summary>What was in front at the click and when (if ever, within <see cref="FollowFor"/>) it changed after.</summary>
    private string FrontAfter(FrontKind kind, MouseSample down)
    {
        var what = kind == FrontKind.App ? "front app" : "front window";
        var before = _front.Where(f => f.Kind == kind && f.At <= down.At).Select(f => (FrontChange?)f).LastOrDefault();
        var after = _front.Where(f => f.Kind == kind && f.At > down.At && f.At - down.At <= FollowFor)
            .Select(f => (FrontChange?)f).FirstOrDefault();
        var target = _windows.TryGetValue(down.WindowUnder, out var w) ? w.Pid : (int?)null;
        if (after is { } a)
            return $"{what}: {(before is { } b ? b.Name : "?")} -> {a.Name} after {Ms(a.At - down.At)}"
                   + (target is { } t && t != a.Pid ? " (not the app under the pointer)" : "");
        if (before is { } same)
            return $"{what}: stayed {same.Name}" + (target == same.Pid ? " (already in front)" : "");
        return $"{what}: not known";
    }
}
