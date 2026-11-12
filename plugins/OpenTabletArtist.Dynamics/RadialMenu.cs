using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using OpenTabletArtist.Domain;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.DependencyInjection;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Platform.Keyboard;
using OpenTabletDriver.Plugin.Platform.Pointer;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletArtist.Dynamics;

/// <summary>
/// Radial menu (TASKS #42), like Wacom's: a pen button or ExpressKey set to it shows an 8-slice menu centred
/// on the pointer. Tap a slice with the pen tip to use it. Holding the button while tapping keeps the menu open
/// (Wacom's rule), so Undo can be tapped several times. Tapping the centre, an empty slice or outside the ring,
/// or pressing the button again, closes it. The menu lives in this binding's "Menu" setting (JSON), so it is
/// saved with the tablet's settings, per application and per pen like any other binding.
/// The app draws the menu (Services/RadialMenuOverlay) from the state file this writes. The pen taps are read here;
/// the Mac mouse's clicks come back from the app through a small command file (TASKS #54, <see cref="RadialMenuState.HandleCommand"/>).
/// The pen tip is kept from the app underneath by <see cref="RadialMenuFilter"/> while the menu is open.
/// </summary>
[PluginName("Tablet App for Mac - Radial menu")]
public class RadialMenuBinding : IStateBinding
{
    [Property("Menu"), ToolTip("The menu, as JSON. Edit it in Tablet App for Mac.")]
    public string? Menu { set; get; }

    [Resolved] public IVirtualKeyboard? Keyboard { set; get; }
    [Resolved] public IMouseButtonHandler? Pointer { set; get; }

    private bool _warned;

    public void Press(TabletReference tablet, IDeviceReport report)
    {
        if (!RadialMenuFilter.Installed && !_warned)
        {
            Log.Write("Radial menu", "The radial menu filter isn't in this profile; save the binding again in Tablet App for Mac.",
                LogLevel.Warning);
            _warned = true;
        }
        RadialMenuState.ButtonDown(this);
    }

    public void Release(TabletReference tablet, IDeviceReport report) => RadialMenuState.ButtonUp(this);

    public override string ToString() => "Radial menu";
}

/// <summary>Keeps the pen tip from reaching the app under the menu while it is open (pressure 0 for the whole
/// stroke that started on the menu), and tells the menu where the tip went down.</summary>
[PluginName("Tablet App for Mac - Radial menu filter")]
public class RadialMenuFilter : IPositionedPipelineElement<IDeviceReport>
{
    private static long _constructed;
    public static bool Installed => Interlocked.Read(ref _constructed) != 0;

    public RadialMenuFilter() => Interlocked.Exchange(ref _constructed, 1);

    public PipelinePosition Position => PipelinePosition.PreTransform;
    public event Action<IDeviceReport>? Emit;

    private bool _down;
    private bool _swallow;

    public void Consume(IDeviceReport value)
    {
        if (value is OutOfRangeReport)
        {
            if (_swallow) RadialMenuState.TipUp();
            _down = false;
            _swallow = false;
        }
        else if (RingGestureGate.IsPenOnly(value) && value is ITabletReport pen)
        {
            bool down = pen.Pressure > 0;
            if (down && !_down && RadialMenuState.SwallowsPen)
            {
                _swallow = true;               // this whole stroke belongs to the menu
                RadialMenuState.TipDown();
            }
            else if (down && _swallow) RadialMenuState.PenMoved();   // a drag by the hand
            if (!down)
            {
                if (_swallow) RadialMenuState.TipUp();
                _swallow = false;
            }
            _down = down;
            if (_swallow || (down && RadialMenuState.SwallowsPen)) pen.Pressure = 0;
            RadialMenuState.CloseIfIdle();
        }
        Emit?.Invoke(value);
    }
}

/// <summary>The one radial menu that can be open, shared by the binding and the filter. Thread-safe.</summary>
public static class RadialMenuState
{
    /// <summary>Close by itself after this long without a tap, so a forgotten menu never blocks the pen.</summary>
    public static int IdleCloseMs { get; set; } = 60_000;

