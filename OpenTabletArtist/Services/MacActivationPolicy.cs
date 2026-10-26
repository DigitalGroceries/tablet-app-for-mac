using System;
using System.Runtime.InteropServices;

namespace OpenTabletArtist.Services;

/// <summary>
/// Version 7 test 9: show or hide the Dock icon at runtime. Accessory = menu-bar (pen) only, no Dock;
/// Regular = normal app with a Dock icon while a window is open. Prefer this over a permanent
/// <c>LSUIElement</c> in Info.plist, which would hide the Dock even when the settings window is open.
/// Mirrors OpenTabletDriver's Mac Eto FormHandler (Accessory ↔ Regular).
/// </summary>
internal static class MacActivationPolicy
{
    // NSApplicationActivationPolicy: Regular = 0, Accessory = 1, Prohibited = 2
    public const nint Regular = 0;
    public const nint Accessory = 1;

    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr r, IntPtr s);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SendSetPolicy(IntPtr r, IntPtr s, nint policy);

    private static nint? _current;

    /// <summary>Hide the Dock icon (tray / login quiet start).</summary>
    public static void SetAccessory() => Set(Accessory, "Accessory");

    /// <summary>Show the Dock icon (settings window about to appear).</summary>
    public static void SetRegular() => Set(Regular, "Regular");

    private static void Set(nint policy, string label)
    {
        if (!OperatingSystem.IsMacOS()) return;
        if (_current == policy) return;
        try
        {
            var cls = ObjCGuard.Class("NSApplication");
            if (cls == IntPtr.Zero || !ObjCGuard.RespondsTo(cls, ObjCSelectors.SharedApplication))
            {
                AppLog.Warn("Dock: NSApplication.sharedApplication unavailable; left unchanged.");
                return;
            }
            var app = Send(cls, ObjCGuard.Sel(ObjCSelectors.SharedApplication));
            if (app == IntPtr.Zero || !ObjCGuard.RespondsTo(app, ObjCSelectors.SetActivationPolicy))
            {
                AppLog.Warn("Dock: setActivationPolicy: unavailable; left unchanged.");
                return;
            }
            if (!SendSetPolicy(app, ObjCGuard.Sel(ObjCSelectors.SetActivationPolicy), policy))
            {
                AppLog.Warn($"Dock: setActivationPolicy {label} returned false.");
                return;
            }
            _current = policy;
            AppLog.Info($"Dock: activation policy {label}");
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Dock: could not set activation policy {label}.", ex);
        }
    }
}
