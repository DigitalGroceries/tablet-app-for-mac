using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Services;

/// <summary>
/// V29 #171 (Zak 20 Nov: "The floating keyboard should check what location keyboard the mac is using, is it GB or US or qwerty or
/// something else and show the same input"): reads the Mac's selected keyboard layout (the input source in the menu bar, e.g. British
/// or Bulgarian – QWERTY) and what each key types in it, with Apple's Text Input Sources and UCKeyTranslate (the same data the Mac's
/// Keyboard Viewer draws), and the keyboard's shape (ISO or ANSI) with KBGetLayoutType(LMGetKbdType()). Main thread only. Returns
/// null off macOS or when anything can't be read (logged once); the floating keyboard then shows its US labels.
/// </summary>
public static class MacKeyboardLayout
{
    private const string HIToolbox = "/System/Library/Frameworks/Carbon.framework/Frameworks/HIToolbox.framework/HIToolbox";
    private const string CoreServices = "/System/Library/Frameworks/CoreServices.framework/CoreServices";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    [DllImport(HIToolbox)] private static extern IntPtr TISCopyCurrentKeyboardLayoutInputSource();
    [DllImport(HIToolbox)] private static extern IntPtr TISGetInputSourceProperty(IntPtr inputSource, IntPtr propertyKey);
    [DllImport(HIToolbox)] private static extern byte LMGetKbdType();
    [DllImport(HIToolbox)] private static extern uint KBGetLayoutType(short keyboardType);
    // Grok's review 20 Nov (1): without CharSet.Unicode a char[] is marshalled as 1-byte ANSI, so UTF-16 Cyrillic came back as
    // Latin junk ('ч' as 'G') while ASCII looked fine. Both calls write UTF-16 (UniChar).
    [DllImport(CoreServices, CharSet = CharSet.Unicode)]
    private static extern int UCKeyTranslate(IntPtr keyLayoutPtr, ushort virtualKeyCode, ushort keyAction, uint modifierKeyState,
        uint keyboardType, uint keyTranslateOptions, ref uint deadKeyState, nuint maxStringLength, out nuint actualStringLength,
        [Out] char[] unicodeString);
    [DllImport(CoreFoundation)] private static extern IntPtr CFDataGetBytePtr(IntPtr data);
    [DllImport(CoreFoundation)] private static extern nint CFStringGetLength(IntPtr str);
    [DllImport(CoreFoundation, CharSet = CharSet.Unicode)] private static extern void CFStringGetCharacters(IntPtr str, CFRange range, [Out] char[] buffer);
    [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr obj);

    [StructLayout(LayoutKind.Sequential)]
    private struct CFRange { public nint Location, Length; public CFRange(nint l, nint n) { Location = l; Length = n; } }

    private const ushort KeyActionDisplay = 3;          // kUCKeyActionDisplay
    private const uint ShiftModifier = 0x0200 >> 8;     // shiftKey, as UCKeyTranslate takes modifiers
    private const uint NoDeadKeys = 1;                  // kUCKeyTranslateNoDeadKeysMask
    private const uint IsoLayout = 0x49534F20;          // 'ISO ' (kKeyboardISO)

    private static bool _warned;
    private static IntPtr _layoutDataKey, _idKey, _nameKey;

    /// <summary>The selected layout now, or null (not a Mac, or it couldn't be read).</summary>
    public static IKeyLabels? Current()
    {
        if (!OperatingSystem.IsMacOS()) return null;
        IntPtr source = IntPtr.Zero;
        try
        {
            if (_layoutDataKey == IntPtr.Zero)
            {
                var lib = NativeLibrary.Load(HIToolbox);
                _layoutDataKey = Marshal.ReadIntPtr(NativeLibrary.GetExport(lib, "kTISPropertyUnicodeKeyLayoutData"));
                _idKey = Marshal.ReadIntPtr(NativeLibrary.GetExport(lib, "kTISPropertyInputSourceID"));
                _nameKey = Marshal.ReadIntPtr(NativeLibrary.GetExport(lib, "kTISPropertyLocalizedName"));
            }
            source = TISCopyCurrentKeyboardLayoutInputSource();
            if (source == IntPtr.Zero) return Fail("no keyboard layout is selected");
            var data = TISGetInputSourceProperty(source, _layoutDataKey);
            if (data == IntPtr.Zero) return Fail("the selected layout has no key data");
            var layout = CFDataGetBytePtr(data);
            var id = ReadString(TISGetInputSourceProperty(source, _idKey)) ?? "";
            var name = ReadString(TISGetInputSourceProperty(source, _nameKey)) ?? id;
            uint kbdType = LMGetKbdType();
            var shape = KBGetLayoutType((short)kbdType) == IsoLayout ? KeyboardShape.Iso : KeyboardShape.Ansi;
            var keys = new Dictionary<int, (string, string)>();
            foreach (var code in FloatingKeyboardLayout.MacKeyCodes.Values)
            {
                var normal = Translate(layout, code, 0, kbdType);
                var shifted = Translate(layout, code, ShiftModifier, kbdType);
                if (normal != null) keys[code] = (normal, shifted ?? "");
            }
            return new Labels(id, name, shape, keys);
        }
        catch (Exception e)
        {
            return Fail(e.Message);
        }
        finally
        {
            if (source != IntPtr.Zero) CFRelease(source);
        }
    }

    private static string? Translate(IntPtr layout, int code, uint modifiers, uint kbdType)
    {
        uint dead = 0;
        var buffer = new char[8];
        var status = UCKeyTranslate(layout, (ushort)code, KeyActionDisplay, modifiers, kbdType, NoDeadKeys, ref dead,
            (nuint)buffer.Length, out var length, buffer);
        if (status != 0 || length == 0) return null;
        var s = new string(buffer, 0, (int)length);
        return s.Length > 0 && !char.IsControl(s[0]) ? s : null;
    }

    private static string? ReadString(IntPtr cfString)
    {
        if (cfString == IntPtr.Zero) return null;
        var n = CFStringGetLength(cfString);
        if (n <= 0 || n > 512) return null;
        var buffer = new char[n];
        CFStringGetCharacters(cfString, new CFRange(0, n), buffer);
        return new string(buffer);
    }

    private static IKeyLabels? Fail(string why)
    {
        if (!_warned) AppLog.Warn($"Floating keyboard: couldn't read the Mac's keyboard layout ({why}); showing US labels.");
        _warned = true;
        return null;
    }

    private sealed class Labels(string id, string name, KeyboardShape shape, Dictionary<int, (string, string)> keys) : IKeyLabels
    {
        public string Id => id;
        public string Name => name;
        public KeyboardShape Shape => shape;
        public (string Normal, string Shifted)? For(int macKeyCode) => keys.TryGetValue(macKeyCode, out var k) ? k : null;
    }
}
