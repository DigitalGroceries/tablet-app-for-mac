using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin.Tablet.Touch;

namespace OpenTabletArtist.Dynamics;

/// <summary>
/// Discussion #31 (best attempt, untested on real hardware): finger touch as a trackpad for tablets whose
/// touch sensor OTD already parses (e.g. the Intuos Pro PTH-651 via <c>Wacom64bAuxReportParser</c>).
/// One finger moves the cursor by its movement (relative, like a trackpad); two fingers scroll. Touch is
/// ignored while the pen is near the tablet and for a short time after, so a resting hand doesn't move
/// the cursor. Version 23 BETA (Jay's test, 2 Nov): tap to click, tap then touch again and move to drag, two quick
/// taps to double-click, two-finger tap to right-click (<see cref="TouchTapGestures"/>). Passes every report on untouched.
/// </summary>
[PluginName("Tablet App for Mac - Touch trackpad")]
public class TouchTrackpadFilter : IPositionedPipelineElement<IDeviceReport>, IDisposable
{
    private TabletReference? _tablet;
    private bool _touchOn = true, _touchOnSet;

    [TabletReference] public TabletReference? Tablet { get => _tablet; set { _tablet = value; Register(); } }

    /// <summary>Version 28 (#77): the switch on the tablet's page. The filter always runs, so a Touch on/off key
    /// (<see cref="TouchToggleBinding"/>) can turn touch on even when this is off. Profiles from before Version 28
    /// have no such setting and were only written while on, so it defaults to on.</summary>
    [Property("Touch on"), DefaultPropertyValue(true)]
    public bool TouchOn { get => _touchOn; set { _touchOn = value; _touchOnSet = true; Register(); } }

    /// <summary>Devin's review of #77: tell <see cref="TouchSwitch"/> the switch's value as soon as the settings are
    /// applied, so a Touch on/off key pressed before any finger touch turns touch ON on a model that starts off.
    /// Only once both the tablet and an explicit TouchOn are known, so no passing default can clear the key's choice.</summary>
    private void Register()
    {
        if (_touchOnSet && _tablet?.Properties?.Name is { Length: > 0 } name) TouchSwitch.Seen(name, _touchOn);
    }

    public PipelinePosition Position => PipelinePosition.PreTransform;
    public event Action<IDeviceReport>? Emit;

    private Vector2? _last;
    private int _lastCount;
    private DateTime _penSeen = DateTime.MinValue;
    private Vector2 _moveRemainder, _scrollRemainder;
    private readonly TouchTapGestures _taps = new();
    private readonly object _gate = new();
    private System.Threading.Timer? _timer;
    private bool _disposed;

    public void Consume(IDeviceReport value)
    {
        try
        {
            lock (_gate)
            {
                if (!_disposed)
                {
                    if (value is ITabletReport) { _penSeen = DateTime.UtcNow; Do(_taps.Cancel()); }
                    else if (value is ITouchReport touch) Handle(touch);
                }
            }
        }
        catch (Exception) { /* never let touch break the pen */ }
        Emit?.Invoke(value);
    }

