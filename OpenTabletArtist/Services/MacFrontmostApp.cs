using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Services;

/// <summary>
/// macOS frontmost-app and running-app helpers for Application-row profiles.
/// Uses the same NSRunningApplication bundle-id path as the Dynamics ring gesture.
/// Off macOS every call returns null / empty.
/// </summary>
internal static class MacFrontmostApp
{
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    private const uint OnScreenOnly = 1, ExcludeDesktopElements = 16;
    private const uint Utf8 = 0x08000100;
    private const int CFNumberSInt32Type = 3;

    [DllImport(CoreGraphics)] private static extern IntPtr CGWindowListCopyWindowInfo(uint option, uint relativeToWindow);
    [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr cf);
    [DllImport(CoreFoundation)] private static extern nint CFArrayGetCount(IntPtr array);
    [DllImport(CoreFoundation)] private static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, nint index);
    [DllImport(CoreFoundation)] private static extern IntPtr CFDictionaryGetValue(IntPtr dict, IntPtr key);
    [DllImport(CoreFoundation)] [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CFNumberGetValue(IntPtr number, int type, out int value);
    [DllImport(CoreFoundation)] private static extern IntPtr CFStringCreateWithCString(IntPtr alloc, string s, uint encoding);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send0(IntPtr obj, IntPtr sel);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendInt(IntPtr obj, IntPtr sel, int a);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendObj(IntPtr obj, IntPtr sel, IntPtr a);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nint SendNInt(IntPtr obj, IntPtr sel);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendUtf8(IntPtr obj, IntPtr sel, [MarshalAs(UnmanagedType.LPUTF8Str)] string s);

    private static bool _appKitLoaded;

    /// <summary>Bundle id of the frontmost other app (layer-0 window, not us), or null.</summary>
    public static string? FrontmostBundleId()
    {
        if (!OperatingSystem.IsMacOS()) return null;
        try
        {
            var pid = FrontmostPid();
            return pid is int p ? BundleId(p) : null;
        }
        catch (Exception) { return null; }
    }

    /// <summary>Running regular (non-background) apps with a bundle id, for the Add Application list.</summary>
    public static IReadOnlyList<MacAppCandidate> RunningApps()
    {
        if (!OperatingSystem.IsMacOS()) return Array.Empty<MacAppCandidate>();
        EnsureAppKit();
        var result = new List<MacAppCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var wsClass = ObjCGuard.Class("NSWorkspace");
            if (wsClass == IntPtr.Zero || !ObjCGuard.RespondsTo(wsClass, "sharedWorkspace")) return result;
            var shared = Send0(wsClass, ObjCGuard.Sel("sharedWorkspace"));
            if (shared == IntPtr.Zero || !ObjCGuard.RespondsTo(shared, "runningApplications")) return result;
            var apps = Send0(shared, ObjCGuard.Sel("runningApplications"));
            if (apps == IntPtr.Zero || !ObjCGuard.RespondsTo(apps, "count")) return result;
            var n = (int)SendNInt(apps, ObjCGuard.Sel("count"));
            for (int i = 0; i < n; i++)
            {
                if (!ObjCGuard.RespondsTo(apps, "objectAtIndex:")) break;
                var app = SendInt(apps, ObjCGuard.Sel("objectAtIndex:"), i);
                if (app == IntPtr.Zero) continue;
                if (ObjCGuard.RespondsTo(app, "activationPolicy"))
                {
                    // NSApplicationActivationPolicyRegular = 0
                    if ((int)SendNInt(app, ObjCGuard.Sel("activationPolicy")) != 0) continue;
                }
                if (ObjCGuard.RespondsTo(app, "processIdentifier")
                    && (int)SendNInt(app, ObjCGuard.Sel("processIdentifier")) == Environment.ProcessId)
                    continue;
                var bundleId = ReadNsString(app, "bundleIdentifier");
                if (string.IsNullOrWhiteSpace(bundleId) || !seen.Add(bundleId!)) continue;
                var name = ReadNsString(app, "localizedName") ?? bundleId!;
                var appPath = "";
                if (ObjCGuard.RespondsTo(app, "bundleURL"))
                {
                    var url = Send0(app, ObjCGuard.Sel("bundleURL"));
                    if (url != IntPtr.Zero && ObjCGuard.RespondsTo(url, "path"))
                        appPath = ReadNsString(url, "path") ?? "";
                }
                result.Add(new MacAppCandidate(bundleId!, name, appPath));
            }
        }
        catch (Exception ex) { AppLog.Warn("Couldn't list running applications.", ex); }
        result.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    /// <summary>Read bundle id + name from a .app path via NSBundle.</summary>
    public static MacAppCandidate? FromAppPath(string appPath)
    {
        if (!OperatingSystem.IsMacOS() || string.IsNullOrWhiteSpace(appPath)) return null;
        try
        {
            var full = Path.GetFullPath(appPath.TrimEnd('/'));
            if (!full.EndsWith(".app", StringComparison.OrdinalIgnoreCase) || !Directory.Exists(full))
                return null;
            EnsureAppKit();
            var nsBundle = ObjCGuard.Class("NSBundle");
            if (nsBundle == IntPtr.Zero || !ObjCGuard.RespondsTo(nsBundle, "bundleWithPath:"))
                return Fallback(full);
            var pathNs = NsString(full);
            if (pathNs == IntPtr.Zero) return Fallback(full);
            var bundle = SendObj(nsBundle, ObjCGuard.Sel("bundleWithPath:"), pathNs);
            if (bundle == IntPtr.Zero) return Fallback(full);
            var bundleId = ReadNsString(bundle, "bundleIdentifier");
            var name = ReadInfo(bundle, "CFBundleDisplayName")
                       ?? ReadInfo(bundle, "CFBundleName")
                       ?? Path.GetFileNameWithoutExtension(full);
            if (string.IsNullOrWhiteSpace(bundleId)) return Fallback(full);
            return new MacAppCandidate(bundleId!, name ?? bundleId!, full);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Couldn't read app info from {appPath}.", ex);
            return null;
        }
    }

    private static MacAppCandidate Fallback(string full) =>
        new(Path.GetFileNameWithoutExtension(full), Path.GetFileNameWithoutExtension(full), full);

    private static int? FrontmostPid()
    {
        IntPtr list = IntPtr.Zero, kLayer = IntPtr.Zero, kPid = IntPtr.Zero;
        try
        {
            list = CGWindowListCopyWindowInfo(OnScreenOnly | ExcludeDesktopElements, 0);
            if (list == IntPtr.Zero) return null;
            kLayer = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowLayer", Utf8);
            kPid = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowOwnerPID", Utf8);
            int me = Environment.ProcessId;
            nint n = CFArrayGetCount(list);
            for (nint i = 0; i < n; i++)
            {
                var d = CFArrayGetValueAtIndex(list, i);
                if (!TryInt(d, kLayer, out var layer) || layer != 0) continue;
                if (!TryInt(d, kPid, out var pid) || pid == me) continue;
                return pid;
            }
            return null;
        }
        finally
        {
            foreach (var cf in new[] { list, kLayer, kPid })
                if (cf != IntPtr.Zero) CFRelease(cf);
        }
    }

    private static string? BundleId(int pid)
    {
        EnsureAppKit();
        var cls = ObjCGuard.Class("NSRunningApplication");
        if (cls == IntPtr.Zero || !ObjCGuard.RespondsTo(cls, "runningApplicationWithProcessIdentifier:"))
            return null;
        var app = SendInt(cls, ObjCGuard.Sel("runningApplicationWithProcessIdentifier:"), pid);
        return app == IntPtr.Zero ? null : ReadNsString(app, "bundleIdentifier");
    }

    private static void EnsureAppKit()
    {
        if (_appKitLoaded || !OperatingSystem.IsMacOS()) return;
        NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");
        _appKitLoaded = true;
    }

    private static bool TryInt(IntPtr dict, IntPtr key, out int value)
    {
        value = 0;
        var v = CFDictionaryGetValue(dict, key);
        return v != IntPtr.Zero && CFNumberGetValue(v, CFNumberSInt32Type, out value);
    }

    private static string? ReadNsString(IntPtr obj, string getter)
    {
        if (obj == IntPtr.Zero || !ObjCGuard.RespondsTo(obj, getter)) return null;
        return NsStringToUtf8(Send0(obj, ObjCGuard.Sel(getter)));
    }

    private static string? ReadInfo(IntPtr bundle, string key)
    {
        if (!ObjCGuard.RespondsTo(bundle, "objectForInfoDictionaryKey:")) return null;
        var keyStr = NsString(key);
        if (keyStr == IntPtr.Zero) return null;
        return NsStringToUtf8(SendObj(bundle, ObjCGuard.Sel("objectForInfoDictionaryKey:"), keyStr));
    }

    private static string? NsStringToUtf8(IntPtr str)
    {
        if (str == IntPtr.Zero || !ObjCGuard.RespondsTo(str, ObjCSelectors.Utf8String)) return null;
        var utf8 = Send0(str, ObjCGuard.Sel(ObjCSelectors.Utf8String));
        return utf8 == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(utf8);
    }

    private static IntPtr NsString(string s)
    {
        var cls = ObjCGuard.Class("NSString");
        if (cls == IntPtr.Zero || !ObjCGuard.RespondsTo(cls, ObjCSelectors.StringWithUtf8String))
            return IntPtr.Zero;
        return SendUtf8(cls, ObjCGuard.Sel(ObjCSelectors.StringWithUtf8String), s);
    }
}
