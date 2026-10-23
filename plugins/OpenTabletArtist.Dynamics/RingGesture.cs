using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin.Tablet.Wheel;

namespace OpenTabletArtist.Dynamics;

/// <summary>Build J: files shared with the OTA watchdog (per-user temp folder, inherited from OTA).</summary>
public static class RingGestureFiles
{
    public static string Dir { get; set; } = Path.Combine(Path.GetTempPath(), "OpenTabletArtist");
    /// <summary>Plugin → OTA: "active|idle &lt;daemon pid&gt; &lt;unix ms&gt;".</summary>
    public static string HeartbeatPath => Path.Combine(Dir, "ring-gesture");
    /// <summary>OTA → plugin: "&lt;unix ms&gt;", rewritten every 250 ms while OTA runs.</summary>
    public static string WatchdogPath => Path.Combine(Dir, "ota-watchdog");
    public const int WatchdogFreshMs = 2000;

    public static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public static void WriteAtomically(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + "." + Environment.ProcessId + ".tmp";
        File.WriteAllText(tmp, content);
        File.Move(tmp, path, overwrite: true);
    }
}

/// <summary>
/// Build J: holds back pen reports while a smooth-rotate gesture is running, so the pen can't move the
/// cursor (or click) during the synthetic drag. Only pen position reports are dropped — ring, aux and
/// wheel-button reports always pass, since the ring's own bindings run after the filters. Added to the
/// profile by OpenTabletArtist when a mode uses smooth rotate.
/// </summary>
[PluginName("OpenTabletArtist - Ring Gesture Gate")]
public class RingGestureGate : IPositionedPipelineElement<IDeviceReport>
{
    private static long _constructedAt;
    private static volatile bool _penBusy;

    public RingGestureGate() => Interlocked.Exchange(ref _constructedAt, RingGestureFiles.NowMs());

    public static bool Installed => Interlocked.Read(ref _constructedAt) != 0;
    public static bool PenBusy => _penBusy;

    public PipelinePosition Position => PipelinePosition.PreTransform;
    public event Action<IDeviceReport>? Emit;

    /// <summary>True for reports that only carry the pen (safe to drop); never for ring/aux/wheel reports.</summary>
    public static bool IsPenOnly(IDeviceReport report) =>
        report is ITabletReport && report is not IAuxReport && report is not IAbsoluteWheelReport
        && report is not IWheelButtonReport;

    public void Consume(IDeviceReport value)
    {
        if (value is ITabletReport t)
            _penBusy = t.Pressure > 0 || (t.PenButtons?.Any(b => b) ?? false);
        if (DragRotate.IsActive && IsPenOnly(value)) return;   // held back during the gesture
        Emit?.Invoke(value);
    }
}

/// <summary>The daemon-wide drag-rotate gesture: one <see cref="DragRotateCore"/>, ticked by a background
/// thread, released on daemon exit.</summary>
public static class DragRotate
{
    private static readonly object Init = new();
    private static DragRotateCore? _core;

    public static bool IsActive => _core?.Active ?? false;

    public static bool Turn(double canvasDegrees) => Turn(GestureKind.Rotate, canvasDegrees);

    /// <summary>Build R: returns whether the gesture acted (false = refused, e.g. Photoshop isn't under the cursor).</summary>
    public static bool Turn(GestureKind kind, double amount)
    {
        if (!OperatingSystem.IsMacOS()) return false;
        try { return Core().Turn(kind, amount); } catch (Exception) { return false; }
    }

    /// <summary>Build R: true only when Photoshop's front window is under the cursor (same test the gesture uses).</summary>
    public static bool PhotoshopInFront()
    {
        if (!OperatingSystem.IsMacOS()) return false;
        try { return new MacGestureIo().PhotoshopWindowUnderCursor() != null; } catch (Exception) { return false; }
    }

    private static DragRotateCore Core()
    {
        lock (Init)
        {
            if (_core != null) return _core;
            var core = new DragRotateCore(new MacGestureIo(), RingGestureFiles.NowMs);
            var t = new Thread(() =>
            {
                while (true)
                {
                    Thread.Sleep(50);
                    try { core.Tick(); } catch (Exception) { /* keep ticking */ }
                }
            }) { IsBackground = true, Name = "OTA ring gesture" };
            t.Start();
            AppDomain.CurrentDomain.ProcessExit += (_, _) => core.Release("driver exiting");
            AppDomain.CurrentDomain.UnhandledException += (_, _) => core.Release("driver error");
            return _core = core;
        }
    }
}

