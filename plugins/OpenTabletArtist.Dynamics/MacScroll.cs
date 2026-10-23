using System;
using System.Runtime.InteropServices;

namespace OpenTabletArtist.Dynamics;

/// <summary>
/// Build I: posts one scroll event with the Option modifier set on the event itself (macOS only). Photoshop
/// zooms on Option+scroll, smoothly with pixel-unit (trackpad-style) scrolls. Setting the flag on the event,
/// instead of holding the Option key, means nothing is ever left held down and the modifier can't arrive
/// after the scroll. Same CoreGraphics calls OTD's own macOS mouse uses (non-variadic ...Event2). Does
/// nothing on other systems or if CoreGraphics fails.
/// </summary>
internal static class MacScroll
{
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const uint PixelUnits = 0;            // kCGScrollEventUnitPixel
    private const ulong OptionFlag = 0x00080000;  // kCGEventFlagMaskAlternate
    private const uint HidEventTap = 0;           // kCGHIDEventTap

    [DllImport(CoreGraphics)] private static extern IntPtr CGEventCreateScrollWheelEvent2(IntPtr source, uint units, uint wheelCount, int wheel1, int wheel2, int wheel3);
    [DllImport(CoreGraphics)] private static extern void CGEventSetFlags(IntPtr evt, ulong flags);
    [DllImport(CoreGraphics)] private static extern void CGEventPost(uint tap, IntPtr evt);
    [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr cf);

    private const uint LineUnits = 1;             // kCGScrollEventUnitLine (a mouse-wheel notch)
    private const ushort KeyCodeOption = 58;      // kVK_Option
    /// <summary>Build K: sensitivity "pixels" per wheel line. 10 at sensitivity 20 = 2 lines per 5° of ring.</summary>
    public const int PixelsPerLine = 10;
    private static readonly object Gate = new();
    private static int _remainder;

    [DllImport(CoreGraphics)] private static extern IntPtr CGEventSourceCreate(int stateId);
    private static readonly Lazy<IntPtr> Src = new(() => { try { return CGEventSourceCreate(1); } catch { return IntPtr.Zero; } });
    private const int ModifierSettleMs = 20;
    [DllImport(CoreGraphics)] private static extern IntPtr CGEventCreateKeyboardEvent(IntPtr src, ushort key, [MarshalAs(UnmanagedType.I1)] bool down);

    /// <summary>Lines to send for <paramref name="pixels"/>, carrying the remainder so slow settings still move.</summary>
    public static int TakeLines(int pixels, ref int remainder)
    {
        remainder += pixels;
        int lines = remainder / PixelsPerLine;   // truncates toward 0 for both signs
        remainder -= lines * PixelsPerLine;
        return lines;
    }

    /// <summary>Build K: zoom in Photoshop as a real mouse wheel with Option held. Build I posted
    /// pixel (trackpad-style) scrolls with only the Option flag on the event, which Photoshop treated as a
    /// pan. Now: Option key down, discrete line-based wheel events carrying the Option flag, Option key up
    /// (always, in finally). Positive = up = zoom in.</summary>
    public static void PostWithOption(int pixels)
    {
        if (pixels == 0 || !OperatingSystem.IsMacOS()) return;
        int lines;
        lock (Gate) lines = TakeLines(pixels, ref _remainder);
        if (lines == 0) return;
        try
        {
            PostKey(KeyCodeOption, true, OptionFlag);
            try
            {
                System.Threading.Thread.Sleep(ModifierSettleMs);   // Option registered before the wheel
                var e = CGEventCreateScrollWheelEvent2(Src.Value, LineUnits, 1, Math.Clamp(lines, -10, 10), 0, 0);
                if (e == IntPtr.Zero) return;
                try
                {
                    CGEventSetFlags(e, OptionFlag);
                    CGEventPost(HidEventTap, e);
                }
                finally { CFRelease(e); }
            }
            finally { System.Threading.Thread.Sleep(5); PostKey(KeyCodeOption, false, 0); }
        }
        catch (Exception) { /* no CoreGraphics: nothing to do */ }
    }

    private static void PostKey(ushort key, bool down, ulong flags)
    {
        var e = CGEventCreateKeyboardEvent(Src.Value, key, down);
        if (e == IntPtr.Zero) return;
        try { CGEventSetFlags(e, flags); CGEventPost(HidEventTap, e); }
        finally { CFRelease(e); }
    }
}