    /// <summary>Per-user temp folder, shared with the app (same place as the ring-mode file).</summary>
    public static string StateFilePath { get; set; } =
        Path.Combine(Path.GetTempPath(), "OpenTabletArtist", "radial-menu");

    /// <summary>The app writes this file while its Radial menu switch is Off; then the function does nothing.</summary>
    public static string OffFlagPath { get; set; } =
        Path.Combine(Path.GetTempPath(), "OpenTabletArtist", "radial-menu-off");

    /// <summary>The app writes the Mac mouse's clicks here (<see cref="RadialMenuCommand"/>); polled while the menu is open.</summary>
    public static string CommandFilePath { get; set; } =
        Path.Combine(Path.GetTempPath(), "OpenTabletArtist", "radial-menu-cmd");

    /// <summary>How often the command file is looked at while the menu is open.</summary>
    public static int CommandPollMs { get; set; } = 40;

    /// <summary>Where the pointer is (macOS global points); tests replace it.</summary>
    public static Func<(double X, double Y)?> CursorProvider { get; set; } = MacCursor.Location;

    /// <summary>Runs an action; tests replace it. Default: keys, clicks and opening apps for real.</summary>
    public static Action<RadialMenuBinding, RadialSlice>? ActionRunner { get; set; }

    private static readonly object Gate = new();
    private static RadialMenuBinding? _owner;
    private static RadialMenuDef? _top;
    private static readonly List<int> _path = new();
    private static double _cx, _cy;
    private static bool _held;
    private static long _lastActivity;
    private static long _seq;
    private static readonly KeyPulse Pulse = new() { HoldMs = 30 };
    private static Timer? _commandTimer;
    private static long _commandSeqSeen;
    private static bool _parked;   // the mouse used or moved the menu: it stays until closed (no idle close)
    private static bool _editing;  // the pencil was tapped: the next slice tapped is edited, not run
    private static bool _confirm;  // "Restore the 8 default slices?" is showing: Yes (right) / No (left)
    private static bool _busy;     // the app is editing a slice: the menu stays shown but the pen goes through to the app
    private static long _busySince;
    private static long _reqSeq;
    // TASKS #65: the slice that ran last (for the app's short "Selection copied" text) and whether it worked. Sent in the next state write.
    private static long _usedSeq;
    private static (long Seq, string Label, string Keys, string Action, bool Ok)? _used;
    private static (long Seq, string Kind, int Slice, int[] Path)? _request;   // written once, with the next state
    // A drag by the hand with the pen tip: where the pen and the menu were when it started.
    private static bool _penDrag, _penDragMoved;
    private static double _dragPx, _dragPy, _dragCx, _dragCy;
    private static long _lastDragWrite;
    /// <summary>The app edits a slice for at most this long before the pen is taken over by the menu again.</summary>
    public static int BusyMaxMs { get; set; } = 300_000;
    public const double DragThreshold = 4;

    public static bool IsOpen { get { lock (Gate) return _top != null; } }

    /// <summary>The menu is open and takes the pen tip (it doesn't while the app is editing a slice, so the pen can click there).</summary>
    public static bool SwallowsPen
    {
        get
        {
            lock (Gate)
            {
                if (_top == null) return false;
                if (_busy && Environment.TickCount64 - _busySince > BusyMaxMs) _busy = false;
                return !_busy;
            }
        }
    }

    public static bool Editing { get { lock (Gate) return _editing; } }
    public static bool Confirming { get { lock (Gate) return _confirm; } }
    public static bool Busy { get { lock (Gate) return _busy; } }

    /// <summary>Slice numbers of the open submenus, from the top (tests).</summary>
    public static IReadOnlyList<int> OpenPath { get { lock (Gate) return _path.ToArray(); } }

