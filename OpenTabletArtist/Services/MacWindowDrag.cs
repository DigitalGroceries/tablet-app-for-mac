using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;

namespace OpenTabletArtist.Services;

/// <summary>
/// V29 #176 (Zak 20 Nov, build 9: the app's window got stuck at the edge between his screens while a Finder window crossed; the
/// report showed the Mac's own drag not starting in all 17 drags). Avalonia asks macOS for the drag with the last mouse press it
/// recorded; Grok's review: when that isn't the press being handled right now, the drag doesn't start or starts late. Here the
/// press macOS is handling at this moment is used, and only when it really is this window's left press at the same time as the
/// one the app saw. The real window frame is read too: Avalonia's Position only follows the Mac's moves later.
/// </summary>
internal static class MacWindowDrag
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private const nuint LeftMouseDown = 1;   // NSEventTypeLeftMouseDown

    [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr obj, IntPtr sel);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nuint SendNUInt(IntPtr obj, IntPtr sel);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern double SendDouble(IntPtr obj, IntPtr sel);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendArg(IntPtr obj, IntPtr sel, IntPtr arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern NSRect SendRectArm(IntPtr obj, IntPtr sel);
    [DllImport(ObjC, EntryPoint = "objc_msgSend_stret")] private static extern void SendRectStret(out NSRect r, IntPtr obj, IntPtr sel);

    /// <summary>Grok's review: a 32-byte struct comes back in registers only on arm64; an x64 run (Intel, Rosetta) needs _stret.</summary>
    private static NSRect SendRect(IntPtr obj, IntPtr sel)
    {
        if (RuntimeInformation.ProcessArchitecture == Architecture.X64) { SendRectStret(out var r, obj, sel); return r; }
        return SendRectArm(obj, sel);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NSRect { public double X, Y, Width, Height; }

    public static IntPtr NSWindowOf(Window w) =>
        w.TryGetPlatformHandle() is Avalonia.Platform.IMacOSTopLevelPlatformHandle mac ? mac.NSWindow : IntPtr.Zero;

    /// <summary>Starts the Mac's own window drag with the press macOS is handling now. False (with the reason) when that press
    /// isn't this window's left press at <paramref name="pressMs"/> (Avalonia's time stamp: the event's seconds x 1000).</summary>
    public static bool TryStart(Window w, ulong pressMs, out string note)
    {
        note = "";
        if (!OperatingSystem.IsMacOS()) { note = "not macOS"; return false; }
        try
        {
            var win = NSWindowOf(w);
            if (win == IntPtr.Zero) { note = "no NSWindow"; return false; }
            var app = Send(ObjCGuard.Class("NSApplication"), sel_registerName(ObjCSelectors.SharedApplication));
            if (app == IntPtr.Zero || !ObjCGuard.RespondsTo(app, ObjCSelectors.CurrentEvent)) { note = "no NSApp"; return false; }
            var ev = Send(app, sel_registerName(ObjCSelectors.CurrentEvent));
            if (ev == IntPtr.Zero) { note = "no current event"; return false; }
            nuint type = SendNUInt(ev, sel_registerName(ObjCSelectors.Type));
            double secs = SendDouble(ev, sel_registerName(ObjCSelectors.Timestamp));
            var evWin = Send(ev, sel_registerName(ObjCSelectors.Window));
            double diff = Math.Abs(secs * 1000 - pressMs);
            note = $"current event type {type}, {(evWin == win ? "this window" : "another window")}, {diff:0.#} ms from the press";
            if (type != LeftMouseDown || evWin != win || diff > 3) return false;
            if (!ObjCGuard.RespondsTo(win, ObjCSelectors.PerformWindowDragWithEvent)) { note += ", no performWindowDragWithEvent:"; return false; }
            SendArg(win, sel_registerName(ObjCSelectors.PerformWindowDragWithEvent), ev);
            return true;
        }
        catch (Exception e) { note = "failed: " + e.Message; return false; }
    }

    /// <summary>The window's real top-left in Avalonia's global points (top-left origin), read from the NSWindow's frame; null
    /// when it can't be read (then the caller uses Position).</summary>
    public static PixelPoint? TopLeft(Window w)
    {
        if (!OperatingSystem.IsMacOS()) return null;
        try
        {
            var win = NSWindowOf(w);
            if (win == IntPtr.Zero) return null;
            var f = SendRect(win, sel_registerName(ObjCSelectors.Frame));
            var screens = Send(ObjCGuard.Class("NSScreen"), sel_registerName(ObjCSelectors.Screens));
            var first = screens == IntPtr.Zero ? IntPtr.Zero : Send(screens, sel_registerName(ObjCSelectors.FirstObject));
            if (first == IntPtr.Zero) return null;
            var primary = SendRect(first, sel_registerName(ObjCSelectors.Frame));   // Cocoa's y runs up from the first screen's bottom
            return new PixelPoint((int)Math.Round(f.X), (int)Math.Round(primary.Height - (f.Y + f.Height)));
        }
        catch { return null; }
    }
}