    /// <summary>OTD disposes pipeline elements that implement <see cref="IDisposable"/> on disconnect or a
    /// settings reload. Force-release a held click so the button is not left down, then stop the timer.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            try { Do(_taps.Cancel()); }   // LeftUp if a touch drag was held
            catch (Exception) { /* Dispose must not throw */ }
            _timer?.Dispose();
            _timer = null;
        }
    }

    private void Handle(ITouchReport report)
    {
        var name = Tablet?.Properties?.Name ?? "";
        TouchSwitch.Seen(name, TouchOn);
        if (!TouchSwitch.IsOn(name, TouchOn))
        {
            Do(_taps.Cancel());   // switched off mid-drag: let go of the held click
            _last = null; _lastCount = 0;
            return;
        }
        var points = (report.Touches ?? Array.Empty<TouchPoint?>()).Where(p => p != null).Select(p => p!.Position).ToArray();
        var now = DateTime.UtcNow;
        if (now - _penSeen < TouchTrackpadMath.PenGuard)
        {
            Do(_taps.Cancel());
            _last = null; _lastCount = 0;
            return;
        }
        if (points.Length == 0)
        {
            Do(_taps.Release(now));
            _last = null; _lastCount = 0;
            return;
        }
        var centre = points.Aggregate(Vector2.Zero, (a, b) => a + b) / points.Length;
        Do(_taps.Touch(points.Length, centre * MmPerUnit(), now));
        if (_last is { } last && points.Length == _lastCount)
        {
            var mm = (centre - last) * MmPerUnit();
            if (points.Length == 1)
            {
                var px = TouchTrackpadMath.Step(mm * TouchTrackpadMath.PixelsPerMm, ref _moveRemainder);
                if (px != Vector2.Zero) MacPointer.MoveBy(px.X, px.Y, dragging: _taps.Dragging);
            }
            else if (points.Length == 2)
            {
                // Content follows the fingers, like macOS natural scrolling.
                var px = TouchTrackpadMath.Step(mm * TouchTrackpadMath.ScrollPixelsPerMm, ref _scrollRemainder);
                if (px != Vector2.Zero) MacPointer.ScrollPixels((int)px.Y, (int)px.X);
            }
        }
        _last = centre; _lastCount = points.Length;
    }

    /// <summary>Posts the button events and keeps the timer running while a touch or a waiting click needs it.</summary>
    private void Do(List<TouchButtonAction> actions)
    {
        foreach (var a in actions)
        {
            switch (a)
            {
                case TouchButtonAction.LeftDown: MacPointer.Button(MacPointer.LeftDown, 1); break;
                case TouchButtonAction.LeftUp: MacPointer.Button(MacPointer.LeftUp, 1); break;
                case TouchButtonAction.LeftClick:
                    MacPointer.Button(MacPointer.LeftDown, 1); MacPointer.Button(MacPointer.LeftUp, 1); break;
                case TouchButtonAction.LeftSecondClick:
                    MacPointer.Button(MacPointer.LeftDown, 2); MacPointer.Button(MacPointer.LeftUp, 2); break;
                case TouchButtonAction.RightClick:
                    MacPointer.Button(MacPointer.RightDown, 1); MacPointer.Button(MacPointer.RightUp, 1); break;
            }
        }
        if (_taps.Busy && !_disposed)
        {
            _timer ??= new System.Threading.Timer(_ => OnTick(), null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
            _timer.Change(50, 50);
        }
        else _timer?.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
    }

    private void OnTick()
    {
        try { lock (_gate) Do(_taps.Tick(DateTime.UtcNow)); }
        catch (Exception) { /* a timer must never throw */ }
    }

    private Vector2 MmPerUnit()
    {
        var t = Tablet?.Properties?.Specifications?.Touch;
        if (t == null || t.MaxX <= 0 || t.MaxY <= 0) return new Vector2(0.05f, 0.05f);
        return new Vector2(t.Width / t.MaxX, t.Height / t.MaxY);
    }
}

/// <summary>
/// Version 28 (#77): the Touch on/off key's state, per tablet, for the driver's lifetime. A key press flips touch
/// from whatever it is now; a change of the switch on the tablet's page (a new TouchOn) clears the key's override,
/// so the switch always wins when the user changes it. Thread-safe. Keyed by tablet name, so two identical tablets
/// share one switch (rare; Devin's review). A tablet not seen yet counts as on (a PTH-651 from before Version 28).
/// </summary>
public static class TouchSwitch
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, bool> Override = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, bool> Setting = new(StringComparer.Ordinal);

    /// <summary>The filter reports the switch's value; a changed value clears the key's override.</summary>
    public static void Seen(string tablet, bool setting)
    {
        lock (Gate)
        {
            if (Setting.TryGetValue(tablet, out var old) && old != setting) Override.Remove(tablet);
            Setting[tablet] = setting;
        }
    }

    public static bool IsOn(string tablet, bool setting)
    {
        lock (Gate) return Override.TryGetValue(tablet, out var o) ? o : setting;
    }

    /// <summary>True once the touch filter for <paramref name="tablet"/> has reported (it runs).</summary>
    public static bool HasSeen(string tablet)
    {
        lock (Gate) return Setting.ContainsKey(tablet);
    }

    /// <summary>Flips touch for <paramref name="tablet"/> and returns the new state.</summary>
    public static bool Toggle(string tablet)
    {
        lock (Gate)
        {
            var setting = !Setting.TryGetValue(tablet, out var s) || s;
            var now = !(Override.TryGetValue(tablet, out var o) ? o : setting);
            Override[tablet] = now;
            return now;
        }
    }

    /// <summary>Tests only.</summary>
    public static void Reset() { lock (Gate) { Override.Clear(); Setting.Clear(); } }
}

/// <summary>
/// Version 28 (#77, Daniel's CTH-461): a pen button or ExpressKey that turns finger touch on or off with each press,
/// like Wacom's Touch On/Off. Acts on press only.
/// </summary>
[PluginName("Tablet App for Mac - Touch on/off")]
public class TouchToggleBinding : IStateBinding
{
    public void Press(TabletReference tablet, IDeviceReport report)
    {
        var name = tablet?.Properties?.Name ?? "";
        var running = TouchSwitch.HasSeen(name);
        var on = TouchSwitch.Toggle(name);
        // Review before release (no silent failures): the key only works while the touch filter runs, i.e. the Touch
        // trackpad switch is on. Until the filter has shown up (its settings or a first touch), say so instead of
        // claiming touch changed.
        Log.Write("Touch trackpad", running
            ? $"Touch on/off key: finger touch is now {(on ? "ON" : "OFF")} for {name}."
            : $"Touch on/off key pressed for {name} (now {(on ? "ON" : "OFF")}), but the touch filter hasn't reported yet: if touch doesn't change, switch on Touch trackpad on Tablet › More › about.",
            running ? LogLevel.Info : LogLevel.Warning);
    }