    internal static void ButtonDown(RadialMenuBinding binding)
    {
        lock (Gate)
        {
            if (_top != null)
            {
                // Pressing the button again closes it (Wacom), whichever button opened it.
                CloseLocked("button pressed again");
                return;
            }
            if (File.Exists(OffFlagPath)) return;
            var at = CursorProvider();
            if (at is not { } c) return;   // not macOS, or no pointer: do nothing
            _owner = binding;
            _top = RadialMenuJson.Parse(binding.Menu);
            _path.Clear();
            _cx = c.X; _cy = c.Y;
            _held = true;
            _parked = false;
            _editing = false; _confirm = false; _busy = false; _penDrag = false; _request = null; _used = null;
            Touch();
            WriteStateLocked();
            StartCommandPollingLocked();
        }
    }

    internal static void ButtonUp(RadialMenuBinding binding)
    {
        lock (Gate) if (ReferenceEquals(binding, _owner)) _held = false;
    }

    /// <summary>The pen tip went down while the menu is open: act on whatever is under the pointer.</summary>
    public static void TipDown()
    {
        RadialSlice? run = null;
        RadialMenuBinding? owner;
        lock (Gate)
        {
            if (_top == null) return;
            owner = _owner;
            Touch();
            var at = CursorProvider();
            if (at is not { } c) { CloseLocked("no pointer"); return; }
            int slice = RadialMenuGeometry.SliceAt(c.X - _cx, c.Y - _cy, _top.Size);
            if (slice == RadialMenuGeometry.Outside) { CloseLocked("outside"); return; }
            if (slice == RadialMenuGeometry.Hand)
            {
                // The hand: only a drag does anything (see PenMoved); a tap that doesn't move changes nothing.
                _penDrag = true; _penDragMoved = false;
                _dragPx = c.X; _dragPy = c.Y; _dragCx = _cx; _dragCy = _cy;
                return;
            }
            if (slice < 0) { CentreQuarterLocked(slice); return; }
            run = SliceTappedLocked(slice, fromMouse: false);
            if (run != null && !_held) CloseLocked("used " + Describe(run));
            else if (run != null) WriteStateLocked();   // flash the choice; the menu stays while the button is held
        }
        if (run != null && owner != null) RunAndReport(owner, run);
    }

    /// <summary>The X, the pencil or the circular arrow was tapped (pen or mouse).</summary>
    private static void CentreQuarterLocked(int quarter)
    {
        switch (quarter)
        {
            case RadialMenuGeometry.Centre:
                CloseLocked("centre");
                break;
            case RadialMenuGeometry.Edit:
                _confirm = false;
                _editing = !_editing;   // tapping the pencil again puts it down
                WriteStateLocked();
                break;
            case RadialMenuGeometry.Restore:
                _editing = false;
                _confirm = true;
                WriteStateLocked();
                break;
        }
    }

    /// <summary>A slice was tapped. Normal: its action to run (null when it only opened a submenu, did nothing or was
    /// used up by edit / restore). Pen taps on an empty slice close the menu; mouse taps leave it open.</summary>
    private static RadialSlice? SliceTappedLocked(int slice, bool fromMouse)
    {
        if (_top == null) return null;
        if (_confirm)
        {
            _confirm = false;
            if (slice == 2) NewRequestLocked("restore", -1);   // Yes is on the right, No on the left
            else WriteStateLocked();
            return null;
        }
        var level = RadialMenuJson.Level(_top, _path) ?? _top;
        if (_editing)
        {
            _editing = false;
            _busy = true;
            _busySince = Environment.TickCount64;
            NewRequestLocked("edit", slice);
            return null;
        }
        var s = level.Slices[slice];
        if (!s.IsSet)
        {
            if (!fromMouse) CloseLocked("empty slice");
            return null;
        }
        if (s.Action == RadialActions.Submenu)
        {
            _path.Add(slice);
            WriteStateLocked();
            return null;
        }
        if (s.Action == RadialActions.Click && fromMouse)
        {
            // "Click at the pointer" would land on the menu itself.
            Log.Write("Radial menu", "A click slice needs the pen: the mouse pointer is on the menu.", LogLevel.Debug);
            return null;
        }
        return s;
    }

    private static void NewRequestLocked(string kind, int slice)
    {
        _request = (++_reqSeq, kind, slice, _path.ToArray());
        WriteStateLocked();
        _request = null;
    }

