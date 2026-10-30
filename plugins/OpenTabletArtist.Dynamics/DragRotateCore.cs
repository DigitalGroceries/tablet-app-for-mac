using System;

namespace OpenTabletArtist.Dynamics;

/// <summary>What the drag-rotate gesture needs from the system. macOS implementation: MacGestureIo.</summary>
public interface IGestureIo
{
    (double X, double Y)? Cursor();
    /// <summary>Bounds of the frontmost normal window, only if it belongs to com.adobe.Photoshop and contains
    /// the cursor; otherwise null.</summary>
    (double X, double Y, double W, double H)? PhotoshopWindowUnderCursor();
    /// <summary>OpenTabletArtist's watchdog heartbeat is recent (OTA is running and can rescue input).</summary>
    bool WatchdogFresh();
    /// <summary>The pen gate filter is in the pipeline (so pen reports can be held back).</summary>
    bool GateInstalled();
    /// <summary>Pen tip or a pen button is down.</summary>
    bool PenBusy();
    void KeyR(bool down);
    void MouseDown(double x, double y);
    void MouseDrag(double x, double y);
    void MouseUp(double x, double y);
    void Warp(double x, double y);
    /// <summary>Write the "gesture active/idle" heartbeat the OTA watchdog reads.</summary>
    void Heartbeat(bool active);
    void Log(string message);

    /// <summary>Build K: hold (or let go of) Control + Option — Photoshop's on-canvas brush HUD modifiers.
    /// While held, mouse events carry the Control + Option flags.</summary>
    void KeyControlOption(bool down) { }
}

/// <summary>Version 16 BETA 2: whose front window is under the cursor, for the smooth ring actions.</summary>
public enum FrontApp { Other, Photoshop, Affinity }

/// <summary>Build K: what a ring drag gesture does in Photoshop.</summary>
public enum GestureKind
{
    /// <summary>Hold R and drag along an arc around the window centre (build J).</summary>
    Rotate,
    /// <summary>Hold Control + Option, press and drag sideways: brush size (right = bigger).</summary>
    BrushSize,
    /// <summary>Hold Control + Option, press and drag vertically: brush hardness (up = harder; UNVERIFIED).</summary>
    BrushHardness,
}

/// <summary>
/// Build J (EXPERIMENTAL): smooth canvas rotation in Photoshop by holding R (spring-loaded Rotate View) and
/// dragging the mouse along an arc around the window centre, driven by the ring. Pure state machine — all
/// system effects go through <see cref="IGestureIo"/>, so the safety rules are unit-tested.
///
/// Safety rules: starts only in Photoshop's window under the cursor, with OTA's watchdog alive, the pen gate
/// installed and the pen tip/buttons up. Releases (mouse-up, R-up, cursor restored) after
/// <see cref="IdleReleaseMs"/> without ring movement, after <see cref="HardCapMs"/> regardless, or as soon as
/// the watchdog goes stale. The heartbeat is marked active before anything is pressed, so a crash at any
/// point after a press is visible to the watchdog.
/// </summary>
public sealed class DragRotateCore
{
    public const int IdleReleaseMs = 300;
    public const int HardCapMs = 3000;
    public const double MinRadius = 60;
    private const int RefuseLogEveryMs = 2000;

    private readonly IGestureIo _io;
    private readonly Func<long> _now;
    private readonly object _lock = new();

    private bool _active;
    private long _startedAt, _lastTurnAt;
    private long? _lastRefuseLog;   // nullable: "now - long.MinValue" overflows
    private double _cx, _cy, _radius, _angle, _px, _py, _restoreX, _restoreY;
    private GestureKind _kind;

    public DragRotateCore(IGestureIo io, Func<long> nowMs)
    {
        _io = io;
        _now = nowMs;
    }

    public bool Active { get { lock (_lock) return _active; } }

    /// <summary>The ring turned: rotate the canvas by <paramref name="canvasDegrees"/> (clockwise positive).
    /// Starts the gesture if allowed. Returns false (and does nothing) when refused.</summary>
    public bool Turn(double canvasDegrees) => Turn(GestureKind.Rotate, canvasDegrees);

    /// <summary>Build K: the ring turned in a smooth mode of <paramref name="kind"/>. Rotate: canvas degrees
    /// (clockwise positive). Brush size / hardness: drag distance in points (positive = bigger / harder).
    /// A running gesture of another kind is released first. Returns false (and does nothing) when refused.</summary>
    public bool Turn(GestureKind kind, double amount)
    {
        lock (_lock)
        {
            long now = _now();
            if (_active && _kind != kind) ReleaseLocked(null);   // mode changed mid-gesture: let go first
            if (!_active && !TryStart(now, kind)) return false;
            if (!_io.WatchdogFresh()) { ReleaseLocked("OpenTabletArtist's watchdog stopped"); return false; }
            switch (_kind)
            {
                case GestureKind.Rotate:
                    _angle += amount * Math.PI / 180;
                    _px = _cx + _radius * Math.Cos(_angle);
                    _py = _cy + _radius * Math.Sin(_angle);
                    break;
                case GestureKind.BrushSize:
                    _px += amount;
                    break;
                case GestureKind.BrushHardness:
                    _py -= amount;   // screen y grows downwards: harder = drag up
                    break;
            }
            Try(() => _io.MouseDrag(_px, _py));
            _lastTurnAt = now;
            return true;
        }
    }

