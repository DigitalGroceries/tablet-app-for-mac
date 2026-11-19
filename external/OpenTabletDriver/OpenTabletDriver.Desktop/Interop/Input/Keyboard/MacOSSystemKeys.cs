using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using OpenTabletDriver.Plugin;

namespace OpenTabletDriver.Desktop.Interop.Input.Keyboard
{
    /// <summary>
    /// Tablet App for Mac, Version 29 (TASKS #87; asked on Reddit for the Touch Ring: "volume and screen brightness"). The Mac's
    /// brightness keys have no key code: a Mac keyboard sends them as "system-defined" events (NX_KEYTYPE_BRIGHTNESS_UP 2 / _DOWN 3,
    /// subtype 8), the same way as its media keys. This posts that event at the HID event tap, as the keyboard does. macOS changes
    /// the brightness of the built-in display and Apple displays this way; most other external monitors ignore it.
    /// </summary>
    public static class MacOSSystemKeys
    {
        /// <summary>OTD key name to NX_KEYTYPE_* number.</summary>
        public static readonly Dictionary<string, int> Keys = new Dictionary<string, int>
        {
            { "BrightnessUp", 2 },
            { "BrightnessDown", 3 },
        };

        private const string ObjC = "/usr/lib/libobjc.A.dylib";
        private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
        private const string AppKit = "/System/Library/Frameworks/AppKit.framework/AppKit";
        private const int NSEventTypeSystemDefined = 14, AuxControlButtons = 8, KeyDown = 0xA, KeyUp = 0xB;

        [StructLayout(LayoutKind.Sequential)] private struct CGPoint { public double X, Y; }

        [DllImport(ObjC)] private static extern IntPtr objc_getClass(string name);
        [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        private static extern IntPtr SendOtherEvent(IntPtr cls, IntPtr sel, nuint type, CGPoint location, nuint modifierFlags,
            double timestamp, nint windowNumber, IntPtr context, short subtype, nint data1, nint data2);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendPtr(IntPtr obj, IntPtr sel);
        [DllImport(CoreGraphics)] private static extern void CGEventPost(int tap, IntPtr evt);
        [DllImport("/usr/lib/libSystem.dylib")] private static extern IntPtr dlopen(string path, int mode);

        private static bool _loaded, _failed;

        /// <summary>The NX_KEYTYPE_* data1 word for a key going down or up (shared with tests).</summary>
        public static long Data1(int keyType, bool down) => ((long)keyType << 16) | ((long)(down ? KeyDown : KeyUp) << 8);

        /// <summary>Post the key's down or up event. Returns false (and logs once) when macOS refuses.</summary>
        public static bool Post(string key, bool down)
        {
            if (!Keys.TryGetValue(key, out var type) || _failed || !OperatingSystem.IsMacOS()) return false;
            try
            {
                if (!_loaded) { dlopen(AppKit, 1); _loaded = true; }
                var cls = objc_getClass("NSEvent");
                if (cls == IntPtr.Zero) throw new InvalidOperationException("NSEvent is not available");
                var sel = sel_registerName("otherEventWithType:location:modifierFlags:timestamp:windowNumber:context:subtype:data1:data2:");
                var ev = SendOtherEvent(cls, sel, NSEventTypeSystemDefined, default, (nuint)(down ? 0xA00 : 0xB00), 0, 0, IntPtr.Zero,
                    AuxControlButtons, (nint)Data1(type, down), -1);
                if (ev == IntPtr.Zero) throw new InvalidOperationException("NSEvent returned no event");
                var cg = SendPtr(ev, sel_registerName("CGEvent"));
                if (cg == IntPtr.Zero) throw new InvalidOperationException("the event has no CGEvent");
                CGEventPost(0 /* kCGHIDEventTap */, cg);
                return true;
            }
            catch (Exception e)
            {
                _failed = true;
                Log.Write("Keyboard", $"Couldn't send the {key} key: {e.Message}", LogLevel.Warning);
                return false;
            }
        }
    }
}
