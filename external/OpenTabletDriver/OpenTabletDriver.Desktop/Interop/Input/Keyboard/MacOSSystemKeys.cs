using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using OpenTabletDriver.Plugin;

namespace OpenTabletDriver.Desktop.Interop.Input.Keyboard
{
    /// <summary>
    /// Tablet App for Mac, Version 29 (TASKS #87; asked on Reddit for the Touch Ring: "volume and screen brightness").
    /// The Mac's brightness keys have no key code: a Mac keyboard sends them as "system-defined" events
    /// (NX_KEYTYPE_BRIGHTNESS_UP 2 / _DOWN 3, subtype 8), the same way as its media keys.
    ///
    /// Zak's test of build 4 (20 Nov): posting that event always changed the built-in screen, while his keyboard changes
    /// the screen of the window he last clicked (and Control + brightness the other screen). So a press now:
    ///  1. picks the screen the Mac's own key would: the screen holding the frontmost normal window, else the pointer's
    ///     screen, else the main screen (<see cref="PickDisplay"/>);
    ///  2. without Control, sets that screen's brightness directly through macOS's DisplayServices, one of the Mac's
    ///     16 brightness steps, when that screen answers it (loaded at run time with dlopen/dlsym; any missing piece
    ///     falls back to step 3, never a crash; the rules for a screen that doesn't answer are in <see cref="DirectBrightness"/>);
    ///  3. otherwise (Control held, or a screen DisplayServices can't change) posts the key event as before: with
    ///     Control, the Control flag and no place of its own (macOS picks "the other screen", as with its own key);
    ///     without, placed on the chosen screen.
    /// The screen is looked up at most every 300 ms, so a fast turn doesn't make the pen stutter. Our own windows (radial
    /// menu, floating keyboard, the app) never count as the front window.
    /// The driver log says which screen and which way, each time that changes, so an in-app report shows it.
    /// </summary>
    public static class MacOSSystemKeys
    {
        /// <summary>OTD key name to NX_KEYTYPE_* number.</summary>
        public static readonly Dictionary<string, int> Keys = new Dictionary<string, int>
        {
            { "BrightnessUp", 2 },
            { "BrightnessDown", 3 },
        };

        /// <summary>kCGEventFlagMaskControl = NSEventModifierFlagControl.</summary>
        public const ulong ControlFlag = 0x40000;

        /// <summary>The Mac's brightness keys move in 16 steps (the 16 squares of macOS's brightness pop-up).</summary>
        public const int BrightnessSteps = 16;

        private const string ObjC = "/usr/lib/libobjc.A.dylib";
        private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
        private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        private const string AppKit = "/System/Library/Frameworks/AppKit.framework/AppKit";
        private const string DisplayServicesPath = "/System/Library/PrivateFrameworks/DisplayServices.framework/DisplayServices";
        private const string LibSystem = "/usr/lib/libSystem.dylib";
        private const int NSEventTypeSystemDefined = 14, AuxControlButtons = 8, KeyDown = 0xA, KeyUp = 0xB;
        private const int RtldLazy = 1;
        private const int HidSystemState = 1;
        private const uint OnScreenOnly = 1, ExcludeDesktopElements = 16, Utf8 = 0x08000100;
        private const int CFNumberSInt32Type = 3, CFNumberFloat64Type = 6;