    /// <summary>Called every ~50 ms: heartbeat while active; release on idle, hard cap or stale watchdog.</summary>
    public void Tick()
    {
        lock (_lock)
        {
            if (!_active) return;
            long now = _now();
            if (now - _startedAt >= HardCapMs) { ReleaseLocked("3 s limit"); return; }
            if (now - _lastTurnAt >= IdleReleaseMs) { ReleaseLocked(null); return; }
            if (!_io.WatchdogFresh()) { ReleaseLocked("OpenTabletArtist's watchdog stopped"); return; }
            Try(() => _io.Heartbeat(true));
        }
    }

    /// <summary>Let go of everything now (idempotent).</summary>
    public void Release(string? why)
    {
        lock (_lock) ReleaseLocked(why);
    }

    private string What => _kind switch
    {
        GestureKind.BrushSize => "Smooth brush size",
        GestureKind.BrushHardness => "Smooth brush hardness",
        _ => "Smooth rotate",
    };

    private bool TryStart(long now, GestureKind kind)
    {
        _kind = kind;
        string? refuse = null;
        (double X, double Y)? cursor = null;
        (double X, double Y, double W, double H)? win = null;
        if (!_io.WatchdogFresh()) refuse = "OpenTabletArtist isn't running (its watchdog is needed)";
        else if (!_io.GateInstalled()) refuse = "the pen gate filter isn't active — re-apply ring modes";
        else if (_io.PenBusy()) refuse = "the pen tip or a pen button is down";
        else if ((cursor = _io.Cursor()) == null) refuse = "no cursor position";
        else if ((win = _io.PhotoshopWindowUnderCursor()) == null) refuse = "the cursor isn't over Photoshop's front window";
        if (refuse != null)
        {
            if (_lastRefuseLog is not { } last || now - last >= RefuseLogEveryMs)
            {
                _lastRefuseLog = now;
                Try(() => _io.Log($"{What} not started: {refuse}."));
            }
            return false;
        }

        var (x, y, w, h) = win!.Value;
        var (curX, curY) = cursor!.Value;
        if (kind != GestureKind.Rotate)
        {
            // Brush HUD: press where the cursor is (Photoshop shows the brush there) and drag in a straight line.
            _px = _restoreX = curX;
            _py = _restoreY = curY;
            _active = true;
            _startedAt = _lastTurnAt = now;
            Try(() => _io.Heartbeat(true));
            Try(() => _io.KeyControlOption(true));
            Try(() => _io.MouseDown(_px, _py));
            Try(() => _io.Log($"{What} started."));
            return true;
        }
        _cx = x + w / 2;
        _cy = y + h / 2;
        double maxR = Math.Max(MinRadius, Math.Min(w, h) / 2 - 20);
        double dx = curX - _cx, dy = curY - _cy, d = Math.Sqrt(dx * dx + dy * dy);
        // Rotate View turns the canvas by the angle swept around its centre: start on a circle of usable
        // radius in the cursor's direction (or to the right of centre if the cursor is near the middle).
        _radius = Math.Clamp(d, MinRadius, maxR);
        _angle = d < 1 ? 0 : Math.Atan2(dy, dx);
        _px = _cx + _radius * Math.Cos(_angle);
        _py = _cy + _radius * Math.Sin(_angle);
        _restoreX = curX;
        _restoreY = curY;

        // Heartbeat first: from here on, a crash leaves "active" behind for the watchdog to clean up.
        _active = true;
        _startedAt = _lastTurnAt = now;
        Try(() => _io.Heartbeat(true));
        if (Math.Abs(_px - curX) > 0.5 || Math.Abs(_py - curY) > 0.5) Try(() => _io.Warp(_px, _py));
        Try(() => _io.KeyR(true));
        Try(() => _io.MouseDown(_px, _py));
        Try(() => _io.Log("Smooth rotate started."));
        return true;
    }

    private void ReleaseLocked(string? why)
    {
        if (!_active) return;
        _active = false;
        Try(() => _io.MouseUp(_px, _py));
        if (_kind == GestureKind.Rotate) Try(() => _io.KeyR(false));
        else Try(() => _io.KeyControlOption(false));
        Try(() => _io.Warp(_restoreX, _restoreY));
        Try(() => _io.Heartbeat(false));
        if (why != null) Try(() => _io.Log($"{What} released: {why}."));
    }

    private static void Try(Action a)
    {
        try { a(); } catch (Exception) { /* each release step must run even if another fails */ }
    }
}
