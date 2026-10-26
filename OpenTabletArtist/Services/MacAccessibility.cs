using System;
using System.Runtime.InteropServices;

namespace OpenTabletArtist.Services;

/// <summary>Version 7: the two macOS permissions the pen needs. The daemon is started by the app, so macOS
/// credits it to the app (the responsible process): the grants belong to "Tablet App for Mac".
/// Input Monitoring lets the daemon open the tablet (IOHID). Accessibility, called "Device Control and Data
/// Access" from macOS 27, lets it post pointer events (CGEventPost); without it macOS drops them silently.</summary>
public static class MacAccessibility
{
    const string AppServices = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    const string IOKit = "/System/Library/Frameworks/IOKit.framework/IOKit";
    const string CF = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    const uint ListenEvent = 1;          // kIOHIDRequestTypeListenEvent
    const uint Granted = 0;              // kIOHIDAccessTypeGranted

    [DllImport(AppServices)] static extern bool AXIsProcessTrusted();
    [DllImport(AppServices)] static extern bool AXIsProcessTrustedWithOptions(IntPtr options);
    [DllImport(IOKit)] static extern uint IOHIDCheckAccess(uint requestType);
    [DllImport(IOKit)] static extern bool IOHIDRequestAccess(uint requestType);
    [DllImport(CF)] static extern IntPtr CFDictionaryCreate(IntPtr alloc, IntPtr[] keys, IntPtr[] values, nint count, IntPtr keyCb, IntPtr valueCb);
    [DllImport(CF)] static extern void CFRelease(IntPtr cf);

    /// <summary>True off macOS, or when the grant is there.</summary>
    public static bool IsTrusted()
    {
        if (!OperatingSystem.IsMacOS()) return true;
        try { return AXIsProcessTrusted(); } catch (Exception) { return true; }
    }

    /// <summary>True off macOS, or when Input Monitoring is granted.</summary>
    public static bool CanReadPen()
    {
        if (!OperatingSystem.IsMacOS()) return true;
        try { return IOHIDCheckAccess(ListenEvent) == Granted; } catch (Exception) { return true; }
    }

    /// <summary>Shows the system Input Monitoring prompt (only the first time macOS will show it).</summary>
    public static void RequestReadPen()
    {
        if (!OperatingSystem.IsMacOS()) return;
        try { IOHIDRequestAccess(ListenEvent); } catch (Exception ex) { AppLog.Warn("Input Monitoring request failed.", ex); }
    }

    /// <summary>Shows the system Accessibility prompt (AXIsProcessTrustedWithOptions, prompt = true).</summary>
    public static void RequestMovePointer()
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            var ax = NativeLibrary.Load(AppServices);
            var cf = NativeLibrary.Load(CF);
            var key = Marshal.ReadIntPtr(NativeLibrary.GetExport(ax, "kAXTrustedCheckOptionPrompt"));
            var yes = Marshal.ReadIntPtr(NativeLibrary.GetExport(cf, "kCFBooleanTrue"));
            var dict = CFDictionaryCreate(IntPtr.Zero, new[] { key }, new[] { yes }, 1,
                NativeLibrary.GetExport(cf, "kCFTypeDictionaryKeyCallBacks"),
                NativeLibrary.GetExport(cf, "kCFTypeDictionaryValueCallBacks"));
            AXIsProcessTrustedWithOptions(dict);
            CFRelease(dict);
        }
        catch (Exception ex) { AppLog.Warn("Accessibility request failed.", ex); }
    }

    /// <summary>The name macOS uses for the Accessibility list: renamed in macOS 27.</summary>
    public static string PointerPaneName(int macMajor) => macMajor >= 27 ? "Device Control and Data Access" : "Accessibility";

    public static string PointerPaneName() => PointerPaneName(Environment.OSVersion.Version.Major);

    public static string MissingText => $"The pen can't move the pointer yet. Allow Tablet App for Mac in {PointerPaneName()}, then restart the app.";
}
