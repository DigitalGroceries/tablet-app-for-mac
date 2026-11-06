using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using OpenTabletDriver.Plugin;

namespace OpenTabletDriver.Desktop.Interop.Input
{
    /// <summary>
    /// Version 25: before a posted left mouse-down, activate the app that owns the window under the pointer.
    /// On macOS 27 a click posted to kCGHIDEventTap raises a window without activating its app, and a click
    /// on the desktop doesn't activate Finder (same symptom and workaround as RustDesk discussion #15583).
    /// Uses only owner PID and bounds from CGWindowListCopyWindowInfo, so no Screen Recording permission is
    /// needed. Fails open: any error and the click is posted as before.
    /// </summary>
    internal static class MacOSAppActivator
    {
        private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
        private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        private const string ObjC = "/usr/lib/libobjc.A.dylib";
        private const string AppKit = "/System/Library/Frameworks/AppKit.framework/AppKit";

        private const uint OnScreenOnly = 1;   // desktop elements are kept on purpose: the desktop is Finder's
        private const uint Utf8 = 0x08000100;
        private const int CFNumberSInt32Type = 3;
        private const int CFNumberFloat64Type = 13;
        private const long ActivationPolicyRegular = 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect { public double X, Y, W, H; }

        [DllImport(CoreGraphics)] private static extern IntPtr CGWindowListCopyWindowInfo(uint option, uint relativeToWindow);
        [DllImport(CoreGraphics)] [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool CGRectMakeWithDictionaryRepresentation(IntPtr dict, out Rect rect);
        [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr cf);
        [DllImport(CoreFoundation)] private static extern nint CFArrayGetCount(IntPtr array);
        [DllImport(CoreFoundation)] private static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, nint index);
        [DllImport(CoreFoundation)] private static extern IntPtr CFDictionaryGetValue(IntPtr dict, IntPtr key);
        [DllImport(CoreFoundation)] [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool CFNumberGetValue(IntPtr number, int type, out int value);
        [DllImport(CoreFoundation, EntryPoint = "CFNumberGetValue")] [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool CFNumberGetDouble(IntPtr number, int type, out double value);
        [DllImport(CoreFoundation)] private static extern IntPtr CFStringCreateWithCString(IntPtr alloc, string s, uint encoding);
        [DllImport(CoreFoundation)] [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool CFStringGetCString(IntPtr str, byte[] buffer, nint size, uint encoding);

        [DllImport(ObjC)] private static extern IntPtr objc_getClass(string name);
        [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
        [DllImport(ObjC)] private static extern IntPtr objc_autoreleasePoolPush();
        [DllImport(ObjC)] private static extern void objc_autoreleasePoolPop(IntPtr pool);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendInt(IntPtr obj, IntPtr sel, int a);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendULong(IntPtr obj, IntPtr sel, nuint a);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nint SendNInt(IntPtr obj, IntPtr sel);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool SendBool(IntPtr obj, IntPtr sel);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool RespondsTo(IntPtr obj, IntPtr sel, IntPtr query);

        private static bool _ready, _failed, _warned;
        private static IntPtr _kPid, _kName, _kLayer, _kBounds, _kAlpha;

        public static void ActivateAt(Vector2 point, IReadOnlyList<MacOSScreenEdge.Rect>? displays)
        {
            if (_failed || !OperatingSystem.IsMacOS()) return;
            var pool = IntPtr.Zero;
            try
            {
                Init();
                pool = objc_autoreleasePoolPush();
                var window = MacOSWindowPick.Pick(Windows(), point, displays);
                if (window is { } w) Activate(w.OwnerPid);
            }
            catch (Exception ex)
            {
                if (!_warned) { _warned = true; Log.Write("MacOSAppActivator", $"Couldn't activate the app under the pen; clicks are posted as before. {ex.Message}", LogLevel.Warning); }
                if (ex is DllNotFoundException or EntryPointNotFoundException) _failed = true;
            }
            finally
            {
                if (pool != IntPtr.Zero) objc_autoreleasePoolPop(pool);
            }
        }

        private static void Init()
        {
            if (_ready) return;
            NativeLibrary.Load(AppKit);
            _kPid = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowOwnerPID", Utf8);
            _kName = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowOwnerName", Utf8);
            _kLayer = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowLayer", Utf8);
            _kBounds = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowBounds", Utf8);
            _kAlpha = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowAlpha", Utf8);
            _ready = true;
        }

        /// <summary>On-screen windows, front to back.</summary>
        private static List<MacOSWindowPick.Window> Windows()
        {
            var result = new List<MacOSWindowPick.Window>();
            var list = CGWindowListCopyWindowInfo(OnScreenOnly, 0);
            if (list == IntPtr.Zero) return result;
            try
            {
                var n = CFArrayGetCount(list);
                for (nint i = 0; i < n; i++)
                {
                    var d = CFArrayGetValueAtIndex(list, i);
                    if (d == IntPtr.Zero) continue;
                    var pidRef = CFDictionaryGetValue(d, _kPid);
                    if (pidRef == IntPtr.Zero || !CFNumberGetValue(pidRef, CFNumberSInt32Type, out var pid)) continue;
                    var boundsRef = CFDictionaryGetValue(d, _kBounds);
                    if (boundsRef == IntPtr.Zero || !CGRectMakeWithDictionaryRepresentation(boundsRef, out var r)) continue;
                    var layer = 0;
                    var layerRef = CFDictionaryGetValue(d, _kLayer);
                    if (layerRef != IntPtr.Zero) CFNumberGetValue(layerRef, CFNumberSInt32Type, out layer);
                    double alpha = 1;
                    var alphaRef = CFDictionaryGetValue(d, _kAlpha);
                    if (alphaRef != IntPtr.Zero) CFNumberGetDouble(alphaRef, CFNumberFloat64Type, out alpha);
                    result.Add(new MacOSWindowPick.Window(pid, CFString(CFDictionaryGetValue(d, _kName)), layer,
                        (float)r.X, (float)r.Y, (float)r.W, (float)r.H, (float)alpha));
                }
            }
            finally { CFRelease(list); }
            return result;
        }

        private static string? CFString(IntPtr s)
        {
            if (s == IntPtr.Zero) return null;
            var buf = new byte[256];
            if (!CFStringGetCString(s, buf, buf.Length, Utf8)) return null;
            var len = Array.IndexOf(buf, (byte)0);
            return System.Text.Encoding.UTF8.GetString(buf, 0, len < 0 ? buf.Length : len);
        }

        private static bool Responds(IntPtr obj, string selector) =>
            obj != IntPtr.Zero && RespondsTo(obj, sel_registerName("respondsToSelector:"), sel_registerName(selector));

        /// <summary>Activates a regular app that isn't already active (NSRunningApplication activateWithOptions:0).</summary>
        private static void Activate(int pid)
        {
            var cls = objc_getClass("NSRunningApplication");
            if (!Responds(cls, "runningApplicationWithProcessIdentifier:")) return;
            var app = SendInt(cls, sel_registerName("runningApplicationWithProcessIdentifier:"), pid);
            if (app == IntPtr.Zero) return;
            if (Responds(app, "activationPolicy") && SendNInt(app, sel_registerName("activationPolicy")) != ActivationPolicyRegular) return;
            if (Responds(app, "isActive") && SendBool(app, sel_registerName("isActive"))) return;
            if (Responds(app, "activateWithOptions:"))
                SendULong(app, sel_registerName("activateWithOptions:"), 0);
        }
    }
}
