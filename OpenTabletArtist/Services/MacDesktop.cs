using System;
using System.Runtime.InteropServices;
using Avalonia;

namespace OpenTabletArtist.Services;

/// <summary>
/// Small macOS-only helpers for the ring-mode popup (build H): where the cursor is, the frontmost app
/// window's frame, and making a window a click-through overlay. Every call returns null / does nothing off
/// macOS or on any failure. Coordinates are global points, top-left origin of the main display — the same
/// space Avalonia uses for screens and window positions on macOS.
/// </summary>
internal static class MacDesktop
{
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    [StructLayout(LayoutKind.Sequential)] private struct CGPoint { public double X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct CGRect { public double X, Y, W, H; }

    [DllImport(CoreGraphics)] private static extern IntPtr CGEventCreate(IntPtr source);
    [DllImport(CoreGraphics)] private static extern CGPoint CGEventGetLocation(IntPtr evt);
    [DllImport(CoreGraphics)] private static extern IntPtr CGWindowListCopyWindowInfo(uint option, uint relativeToWindow);
    [DllImport(CoreGraphics)] [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CGRectMakeWithDictionaryRepresentation(IntPtr dict, out CGRect rect);
    [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr cf);
    [DllImport(CoreFoundation)] private static extern nint CFArrayGetCount(IntPtr array);
    [DllImport(CoreFoundation)] private static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, nint index);
    [DllImport(CoreFoundation)] private static extern IntPtr CFDictionaryGetValue(IntPtr dict, IntPtr key);
    [DllImport(CoreFoundation)] [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CFNumberGetValue(IntPtr number, int type, out int value);
    [DllImport(CoreFoundation)] private static extern IntPtr CFStringCreateWithCString(IntPtr alloc, string s, uint encoding);
    [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendBool(IntPtr obj, IntPtr sel, [MarshalAs(UnmanagedType.I1)] bool value);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendNUInt(IntPtr obj, IntPtr sel, nuint value);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendPtr(IntPtr obj, IntPtr sel);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendPtrArg(IntPtr obj, IntPtr sel, IntPtr arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendPtrInt(IntPtr obj, IntPtr sel, int value);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern int SendIntRet(IntPtr obj, IntPtr sel);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)] private static extern bool SendBoolRet(IntPtr obj, IntPtr sel);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)] private static extern bool SendActivate(IntPtr obj, IntPtr sel, nuint options);
    [DllImport(CoreGraphics)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CGEventSourceKeyState(int stateId, ushort key);

    [DllImport(CoreGraphics)] private static extern IntPtr CGEventCreateKeyboardEvent(IntPtr src, ushort key, [MarshalAs(UnmanagedType.I1)] bool down);
    [DllImport(CoreGraphics)] private static extern IntPtr CGEventCreateMouseEvent(IntPtr src, uint type, CGPoint pos, uint button);
    [DllImport(CoreGraphics)] private static extern void CGEventSetFlags(IntPtr e, ulong flags);
    [DllImport(CoreGraphics)] private static extern void CGEventPost(uint tap, IntPtr e);

    /// <summary>Build J watchdog: post R key-up and left mouse-up at the cursor, undoing whatever a crashed
    /// smooth-rotate gesture left pressed. Harmless if nothing was pressed.</summary>
    public static void ReleaseRotateInput()
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            var at = CursorLocation() ?? new Point(0, 0);
            PostAndRelease(CGEventCreateMouseEvent(IntPtr.Zero, 2 /* LeftMouseUp */, new CGPoint { X = at.X, Y = at.Y }, 0));
            PostAndRelease(CGEventCreateKeyboardEvent(IntPtr.Zero, 15 /* kVK_ANSI_R */, false));
            // Build K: the brush gestures hold Control + Option, and smooth zoom taps Option.
            PostAndRelease(CGEventCreateKeyboardEvent(IntPtr.Zero, 58 /* kVK_Option */, false));
            PostAndRelease(CGEventCreateKeyboardEvent(IntPtr.Zero, 59 /* kVK_Control */, false));
        }
        catch (Exception e) { AppLog.Warn("Couldn't release the ring gesture's input", e); }
    }

    private static void PostAndRelease(IntPtr e)
    {
        if (e == IntPtr.Zero) return;
        try { CGEventSetFlags(e, 0); CGEventPost(0, e); }
        finally { CFRelease(e); }
    }

    private const uint OnScreenOnly = 1, ExcludeDesktopElements = 16;
    private const uint Utf8 = 0x08000100;
    private const int CFNumberSInt32Type = 3;

    /// <summary>The mouse cursor (the pen moves it), or null.</summary>
    public static Point? CursorLocation()
    {
        if (!OperatingSystem.IsMacOS()) return null;
        try
        {
            var e = CGEventCreate(IntPtr.Zero);
            if (e == IntPtr.Zero) return null;
            try { var p = CGEventGetLocation(e); return new Point(p.X, p.Y); }
            finally { CFRelease(e); }
        }
        catch (Exception) { return null; }
    }

    /// <summary>The frame of the frontmost normal window of another app (front-to-back order, layer 0, not
    /// this process), or null. Window bounds need no Screen Recording permission (titles would).</summary>
    public static Rect? FrontmostWindowBounds()
    {
        if (!OperatingSystem.IsMacOS()) return null;
        IntPtr list = IntPtr.Zero, kLayer = IntPtr.Zero, kBounds = IntPtr.Zero, kPid = IntPtr.Zero;
        try
        {
            list = CGWindowListCopyWindowInfo(OnScreenOnly | ExcludeDesktopElements, 0);
            if (list == IntPtr.Zero) return null;
            kLayer = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowLayer", Utf8);
            kBounds = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowBounds", Utf8);
            kPid = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowOwnerPID", Utf8);
            int me = Environment.ProcessId;
            nint n = CFArrayGetCount(list);
            for (nint i = 0; i < n; i++)
            {
                var d = CFArrayGetValueAtIndex(list, i);
                if (!TryInt(d, kLayer, out var layer) || layer != 0) continue;
                if (TryInt(d, kPid, out var pid) && pid == me) continue;
                var b = CFDictionaryGetValue(d, kBounds);
                if (b == IntPtr.Zero || !CGRectMakeWithDictionaryRepresentation(b, out var r)) continue;
                if (r.W < 50 || r.H < 50) continue;
                return new Rect(r.X, r.Y, r.W, r.H);
            }
            return null;
        }
        catch (Exception) { return null; }
        finally
        {
            foreach (var cf in new[] { list, kLayer, kBounds, kPid })
                if (cf != IntPtr.Zero) CFRelease(cf);
        }
    }

    private static bool TryInt(IntPtr dict, IntPtr key, out int value)
    {
        value = 0;
        var v = CFDictionaryGetValue(dict, key);
        return v != IntPtr.Zero && CFNumberGetValue(v, CFNumberSInt32Type, out value);
    }

    /// <summary>Click-through (ignoresMouseEvents), on every Space and over full-screen apps, and left out of
    /// the window cycle. Never makes the window key or activates the app.</summary>
    public static void MakeOverlay(IntPtr nsWindow) => MakeOverlay(nsWindow, clickThrough: true);

    /// <summary>The radial menu's window (TASKS #54): takes the Mac mouse (<paramref name="clickThrough"/> false) but
    /// asks macOS not to activate this app when it is clicked, so the shortcut a slice sends still goes to the app
    /// that was in front. Everything else as <see cref="MakeOverlay(IntPtr)"/>.</summary>
    public static void MakeOverlay(IntPtr nsWindow, bool clickThrough)
    {
        if (!OperatingSystem.IsMacOS() || nsWindow == IntPtr.Zero) return;
        // Build Q: only send what the window answers to.
        if (ObjCGuard.RespondsTo(nsWindow, ObjCSelectors.SetIgnoresMouseEvents))
            SendBool(nsWindow, sel_registerName(ObjCSelectors.SetIgnoresMouseEvents), clickThrough);
        if (!clickThrough && ObjCGuard.RespondsTo(nsWindow, ObjCSelectors.SetPreventsActivation))
            SendBool(nsWindow, sel_registerName(ObjCSelectors.SetPreventsActivation), true);
        // NSWindowCollectionBehaviorCanJoinAllSpaces (1) | Stationary (16) | IgnoresCycle (64) | FullScreenAuxiliary (256)
        if (ObjCGuard.RespondsTo(nsWindow, ObjCSelectors.SetCollectionBehavior))
            SendNUInt(nsWindow, sel_registerName(ObjCSelectors.SetCollectionBehavior), (nuint)(1 | 16 | 64 | 256));
        // v27 (Zak): the window itself must be non-opaque with a clear backing, or turning the menu see-through
        // only shows a dark window background instead of the user's work.
        if (ObjCGuard.RespondsTo(nsWindow, ObjCSelectors.SetOpaque))
            SendBool(nsWindow, sel_registerName(ObjCSelectors.SetOpaque), false);
        var nsColor = ObjCGuard.Class("NSColor");
        if (nsColor != IntPtr.Zero && ObjCGuard.RespondsTo(nsColor, ObjCSelectors.ClearColor)
            && ObjCGuard.RespondsTo(nsWindow, ObjCSelectors.SetBackgroundColor))
        {
            var clear = SendPtr(nsColor, sel_registerName(ObjCSelectors.ClearColor));
            if (clear != IntPtr.Zero)
                SendPtrArg(nsWindow, sel_registerName(ObjCSelectors.SetBackgroundColor), clear);
        }
    }

    private static int _frontPid;

    /// <summary>Remember which app is in front now (not this one), so <see cref="GiveFocusBack"/> can return to it.</summary>
    public static void RememberFrontmost()
    {
        _frontPid = 0;
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            var ws = SendPtr(ObjCGuard.Class("NSWorkspace"), sel_registerName(ObjCSelectors.SharedWorkspace));
            if (ws == IntPtr.Zero || !ObjCGuard.RespondsTo(ws, ObjCSelectors.FrontmostApplication)) return;
            var app = SendPtr(ws, sel_registerName(ObjCSelectors.FrontmostApplication));
            if (app == IntPtr.Zero || !ObjCGuard.RespondsTo(app, ObjCSelectors.ProcessIdentifier)) return;
            int pid = SendIntRet(app, sel_registerName(ObjCSelectors.ProcessIdentifier));
            if (pid > 0 && pid != Environment.ProcessId) _frontPid = pid;
        }
        catch (Exception) { _frontPid = 0; }
    }

    /// <summary>Safety net for a click on the radial menu: if macOS made this app active anyway, hand the keyboard back
    /// to the app that was in front. True when it had to. Does nothing when this app isn't active.</summary>
    public static bool GiveFocusBack()
    {
        if (!OperatingSystem.IsMacOS() || _frontPid == 0) return false;
        try
        {
            var me = SendPtr(ObjCGuard.Class("NSApplication"), sel_registerName(ObjCSelectors.SharedApplication));
            if (me == IntPtr.Zero || !ObjCGuard.RespondsTo(me, ObjCSelectors.IsActive)
                || !SendBoolRet(me, sel_registerName(ObjCSelectors.IsActive))) return false;
            var cls = ObjCGuard.Class("NSRunningApplication");
            if (cls == IntPtr.Zero || !ObjCGuard.RespondsTo(cls, ObjCSelectors.RunningApplicationWithPid)) return false;
            var target = SendPtrInt(cls, sel_registerName(ObjCSelectors.RunningApplicationWithPid), _frontPid);
            if (target == IntPtr.Zero || !ObjCGuard.RespondsTo(target, ObjCSelectors.ActivateWithOptions)) return false;
            SendActivate(target, sel_registerName(ObjCSelectors.ActivateWithOptions), 0);
            return true;
        }
        catch (Exception) { return false; }
    }

    /// <summary>Is the Esc key down right now (the hardware or any app's synthetic event)? Reading the key state needs
    /// no permission and doesn't need this app to be in front. False off macOS.</summary>
    public static bool IsEscapeDown()
    {
        if (!OperatingSystem.IsMacOS()) return false;
        try { return CGEventSourceKeyState(0 /* combined session state */, 0x35 /* kVK_Escape */); }
        catch (Exception) { return false; }
    }
}
