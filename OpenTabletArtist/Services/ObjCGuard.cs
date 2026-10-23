using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace OpenTabletArtist.Services;

/// <summary>Build Q: every Objective-C selector the app sends, by its real ObjC name. Build P crashed on launch
/// (doesNotRecognizeSelector) by sending "mainApp" — that is the Swift name; the ObjC class method is
/// +[SMAppService mainAppService]. Tests check this table so a Swift-only name can't slip in again.</summary>
public static class ObjCSelectors
{
    public const string MainAppService = "mainAppService";
    public const string Status = "status";
    public const string Register = "registerAndReturnError:";
    public const string Unregister = "unregisterAndReturnError:";
    public const string LocalizedDescription = "localizedDescription";
    public const string Utf8String = "UTF8String";
    public const string SetIgnoresMouseEvents = "setIgnoresMouseEvents:";
    public const string SetCollectionBehavior = "setCollectionBehavior:";
    public const string Window = "window";
    public const string SetLevel = "setLevel:";
    public const string Screen = "screen";
    public const string Frame = "frame";
    public const string SetFrameDisplay = "setFrame:display:";
    public const string AgentServiceWithPlistName = "agentServiceWithPlistName:";
    public const string StringWithUtf8String = "stringWithUTF8String:";

    public static IReadOnlyList<string> All { get; } = new[]
    {
        MainAppService, Status, Register, Unregister, LocalizedDescription, Utf8String, SetIgnoresMouseEvents,
        SetCollectionBehavior, Window, SetLevel, Screen, Frame, SetFrameDisplay, AgentServiceWithPlistName, StringWithUtf8String,
    };
}

/// <summary>Build Q: check before sending. <see cref="RespondsTo"/> works for instances and for classes alike:
/// object_getClass of a class object is its metaclass, which holds the class methods.</summary>
public static class ObjCGuard
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    [DllImport(ObjC)] private static extern IntPtr object_getClass(IntPtr obj);
    [DllImport(ObjC)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool class_respondsToSelector(IntPtr cls, IntPtr sel);
    [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC)] private static extern IntPtr objc_getClass(string name);

    public static IntPtr Sel(string name) => sel_registerName(name);
    public static IntPtr Class(string name) => objc_getClass(name);

    /// <summary>True when <paramref name="receiver"/> (an object or a class) responds to <paramref name="selector"/>.
    /// Never throws; a null receiver or any runtime failure answers false.</summary>
    public static bool RespondsTo(IntPtr receiver, string selector)
    {
        if (receiver == IntPtr.Zero || !OperatingSystem.IsMacOS()) return false;
        try
        {
            var cls = object_getClass(receiver);
            if (cls == IntPtr.Zero) return false;
            var ok = class_respondsToSelector(cls, sel_registerName(selector));
            if (!ok) AppLog.Warn($"Objective-C receiver doesn't respond to '{selector}'; skipped.");
            return ok;
        }
        catch { return false; }
    }
}