/// <summary>macOS side of the gesture: CoreGraphics events and window lookup, NSRunningApplication for the
/// bundle id. Any failure → the gesture is refused (or the step is skipped), never forced.</summary>
internal sealed class MacGestureIo : IGestureIo
{
    private const string CG = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CF = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private const string PhotoshopBundleId = "com.adobe.Photoshop";
    private const ushort KeyCodeR = 15;           // kVK_ANSI_R
    private const ushort KeyCodeControl = 59, KeyCodeOption = 58;   // kVK_Control, kVK_Option
    private const ulong FlagControl = 0x40000, FlagOption = 0x80000; // kCGEventFlagMaskControl / Alternate
    private volatile bool _controlOption;   // build K: brush gesture holds Control + Option
    private const uint HidTap = 0;

    [StructLayout(LayoutKind.Sequential)] private struct CGPoint { public double X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct CGRect { public double X, Y, W, H; }

    [DllImport(CG)] private static extern IntPtr CGEventCreate(IntPtr src);
    [DllImport(CG)] private static extern IntPtr CGEventSourceCreate(int stateId);
    // Build L: one HID-system-state source for every synthetic event, so the modifier keys we post are part
    // of the system's modifier state that Photoshop reads (not only flags on individual events).
    private static readonly Lazy<IntPtr> Src = new(() => { try { return CGEventSourceCreate(1); } catch { return IntPtr.Zero; } });
    private const int ModifierSettleMs = 30, PressSettleMs = 30;
    [DllImport(CG)] private static extern CGPoint CGEventGetLocation(IntPtr e);
    [DllImport(CG)] private static extern IntPtr CGEventCreateKeyboardEvent(IntPtr src, ushort key, [MarshalAs(UnmanagedType.I1)] bool down);
    [DllImport(CG)] private static extern IntPtr CGEventCreateMouseEvent(IntPtr src, uint type, CGPoint pos, uint button);
    [DllImport(CG)] private static extern void CGEventSetFlags(IntPtr e, ulong flags);
    [DllImport(CG)] private static extern void CGEventPost(uint tap, IntPtr e);
    [DllImport(CG)] private static extern int CGWarpMouseCursorPosition(CGPoint p);
    [DllImport(CG)] private static extern IntPtr CGWindowListCopyWindowInfo(uint option, uint relativeTo);
    [DllImport(CG)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CGRectMakeWithDictionaryRepresentation(IntPtr d, out CGRect r);
    [DllImport(CF)] private static extern void CFRelease(IntPtr cf);
    [DllImport(CF)] private static extern nint CFArrayGetCount(IntPtr a);
    [DllImport(CF)] private static extern IntPtr CFArrayGetValueAtIndex(IntPtr a, nint i);
    [DllImport(CF)] private static extern IntPtr CFDictionaryGetValue(IntPtr d, IntPtr k);
    [DllImport(CF)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CFNumberGetValue(IntPtr n, int type, out int v);
    [DllImport(CF)] private static extern IntPtr CFStringCreateWithCString(IntPtr alloc, string s, uint enc);
    [DllImport(ObjC)] private static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr o, IntPtr sel);
    [DllImport(ObjC)] private static extern IntPtr object_getClass(IntPtr o);
    [DllImport(ObjC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool class_respondsToSelector(IntPtr cls, IntPtr sel);
    /// <summary>Build Q: true when the object (or class — its metaclass holds class methods) responds to the selector.</summary>
    internal static bool RespondsTo(IntPtr o, string sel)
    {
        if (o == IntPtr.Zero) return false;
        var c = object_getClass(o);
        return c != IntPtr.Zero && class_respondsToSelector(c, sel_registerName(sel));
    }
    private static string[] Selectors => RingGestureSelectors.All;
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendInt(IntPtr o, IntPtr sel, int arg);

    private static bool _appKitLoaded;

    public (double X, double Y)? Cursor()
    {
        var e = CGEventCreate(IntPtr.Zero);
        if (e == IntPtr.Zero) return null;
        try { var p = CGEventGetLocation(e); return (p.X, p.Y); }
        finally { CFRelease(e); }
    }

    public (double X, double Y, double W, double H)? PhotoshopWindowUnderCursor()
    {
        if (Cursor() is not { } c) return null;
        IntPtr list = IntPtr.Zero, kLayer = IntPtr.Zero, kBounds = IntPtr.Zero, kPid = IntPtr.Zero;
        try
        {
            list = CGWindowListCopyWindowInfo(1 | 16, 0);   // on-screen only, no desktop elements
            if (list == IntPtr.Zero) return null;
            kLayer = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowLayer", 0x08000100);
            kBounds = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowBounds", 0x08000100);
            kPid = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowOwnerPID", 0x08000100);
            nint n = CFArrayGetCount(list);
            for (nint i = 0; i < n; i++)
            {
                var d = CFArrayGetValueAtIndex(list, i);
                var layerRef = CFDictionaryGetValue(d, kLayer);
                if (layerRef == IntPtr.Zero || !CFNumberGetValue(layerRef, 3, out var layer) || layer != 0) continue;
                // The frontmost normal window decides: it must be Photoshop's and contain the cursor.
                var pidRef = CFDictionaryGetValue(d, kPid);
                var b = CFDictionaryGetValue(d, kBounds);
                if (pidRef == IntPtr.Zero || !CFNumberGetValue(pidRef, 3, out var pid)) return null;
                if (b == IntPtr.Zero || !CGRectMakeWithDictionaryRepresentation(b, out var r)) return null;
                if (BundleId(pid) != PhotoshopBundleId) return null;
                if (c.X < r.X || c.Y < r.Y || c.X > r.X + r.W || c.Y > r.Y + r.H) return null;
                if (r.W < 200 || r.H < 200) return null;
                return (r.X, r.Y, r.W, r.H);
            }
            return null;
        }
        finally
        {
            foreach (var cf in new[] { list, kLayer, kBounds, kPid })
                if (cf != IntPtr.Zero) CFRelease(cf);
        }
    }

    private static string? BundleId(int pid)
    {
        if (!_appKitLoaded)
        {
            NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");
            _appKitLoaded = true;
        }
        var cls = objc_getClass("NSRunningApplication");
        if (cls == IntPtr.Zero || !RespondsTo(cls, Selectors[0])) return null;
        var app = SendInt(cls, sel_registerName(Selectors[0]), pid);
        if (app == IntPtr.Zero || !RespondsTo(app, Selectors[1])) return null;
        var str = Send(app, sel_registerName(Selectors[1]));
        if (str == IntPtr.Zero || !RespondsTo(str, Selectors[2])) return null;
        var utf8 = Send(str, sel_registerName(Selectors[2]));
        return utf8 == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(utf8);
    }

    public bool WatchdogFresh()
    {
        try
        {
            var text = File.ReadAllText(RingGestureFiles.WatchdogPath).Trim();
            return long.TryParse(text, out var t) && RingGestureFiles.NowMs() - t < RingGestureFiles.WatchdogFreshMs;
        }
        catch (Exception) { return false; }
    }

    public bool GateInstalled() => RingGestureGate.Installed;
    public bool PenBusy() => RingGestureGate.PenBusy;

    public void KeyR(bool down) => Post(CGEventCreateKeyboardEvent(IntPtr.Zero, KeyCodeR, down));

    public void KeyControlOption(bool down)
    {
        if (down)
        {
            _controlOption = true;
            Post(CGEventCreateKeyboardEvent(Src.Value, KeyCodeControl, true), FlagControl);
            Post(CGEventCreateKeyboardEvent(Src.Value, KeyCodeOption, true), FlagControl | FlagOption);
            Thread.Sleep(ModifierSettleMs);   // let Photoshop see Control+Option before the press
        }
        else
        {
            _controlOption = false;   // later mouse events carry no modifiers, even if a key-up below fails
            // Released last (after mouse-up), Option first, each key-up still carrying the other's flag.
            try { Post(CGEventCreateKeyboardEvent(Src.Value, KeyCodeOption, false), FlagControl); }
            finally { Post(CGEventCreateKeyboardEvent(Src.Value, KeyCodeControl, false), 0); }
        }
    }
    private ulong MouseFlags => _controlOption ? FlagControl | FlagOption : 0;
    public void MouseDown(double x, double y)
    {
        Post(Mouse(1, x, y), MouseFlags);   // kCGEventLeftMouseDown
        if (_controlOption) Thread.Sleep(PressSettleMs);   // brush HUD: let the press register before dragging
    }
    public void MouseDrag(double x, double y) => Post(Mouse(6, x, y), MouseFlags);   // kCGEventLeftMouseDragged
    public void MouseUp(double x, double y) => Post(Mouse(2, x, y), MouseFlags);     // kCGEventLeftMouseUp
    public void Warp(double x, double y) => CGWarpMouseCursorPosition(new CGPoint { X = x, Y = y });

    // Rotate keeps J's working path (no source); the brush gesture uses the HID-state source.
    private IntPtr Mouse(uint type, double x, double y) =>
        CGEventCreateMouseEvent(_controlOption ? Src.Value : IntPtr.Zero, type, new CGPoint { X = x, Y = y }, 0);

    private static void Post(IntPtr e, ulong flags = 0)
    {
        if (e == IntPtr.Zero) return;
        try
        {
            CGEventSetFlags(e, flags);   // only the modifiers we mean: none for R / rotate, Control+Option for brush
            CGEventPost(HidTap, e);
        }
        finally { CFRelease(e); }
    }

    public void Heartbeat(bool active) =>
        RingGestureFiles.WriteAtomically(RingGestureFiles.HeartbeatPath,
            $"{(active ? "active" : "idle")} {Environment.ProcessId} {RingGestureFiles.NowMs()}");

    public void Log(string message) => OpenTabletDriver.Plugin.Log.Write("OpenTabletArtist", message);
}
