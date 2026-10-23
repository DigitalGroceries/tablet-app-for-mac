using System;
using System.Runtime.InteropServices;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.DependencyInjection;
using OpenTabletDriver.Plugin.Platform.Pointer;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletArtist.Dynamics;

/// <summary>
/// Version 1: what a disabled pen tip or eraser is bound to. It clicks only when the window under the
/// pointer belongs to Tablet App for Mac, so the user can always reach the switch or the Fix button to turn
/// the end back on. Over every other app it does nothing, exactly like no binding.
/// </summary>
[PluginName("Tablet App for Mac - Click in this app only")]
public class OwnWindowClickBinding : IStateBinding
{
    [Resolved] public IMouseButtonHandler? Pointer { set; get; }

    [Property("Button")] public string? Button { set; get; } = "Left";

    private MouseButton? _held;

    public void Press(TabletReference tablet, IDeviceReport report)
    {
        if (_held != null || !OwnWindow.IsUnderPointer()) return;
        if (!Enum.TryParse<MouseButton>(Button ?? "Left", true, out var b)) b = MouseButton.Left;
        Pointer?.MouseDown(b);
        _held = b;
    }

    public void Release(TabletReference tablet, IDeviceReport report)
    {
        if (_held is not { } b) return;
        _held = null;
        Pointer?.MouseUp(b);
    }

    public override string ToString() => "Click in this app only";
}

/// <summary>Is the frontmost on-screen window under the pointer one of Tablet App for Mac's? macOS only; false
/// elsewhere or on any failure (so a disabled end stays disabled).</summary>
public static class OwnWindow
{
    /// <summary>Process names the app's windows carry (kCGWindowOwnerName).</summary>
    public static readonly string[] OwnerNames = { "Tablet App for Mac", "OpenTabletArtist" };

    private const string CG = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CF = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    [StructLayout(LayoutKind.Sequential)] private struct CGPoint { public double X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct CGRect { public double X, Y, W, H; }
    [DllImport(CG)] private static extern IntPtr CGEventCreate(IntPtr source);
    [DllImport(CG)] private static extern CGPoint CGEventGetLocation(IntPtr evt);
    [DllImport(CG)] private static extern IntPtr CGWindowListCopyWindowInfo(uint option, uint relativeToWindow);
    [DllImport(CG)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CGRectMakeWithDictionaryRepresentation(IntPtr dict, out CGRect rect);
    [DllImport(CF)] private static extern long CFArrayGetCount(IntPtr arr);
    [DllImport(CF)] private static extern IntPtr CFArrayGetValueAtIndex(IntPtr arr, long i);
    [DllImport(CF)] private static extern IntPtr CFDictionaryGetValue(IntPtr dict, IntPtr key);
    [DllImport(CF)] private static extern IntPtr CFStringCreateWithCString(IntPtr alloc, string s, uint encoding);
    [DllImport(CF)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CFStringGetCString(IntPtr s, byte[] buf, long size, uint encoding);
    [DllImport(CF)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CFNumberGetValue(IntPtr n, long type, out double value);
    [DllImport(CF)] private static extern void CFRelease(IntPtr cf);
    private const uint Utf8 = 0x08000100;
    private const long Float64Type = 6;   // kCFNumberFloat64Type
    private static IntPtr _kBounds, _kOwner, _kAlpha;

    public static bool IsUnderPointer()
    {
        if (!OperatingSystem.IsMacOS()) return false;
        try
        {
            if (_kBounds == IntPtr.Zero)
            {
                _kBounds = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowBounds", Utf8);
                _kOwner = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowOwnerName", Utf8);
                _kAlpha = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowAlpha", Utf8);
            }
            var ev = CGEventCreate(IntPtr.Zero);
            if (ev == IntPtr.Zero) return false;
            var p = CGEventGetLocation(ev);
            CFRelease(ev);
            // On-screen windows, front to back, without the desktop.
            var list = CGWindowListCopyWindowInfo(1 | 16, 0);
            if (list == IntPtr.Zero) return false;
            try
            {
                var buf = new byte[256];
                for (long i = 0, n = CFArrayGetCount(list); i < n; i++)
                {
                    var w = CFArrayGetValueAtIndex(list, i);
                    var a = CFDictionaryGetValue(w, _kAlpha);
                    if (a != IntPtr.Zero && CFNumberGetValue(a, Float64Type, out var alpha) && alpha < 0.01) continue;
                    var b = CFDictionaryGetValue(w, _kBounds);
                    if (b == IntPtr.Zero || !CGRectMakeWithDictionaryRepresentation(b, out var r)) continue;
                    if (p.X < r.X || p.Y < r.Y || p.X >= r.X + r.W || p.Y >= r.Y + r.H) continue;
                    var o = CFDictionaryGetValue(w, _kOwner);
                    if (o == IntPtr.Zero || !CFStringGetCString(o, buf, buf.Length, Utf8)) return false;
                    var name = System.Text.Encoding.UTF8.GetString(buf, 0, Array.IndexOf(buf, (byte)0) is var z and >= 0 ? z : buf.Length);
                    return Array.IndexOf(OwnerNames, name) >= 0;
                }
                return false;
            }
            finally { CFRelease(list); }
        }
        catch { return false; }
    }
}
