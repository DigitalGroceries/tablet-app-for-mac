using System;
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
/// the cursor. No tap-to-click yet. Passes every report on untouched.
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

    public void Consume(IDeviceReport value)
    {
        try
        {
            if (value is ITabletReport) _penSeen = DateTime.UtcNow;
            else if (value is ITouchReport touch) Handle(touch);
        }
        catch (Exception) { /* never let touch break the pen */ }
        Emit?.Invoke(value);
    }

    private void Handle(ITouchReport report)
    {
        var points = (report.Touches ?? Array.Empty<TouchPoint?>()).Where(p => p != null).Select(p => p!.Position).ToArray();
        if (points.Length == 0 || DateTime.UtcNow - _penSeen < TouchTrackpadMath.PenGuard)
        {
            _last = null; _lastCount = 0;
            return;
        }
        var centre = points.Aggregate(Vector2.Zero, (a, b) => a + b) / points.Length;
        if (_last is { } last && points.Length == _lastCount)
        {
            var mm = (centre - last) * MmPerUnit();
            if (points.Length == 1)
            {
                var px = TouchTrackpadMath.Step(mm * TouchTrackpadMath.PixelsPerMm, ref _moveRemainder);
                if (px != Vector2.Zero) MacPointer.MoveBy(px.X, px.Y);
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

    private const uint MouseMoved = 5;   // kCGEventMouseMoved
    private const uint HidEventTap = 0;  // kCGHIDEventTap

    public static void MoveBy(float dx, float dy)
    {
        if (!OperatingSystem.IsMacOS()) return;
        var probe = CGEventCreate(IntPtr.Zero);
        if (probe == IntPtr.Zero) return;
        CGPoint at;
        try { at = CGEventGetLocation(probe); } finally { CFRelease(probe); }
        at.X += dx; at.Y += dy;
        var e = CGEventCreateMouseEvent(IntPtr.Zero, MouseMoved, at, 0);
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