    /// <summary>The pen moved while its tip is down on the hand: once it has moved a few points the menu follows it.</summary>
    public static void PenMoved()
    {
        lock (Gate)
        {
            if (_top == null || !_penDrag) return;
            if (CursorProvider() is not { } c) return;
            double dx = c.X - _dragPx, dy = c.Y - _dragPy;
            if (!_penDragMoved && Math.Sqrt(dx * dx + dy * dy) > DragThreshold) _penDragMoved = true;
            if (!_penDragMoved) return;
            _cx = _dragCx + dx; _cy = _dragCy + dy;
            _parked = true;
            Touch();
            if (Environment.TickCount64 - _lastDragWrite >= 30)
            {
                _lastDragWrite = Environment.TickCount64;
                WriteStateLocked();
            }
        }
    }

    /// <summary>The pen tip came up (or the pen left): a drag ends where it is.</summary>
    public static void TipUp()
    {
        lock (Gate)
        {
            if (!_penDrag) return;
            _penDrag = false;
            if (_penDragMoved && _top != null) WriteStateLocked();
            _penDragMoved = false;
        }
    }

    public static void CloseIfIdle()
    {
        lock (Gate)
        {
            if (_top != null && !_parked && !_busy && Environment.TickCount64 - Interlocked.Read(ref _lastActivity) > IdleCloseMs)
                CloseLocked("no tap for a minute");
        }
    }

    public static void Close(string why) { lock (Gate) CloseLocked(why); }

    private static void CloseLocked(string why)
    {
        if (_top == null) return;
        _top = null;
        _owner = null;
        _path.Clear();
        _held = false;
        _parked = false;
        _editing = false; _confirm = false; _busy = false; _penDrag = false; _request = null;
        StopCommandPollingLocked();
        WriteStateLocked();
        Log.Write("Radial menu", "Closed: " + why, LogLevel.Debug);
    }

    private static void Touch() => Interlocked.Exchange(ref _lastActivity, Environment.TickCount64);

    private static string Describe(RadialSlice s) => string.IsNullOrWhiteSpace(s.Label) ? s.Action : s.Label;

    // --- the Mac mouse (TASKS #54) ---

    private static void StartCommandPollingLocked()
    {
        StopCommandPollingLocked();
        _commandSeqSeen = ReadCommandFile()?.Seq ?? 0;   // whatever is there now is old news
        _commandTimer = new Timer(_ => PollCommands(), null, CommandPollMs, CommandPollMs);
    }

    private static void StopCommandPollingLocked()
    {
        _commandTimer?.Dispose();
        _commandTimer = null;
    }