    public void Release(TabletReference tablet, IDeviceReport report) { }

    public override string ToString() => "Touch on/off";
}

public static class TouchTrackpadMath
{
    public const float PixelsPerMm = 8f;
    public const float ScrollPixelsPerMm = 6f;
    public static readonly TimeSpan PenGuard = TimeSpan.FromMilliseconds(500);

    /// <summary>Whole pixels to send now; the fraction is kept for the next report so slow moves still move.</summary>
    public static Vector2 Step(Vector2 pixels, ref Vector2 remainder)
    {
        remainder += pixels;
        var whole = new Vector2(MathF.Truncate(remainder.X), MathF.Truncate(remainder.Y));
        remainder -= whole;
        return whole;
    }
}

/// <summary>Moves the macOS cursor by a delta and posts pixel scrolls (CoreGraphics, macOS only).</summary>
internal static class MacPointer
{
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    [StructLayout(LayoutKind.Sequential)] private struct CGPoint { public double X, Y; }

    [DllImport(CoreGraphics)] private static extern IntPtr CGEventCreate(IntPtr source);
    [DllImport(CoreGraphics)] private static extern CGPoint CGEventGetLocation(IntPtr evt);
    [DllImport(CoreGraphics)] private static extern IntPtr CGEventCreateMouseEvent(IntPtr source, uint type, CGPoint pos, uint button);
    [DllImport(CoreGraphics)] private static extern IntPtr CGEventCreateScrollWheelEvent2(IntPtr source, uint units, uint wheelCount, int wheel1, int wheel2, int wheel3);
    [DllImport(CoreGraphics)] private static extern void CGEventPost(uint tap, IntPtr evt);
    [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr cf);

    [DllImport(CoreGraphics)] private static extern void CGEventSetIntegerValueField(IntPtr evt, uint field, long value);

    private const uint MouseMoved = 5;   // kCGEventMouseMoved
    private const uint LeftDragged = 6;  // kCGEventLeftMouseDragged
    public const uint LeftDown = 1, LeftUp = 2, RightDown = 3, RightUp = 4;   // kCGEvent{Left,Right}Mouse{Down,Up}
    private const uint ClickStateField = 1;   // kCGMouseEventClickState
    private const uint HidEventTap = 0;  // kCGHIDEventTap

    /// <summary>A mouse button event at the cursor; <paramref name="clicks"/> 2 makes it the second click of a double-click.</summary>
    public static void Button(uint type, int clicks)
    {
        if (!OperatingSystem.IsMacOS()) return;
        var probe = CGEventCreate(IntPtr.Zero);
        if (probe == IntPtr.Zero) return;
        CGPoint at;
        try { at = CGEventGetLocation(probe); } finally { CFRelease(probe); }
        var button = type is RightDown or RightUp ? 1u : 0u;
        var e = CGEventCreateMouseEvent(IntPtr.Zero, type, at, button);
        if (e == IntPtr.Zero) return;
        try { CGEventSetIntegerValueField(e, ClickStateField, clicks); CGEventPost(HidEventTap, e); }
        finally { CFRelease(e); }
    }

    public static void MoveBy(float dx, float dy, bool dragging = false)
    {
        if (!OperatingSystem.IsMacOS()) return;
        var probe = CGEventCreate(IntPtr.Zero);
        if (probe == IntPtr.Zero) return;
        CGPoint at;
        try { at = CGEventGetLocation(probe); } finally { CFRelease(probe); }
        at.X += dx; at.Y += dy;
        // While the button is held (a touch drag) the move must be a drag, or apps see a move with no button.
        var e = CGEventCreateMouseEvent(IntPtr.Zero, dragging ? LeftDragged : MouseMoved, at, 0);
        if (e == IntPtr.Zero) return;
        try { CGEventPost(HidEventTap, e); } finally { CFRelease(e); }
    }

    public static void ScrollPixels(int vertical, int horizontal)
    {
        if (!OperatingSystem.IsMacOS() || (vertical == 0 && horizontal == 0)) return;
        var e = CGEventCreateScrollWheelEvent2(IntPtr.Zero, 0, 2, vertical, horizontal, 0);
        if (e == IntPtr.Zero) return;
        try { CGEventPost(HidEventTap, e); } finally { CFRelease(e); }
    }
}