        [StructLayout(LayoutKind.Sequential)] private struct CGPoint { public double X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct CGRect { public double X, Y, W, H; }

        [DllImport(ObjC)] private static extern IntPtr objc_getClass(string name);
        [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        private static extern IntPtr SendOtherEvent(IntPtr cls, IntPtr sel, nuint type, CGPoint location, nuint modifierFlags,
            double timestamp, nint windowNumber, IntPtr context, short subtype, nint data1, nint data2);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendPtr(IntPtr obj, IntPtr sel);
        [DllImport(CoreGraphics)] private static extern void CGEventPost(int tap, IntPtr evt);
        [DllImport(CoreGraphics)] private static extern void CGEventSetLocation(IntPtr evt, CGPoint location);
        [DllImport(CoreGraphics)] private static extern IntPtr CGEventCreate(IntPtr source);
        [DllImport(CoreGraphics)] private static extern CGPoint CGEventGetLocation(IntPtr evt);
        [DllImport(CoreGraphics)] private static extern ulong CGEventSourceFlagsState(int stateId);
        [DllImport(CoreGraphics)] private static extern int CGGetActiveDisplayList(uint max, [In, Out] uint[] displays, out uint count);
        [DllImport(CoreGraphics)] private static extern CGRect CGDisplayBounds(uint display);
        [DllImport(CoreGraphics)] private static extern uint CGMainDisplayID();
        [DllImport(CoreGraphics)] private static extern int CGDisplayIsBuiltin(uint display);
        [DllImport(CoreGraphics)] private static extern uint CGDisplayMirrorsDisplay(uint display);
        [DllImport(ObjC)] private static extern IntPtr objc_autoreleasePoolPush();
        [DllImport(ObjC)] private static extern void objc_autoreleasePoolPop(IntPtr pool);
        [DllImport(CoreFoundation)] [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool CFNumberGetValue(IntPtr number, int type, out double value);
        [DllImport(CoreFoundation)] private static extern IntPtr CFStringGetCStringPtr(IntPtr s, uint encoding);
        [DllImport(CoreFoundation)] [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool CFStringGetCString(IntPtr s, byte[] buffer, nint size, uint encoding);
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
        [DllImport(LibSystem)] private static extern IntPtr dlopen(string path, int mode);
        [DllImport(LibSystem)] private static extern IntPtr dlsym(IntPtr handle, string symbol);

        // DisplayServices (private framework; signatures as MonitorControl's Bridging-Header.h uses them):
        // int DisplayServicesGetBrightness(CGDirectDisplayID, float *); int DisplayServicesSetBrightness(CGDirectDisplayID, float)
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetBrightnessFn(uint display, out float brightness);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetBrightnessFn(uint display, float brightness);

        private static readonly object Gate = new object();
        private static bool _appKitLoaded, _keyEventFailed, _dsTried;
        private static GetBrightnessFn? _getBrightness;
        private static SetBrightnessFn? _setBrightness;
        private static readonly Dictionary<int, bool> DownWasDirect = new Dictionary<int, bool>();
        private static CGPoint? _lastTargetCentre;
        private static string? _lastLogged;

        /// <summary>The NX_KEYTYPE_* data1 word for a key going down or up (shared with tests).</summary>
        public static long Data1(int keyType, bool down) => ((long)keyType << 16) | ((long)(down ? KeyDown : KeyUp) << 8);

        /// <summary>The NSEvent modifier-flags word for the posted event: 0xA00 / 0xB00, plus Control when it is held.</summary>
        public static ulong EventFlags(bool down, bool control) => (ulong)(down ? 0xA00 : 0xB00) | (control ? ControlFlag : 0);

        /// <summary>A screen or window rectangle in global display points (top-left origin of the main display).</summary>
        public readonly record struct Box(double X, double Y, double W, double H)
        {
            public double OverlapArea(Box o)
            {
                double w = Math.Min(X + W, o.X + o.W) - Math.Max(X, o.X);
                double h = Math.Min(Y + H, o.Y + o.H) - Math.Max(Y, o.Y);
                return w > 0 && h > 0 ? w * h : 0;
            }

            public bool Contains(double x, double y) => x >= X && x < X + W && y >= Y && y < Y + H;
            public (double X, double Y) Centre => (X + W / 2, Y + H / 2);
        }

        /// <summary>
        /// Which screen a brightness press should change, the way the Mac's own key does: the screen that holds most of
        /// the frontmost normal window (the window last clicked), else the screen under the pointer, else the main screen.
        /// </summary>
        public static (uint Display, string Why) PickDisplay(Box? frontWindow, (double X, double Y)? pointer,
            IReadOnlyList<(uint Id, Box Bounds)> displays, uint mainDisplay)
        {
            if (frontWindow is Box w)
            {
                uint best = 0; double bestArea = 0;
                foreach (var (id, b) in displays)
                {
                    var a = b.OverlapArea(w);
                    if (a > bestArea) { bestArea = a; best = id; }
                }
                if (bestArea > 0) return (best, "the front window's screen");
            }
            if (pointer is (double px, double py))
                foreach (var (id, b) in displays)
                    if (b.Contains(px, py)) return (id, "the pointer's screen");
            return (mainDisplay, "the main screen");
        }

        /// <summary>One brightness step from <paramref name="current"/> (0..1), snapped to the Mac's 16 steps.</summary>
        public static float NextBrightness(float current, bool up)
        {
            double step = Math.Round(Math.Clamp(current, 0f, 1f) * BrightnessSteps) + (up ? 1 : -1);
            return (float)(Math.Clamp(step, 0, BrightnessSteps) / BrightnessSteps);
        }

        /// <summary>Press or release a brightness key. <paramref name="heldFlags"/>: the modifiers the driver itself holds
        /// down at this moment (a binding such as Control + Brightness up). Returns false (and logs) when nothing could be done.</summary>
        public static bool Post(string key, bool down, ulong heldFlags = 0)
        {
            if (!Keys.TryGetValue(key, out var type) || !OperatingSystem.IsMacOS()) return false;
            lock (Gate)
            {
                bool control = ((heldFlags | PhysicalFlags()) & ControlFlag) != 0;
                if (down)
                {
                    var (display, why, centre) = CachedTarget();
                    // Control means "the other screen" on the Mac's own keyboard: leave the event's place alone then,
                    // as the keyboard does, so macOS picks the screen itself.
                    _lastTargetCentre = control ? null : centre;
                    if (!control && TrySetDirectly(display, why, up: type == 2))
                    {
                        DownWasDirect[type] = true;
                        return true;
                    }
                    DownWasDirect[type] = false;
                    LogOnce($"Brightness {(type == 2 ? "up" : "down")}: sent as the Mac's brightness key{(control ? " with Control" : "")}, placed on display {display} ({Kind(display)}, {why})");
                }
                else if (DownWasDirect.TryGetValue(type, out var direct) && direct)
                {
                    return true; // the press already changed the screen; a real key-up never changes brightness
                }
                return PostKeyEvent(key, type, down, control);
            }
        }

        private static bool PostKeyEvent(string key, int type, bool down, bool control)
        {
            if (_keyEventFailed) return false;
            var pool = IntPtr.Zero;
            try
            {
                pool = objc_autoreleasePoolPush(); // the NSEvent is autoreleased; without a pool each press leaked one
                if (!_appKitLoaded) { dlopen(AppKit, RtldLazy); _appKitLoaded = true; }
                var cls = objc_getClass("NSEvent");
                if (cls == IntPtr.Zero) throw new InvalidOperationException("NSEvent is not available");
                var sel = sel_registerName("otherEventWithType:location:modifierFlags:timestamp:windowNumber:context:subtype:data1:data2:");
                var ev = SendOtherEvent(cls, sel, NSEventTypeSystemDefined, default, (nuint)EventFlags(down, control), 0, 0, IntPtr.Zero,
                    AuxControlButtons, (nint)Data1(type, down), -1);
                if (ev == IntPtr.Zero) throw new InvalidOperationException("NSEvent returned no event");
                var cg = SendPtr(ev, sel_registerName("CGEvent"));
                if (cg == IntPtr.Zero) throw new InvalidOperationException("the event has no CGEvent");
                if (_lastTargetCentre is CGPoint at) CGEventSetLocation(cg, at);
                CGEventPost(0 /* kCGHIDEventTap */, cg);
                return true;
            }
            catch (Exception e)
            {
                _keyEventFailed = true;
                Log.Write("Keyboard", $"Couldn't send the {key} key: {e.Message}", LogLevel.Warning);
                return false;
            }
            finally
            {
                if (pool != IntPtr.Zero) objc_autoreleasePoolPop(pool);
            }
        }

        /// <summary>Step 2 on the Mac: the chosen screen through DisplayServices (see <see cref="DirectBrightness"/>).</summary>
        private static bool TrySetDirectly(uint display, string why, bool up)
        {
            if (display == 0 || !LoadDisplayServices()) return false;
            Direct ??= new DirectBrightness(
                d => _getBrightness!(d, out var b) == 0 ? (true, b) : (false, 0f),
                (d, b) => _setBrightness!(d, b) == 0,
                d => Kind(d) == "built-in",
                (text, level) => { if (level >= LogLevel.Warning) Log.Write("Keyboard", text, level); else LogOnce(text, level); });
            return Direct.Step(display, up, $"{Kind(display)}, {why}") != DirectBrightness.Outcome.NotPossible;
        }

        private static DirectBrightness? Direct;

        private static bool LoadDisplayServices()
        {
            if (_dsTried) return _getBrightness != null && _setBrightness != null;
            _dsTried = true;
            try
            {
                var lib = dlopen(DisplayServicesPath, RtldLazy);
                if (lib == IntPtr.Zero) { Log.Write("Keyboard", "Brightness: DisplayServices isn't available on this Mac; using the Mac's brightness key", LogLevel.Info); return false; }
                var get = dlsym(lib, "DisplayServicesGetBrightness");
                var set = dlsym(lib, "DisplayServicesSetBrightness");
                if (get == IntPtr.Zero || set == IntPtr.Zero) { Log.Write("Keyboard", "Brightness: DisplayServices has no brightness functions on this macOS; using the Mac's brightness key", LogLevel.Info); return false; }
                _getBrightness = Marshal.GetDelegateForFunctionPointer<GetBrightnessFn>(get);
                _setBrightness = Marshal.GetDelegateForFunctionPointer<SetBrightnessFn>(set);
                return true;
            }
            catch (Exception e)
            {
                _getBrightness = null; _setBrightness = null;
                Log.Write("Keyboard", $"Brightness: couldn't load DisplayServices ({e.Message}); using the Mac's brightness key", LogLevel.Warning);
                return false;
            }
        }

        private static (uint Display, string Why, CGPoint? Centre) _cachedTarget;
        private static long _cachedTargetAt = long.MinValue;

        /// <summary>One ring turn sends many presses: the screen is looked up at most every 300 ms (Grok's review: reading
        /// the window list on every detent could make the pen stutter on a fast turn).</summary>
        private static (uint Display, string Why, CGPoint? Centre) CachedTarget()
        {
            long now = Environment.TickCount64;
            if (now - _cachedTargetAt > 300 || _cachedTarget.Display == 0)
            {
                _cachedTarget = Target();
                _cachedTargetAt = now;
            }
            return _cachedTarget;
        }

        /// <summary>Step 1 on the Mac: gather the front window, the pointer and the screens, then <see cref="PickDisplay"/>.</summary>
        private static (uint Display, string Why, CGPoint? Centre) Target()
        {
            var displays = new List<(uint, Box)>();
            uint main = 0;
            try
            {
                main = CGMainDisplayID();
                var ids = new uint[16];
                if (CGGetActiveDisplayList((uint)ids.Length, ids, out var n) == 0)
                    for (int i = 0; i < n; i++)
                    {
                        if (CGDisplayMirrorsDisplay(ids[i]) != 0) continue; // a mirror has the same bounds as its main screen
                        var r = CGDisplayBounds(ids[i]);
                        displays.Add((ids[i], new Box(r.X, r.Y, r.W, r.H)));
                    }
            }
            catch (Exception e)
            {
                LogOnce($"Brightness: couldn't list the screens ({e.Message})", LogLevel.Warning);
            }
            var (display, why) = PickDisplay(FrontWindow(), Pointer(), displays, main);
            CGPoint? centre = null;
            foreach (var (id, b) in displays)
                if (id == display) { var c = b.Centre; centre = new CGPoint { X = c.X, Y = c.Y }; }
            return (display, why, centre);
        }

        /// <summary>The frontmost normal window (layer 0, at least 50 x 50) of any app, front-to-back order, or null.
        /// Window bounds need no Screen Recording permission (titles would).</summary>
        private static Box? FrontWindow()
        {
            IntPtr list = IntPtr.Zero, kLayer = IntPtr.Zero, kBounds = IntPtr.Zero, kPid = IntPtr.Zero, kOwner = IntPtr.Zero, kAlpha = IntPtr.Zero;
            try
            {
                list = CGWindowListCopyWindowInfo(OnScreenOnly | ExcludeDesktopElements, 0);
                if (list == IntPtr.Zero) return null;
                kLayer = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowLayer", Utf8);
                kBounds = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowBounds", Utf8);
                kPid = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowOwnerPID", Utf8);
                kOwner = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowOwnerName", Utf8);
                kAlpha = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowAlpha", Utf8);
                int me = Environment.ProcessId;
                nint count = CFArrayGetCount(list);
                for (nint i = 0; i < count; i++)
                {
                    var d = CFArrayGetValueAtIndex(list, i);
                    var layer = CFDictionaryGetValue(d, kLayer);
                    if (layer == IntPtr.Zero || !CFNumberGetValue(layer, CFNumberSInt32Type, out int l) || l != 0) continue;
                    // Never our own windows (Grok's review): the radial menu, the floating keyboard and the app's pop-ups
                    // are not where the user is working. Window owner names need no Screen Recording permission.
                    var pid = CFDictionaryGetValue(d, kPid);
                    if (pid != IntPtr.Zero && CFNumberGetValue(pid, CFNumberSInt32Type, out int p) && p == me) continue;
                    if (IsOwnApp(CfString(CFDictionaryGetValue(d, kOwner)))) continue;
                    var alpha = CFDictionaryGetValue(d, kAlpha);
                    if (alpha != IntPtr.Zero && CFNumberGetValue(alpha, CFNumberFloat64Type, out double a) && a <= 0) continue;
                    var b = CFDictionaryGetValue(d, kBounds);
                    if (b == IntPtr.Zero || !CGRectMakeWithDictionaryRepresentation(b, out var r)) continue;
                    if (r.W < 50 || r.H < 50) continue;
                    return new Box(r.X, r.Y, r.W, r.H);
                }
                return null;
            }
            catch (Exception e)
            {
                LogOnce($"Brightness: couldn't read the front window ({e.Message}); using the pointer's screen", LogLevel.Warning);
                return null;
            }
            finally
            {
                foreach (var cf in new[] { list, kLayer, kBounds, kPid, kOwner, kAlpha })
                    if (cf != IntPtr.Zero) CFRelease(cf);
            }
        }

        /// <summary>True for the Tablet App's own process names (the executable keeps its old name, OpenTabletArtist).</summary>
        public static bool IsOwnApp(string? ownerName) =>
            ownerName is "OpenTabletArtist" or "Tablet App for Mac" or "OpenTabletDriver.Daemon";

        private static string? CfString(IntPtr s)
        {
            if (s == IntPtr.Zero) return null;
            var p = CFStringGetCStringPtr(s, Utf8);
            if (p != IntPtr.Zero) return Marshal.PtrToStringUTF8(p);
            var buf = new byte[256];
            return CFStringGetCString(s, buf, buf.Length, Utf8) ? System.Text.Encoding.UTF8.GetString(buf).TrimEnd('\0') : null;
        }

        private static (double X, double Y)? Pointer()
        {
            try
            {
                var e = CGEventCreate(IntPtr.Zero);
                if (e == IntPtr.Zero) return null;
                try { var p = CGEventGetLocation(e); return (p.X, p.Y); }
                finally { CFRelease(e); }
            }
            catch (Exception) { return null; }
        }

        private static ulong PhysicalFlags()
        {
            try { return CGEventSourceFlagsState(HidSystemState); }
            catch (Exception) { return 0; }
        }

        private static string Kind(uint display)
        {
            try { return CGDisplayIsBuiltin(display) != 0 ? "built-in" : "external"; }
            catch (Exception) { return "unknown"; }
        }

        /// <summary>Write a line only when it differs from the last one, so turning the ring doesn't flood the log.</summary>
        private static void LogOnce(string text, LogLevel level = LogLevel.Info)
        {
            if (text == _lastLogged) return;
            _lastLogged = text;
            Log.Write("Keyboard", text, level);
        }
    }
}