    private static RadialMenuCommand? ReadCommandFile()
    {
        try { return File.Exists(CommandFilePath) ? RadialMenuCommand.Parse(File.ReadAllText(CommandFilePath)) : null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>Look at the command file once (the timer does this while the menu is open; tests call it).</summary>
    public static void PollCommands()
    {
        var cmd = ReadCommandFile();
        if (cmd == null) return;
        lock (Gate)
        {
            if (_top == null || cmd.Seq <= _commandSeqSeen) return;
            _commandSeqSeen = cmd.Seq;
        }
        HandleCommand(cmd);
    }

    /// <summary>The mouse used the menu (or the app finished an edit). A slice runs and the menu STAYS open (so the 8
    /// commands can be used one after another); the X and Esc close it; a drag moves it; the pencil, the circular arrow and
    /// the app's answer are handled as for the pen. Nothing happens when no menu is open.</summary>
    public static void HandleCommand(RadialMenuCommand cmd)
    {
        RadialSlice? run = null;
        RadialMenuBinding? owner;
        lock (Gate)
        {
            if (_top == null) return;
            owner = _owner;
            Touch();
            switch (cmd.Op)
            {
                case RadialMenuCommand.Centre:
                case RadialMenuCommand.Close:
                    CloseLocked("mouse " + cmd.Op);
                    return;
                case RadialMenuCommand.Edit:
                    _parked = true;
                    CentreQuarterLocked(RadialMenuGeometry.Edit);
                    return;
                case RadialMenuCommand.Restore:
                    _parked = true;
                    CentreQuarterLocked(RadialMenuGeometry.Restore);
                    return;
                case RadialMenuCommand.MenuChanged:
                    // The app edited (or restored, or cancelled): use this menu from now on and let the pen back in.
                    _busy = false;
                    if (!string.IsNullOrWhiteSpace(cmd.Menu))
                    {
                        _top = RadialMenuJson.Parse(cmd.Menu);
                        if (RadialMenuJson.Level(_top, _path) == null) _path.Clear();
                    }
                    WriteStateLocked();
                    return;
                case RadialMenuCommand.Move:
                    // The app moved its window itself; keep the pen's idea of the centre the same. No state write:
                    // the app would move the window back to an older position.
                    _cx = cmd.X; _cy = cmd.Y;
                    _parked = true;
                    return;
                case RadialMenuCommand.Tap:
                    _parked = true;
                    if (cmd.Slice < 0 || cmd.Slice >= RadialMenuDef.SliceCount) return;
                    run = SliceTappedLocked(cmd.Slice, fromMouse: true);
                    break;
                default:
                    return;
            }
        }
        if (run != null && owner != null) RunAndReport(owner, run);
    }

    /// <summary>Run a slice, then tell the app which one ran and whether it worked (TASKS #65: its short confirmation text, or the
    /// failure). Written with the next state, which is the closed state when this slice closed the menu.</summary>
    private static void RunAndReport(RadialMenuBinding owner, RadialSlice s)
    {
        bool ok = Run(owner, s);
        lock (Gate)
        {
            _used = (++_usedSeq, s.Label ?? "", s.Keys ?? "", s.Action ?? "", ok);
            WriteStateLocked();
        }
    }

    /// <summary>True when the action was carried out; false (and logged) when it could not be.</summary>
    private static bool Run(RadialMenuBinding owner, RadialSlice s)
    {
        if (ActionRunner != null) { ActionRunner(owner, s); return true; }
        try
        {
            switch (s.Action)
            {
                case RadialActions.Keys:
                    var keys = RingModeBindingsKeys.Parse(s.Keys, owner.Keyboard);
                    if (keys.Length > 0 && owner.Keyboard != null) { Pulse.Press(owner.Keyboard, keys); return true; }
                    Log.Write("Radial menu", $"Keys not supported: '{s.Keys}'", LogLevel.Warning);
                    return false;
                case RadialActions.Click:
                    return Click(owner.Pointer, s.Click);
                case RadialActions.Open:
                    return OpenApp(s.App);
            }
            return true;
        }
        catch (Exception e)
        {
            Log.Write("Radial menu", $"Action failed: {e.Message}", LogLevel.Warning);
            return false;
        }
    }

    private static bool Click(IMouseButtonHandler? pointer, string? which)
    {
        if (pointer == null || string.IsNullOrWhiteSpace(which))
        {
            Log.Write("Radial menu", "Click not done: no pointer or no button chosen.", LogLevel.Warning);
            return false;
        }
        bool dbl = string.Equals(which, "DoubleClick", StringComparison.OrdinalIgnoreCase);
        if (!Enum.TryParse<MouseButton>(dbl ? "Left" : which, true, out var b))
        {
            Log.Write("Radial menu", $"Click not done: unknown button '{which}'.", LogLevel.Warning);
            return false;
        }
        for (int i = 0; i < (dbl ? 2 : 1); i++)
        {
            pointer.MouseDown(b);
            pointer.MouseUp(b);
            if (pointer is ISynchronousPointer sync) sync.Flush();
        }
        return true;
    }

    private static bool OpenApp(string? app)
    {
        if (string.IsNullOrWhiteSpace(app) || !OperatingSystem.IsMacOS())
        {
            Log.Write("Radial menu", "Open app not done: no app chosen (or not macOS).", LogLevel.Warning);
            return false;
        }
        var psi = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
        if (app.EndsWith(".app", StringComparison.OrdinalIgnoreCase) || app.Contains('/'))
        {
            psi.ArgumentList.Add("-a");
            psi.ArgumentList.Add(app);
        }
        else
        {
            psi.ArgumentList.Add("-b");
            psi.ArgumentList.Add(app);
        }
        Process.Start(psi)?.Dispose();
        return true;
    }

    private static object? UsedPayloadLocked() =>
        _used is { } u ? new { seq = u.Seq, label = u.Label, keys = u.Keys, action = u.Action, ok = u.Ok } : null;

    /// <summary>What the app's overlay reads: open or closed, where, how big, and the current ring's slices.</summary>
    private static void WriteStateLocked()
    {
        try
        {
            var seq = ++_seq;
            object payload;
            if (_top == null)
            {
                payload = new { state = "closed", seq, used = UsedPayloadLocked() };
            }
            else
            {
                var level = RadialMenuJson.Level(_top, _path) ?? _top;
                var slices = new List<object>();
                for (int i = 0; i < level.Slices.Count; i++)
                {
                    var s = level.Slices[i];
                    // While "Restore the 8 default slices?" is showing, only Yes (right) and No (left) are on the ring.
                    if (_confirm) slices.Add(new { label = "", action = RadialActions.None, set = i == 2 || i == 6 });
                    else slices.Add(new { label = s.Label, action = s.Action, set = s.IsSet });
                }
                object? request = _request is { } r ? new { seq = r.Seq, kind = r.Kind, slice = r.Slice, path = r.Path } : null;
                payload = new
                {
                    state = "open", seq, x = _cx, y = _cy, size = _top.Size, transparency = _top.Transparency, depth = _path.Count, slices,
                    editing = _editing, confirm = _confirm, busy = _busy,
                    menu = RadialMenuJson.Serialize(_top), request, used = UsedPayloadLocked(),
                };
            }
            var json = JsonSerializer.Serialize(payload);
            var dir = Path.GetDirectoryName(StateFilePath)!;
            Directory.CreateDirectory(dir);
            var tmp = StateFilePath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, StateFilePath, overwrite: true);
        }
        catch (Exception e)
        {
            Log.Write("Radial menu", $"Couldn't write the menu state: {e.Message}", LogLevel.Warning);
        }
    }

    /// <summary>Tests: forget everything.</summary>
    public static void ResetForTests()
    {
        lock (Gate)
        {
            _top = null; _owner = null; _path.Clear(); _held = false; _seq = 0; _parked = false;
            _editing = false; _confirm = false; _busy = false; _penDrag = false; _request = null; _used = null; _usedSeq = 0;
            StopCommandPollingLocked();
            Interlocked.Exchange(ref _lastActivity, Environment.TickCount64);
        }
    }

    /// <summary>Tests: let go of the button as if by hand.</summary>
    public static bool Held { get { lock (Gate) return _held; } }
}

/// <summary>Same key parsing as the touch ring (only keys this Mac's keyboard supports).</summary>
internal static class RingModeBindingsKeys
{
    public static string[] Parse(string? keys, IVirtualKeyboard? keyboard) => RingModeTurnBinding.ParseKeys(keys, keyboard);
}

/// <summary>The pointer position in macOS global points (top-left origin), or null elsewhere.</summary>
internal static class MacCursor
{
    private const string CG = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CF = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    [StructLayout(LayoutKind.Sequential)] private struct CGPoint { public double X, Y; }
    [DllImport(CG)] private static extern IntPtr CGEventCreate(IntPtr source);
    [DllImport(CG)] private static extern CGPoint CGEventGetLocation(IntPtr evt);
    [DllImport(CF)] private static extern void CFRelease(IntPtr cf);

    public static (double X, double Y)? Location()
    {
        if (!OperatingSystem.IsMacOS()) return null;
        try
        {
            var e = CGEventCreate(IntPtr.Zero);
            if (e == IntPtr.Zero) return null;
            try { var p = CGEventGetLocation(e); return (p.X, p.Y); }
            finally { CFRelease(e); }
        }
        catch (Exception) { return null; }
    }
}
