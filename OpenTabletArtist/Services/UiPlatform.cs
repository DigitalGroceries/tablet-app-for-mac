using System;

namespace OpenTabletArtist.Services;

/// <summary>Version 29 (Setting search): the system the pages are laid out for. It is the real system,
/// except while the setting-search index is built: that walk lays every page out as on a Mac on any
/// machine (the build boxes run Linux too), so the index lists what a Mac user sees.
/// Only page content (which cards and tabs show) reads this; code that calls system functions keeps
/// asking <see cref="OperatingSystem"/>.</summary>
public static class UiPlatform
{
    public static bool IsMac { get; internal set; } = OperatingSystem.IsMacOS();
    public static bool IsLinux { get; internal set; } = OperatingSystem.IsLinux();
    public static bool IsWindows { get; internal set; } = OperatingSystem.IsWindows();

    /// <summary>True during <see cref="PretendMac"/>: a card that depends on a macOS service (Start at login)
    /// shows as on a Mac where the service works, without asking the service.</summary>
    public static bool IsPretend { get; private set; }

    /// <summary>Lay pages out as on a Mac until the returned object is disposed (the index walk).</summary>
    internal static IDisposable PretendMac()
    {
        var (mac, linux, windows, pretend) = (IsMac, IsLinux, IsWindows, IsPretend);
        IsMac = true; IsLinux = false; IsWindows = false; IsPretend = true;
        return new Restore(() => { IsMac = mac; IsLinux = linux; IsWindows = windows; IsPretend = pretend; });
    }

    sealed class Restore(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }
}
