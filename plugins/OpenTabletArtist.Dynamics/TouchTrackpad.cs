using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
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
public class TouchTrackpadFilter : IPositionedPipelineElement<IDeviceReport>
{
    [TabletReference] public TabletReference? Tablet { get; set; }

    public PipelinePosition Position => PipelinePosition.PreTransform;
    public event Action<IDeviceReport>? Emit;

    private Vector2? _last;
    private int _lastCount;
    private DateTime _penSeen = DateTime.MinValue;
    private Vector2 _moveRemainder, _scrollRemainder;
    private readonly TouchTapGestures _taps = new();
    private readonly object _gate = new();
    private System.Threading.Timer? _timer;

    public void Consume(IDeviceReport value)
    {
        try
        {
            lock (_gate)
            {
                if (value is ITabletReport) { _penSeen = DateTime.UtcNow; Do(_taps.Cancel()); }
                else if (value is ITouchReport touch) Handle(touch);
            }
        }
        catch (Exception) { /* never let touch break the pen */ }
        Emit?.Invoke(value);
    }

    private void Handle(ITouchReport report)
    {
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
        if (_taps.Busy)
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
