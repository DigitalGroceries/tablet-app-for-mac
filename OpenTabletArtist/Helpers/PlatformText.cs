using System;

namespace OpenTabletArtist.Helpers;

/// <summary>Version 1: platform words and flags for XAML, so a Mac never sees Windows-only text.</summary>
public static class PlatformText
{
    public static bool IsWindows => OperatingSystem.IsWindows();
    public static bool IsMac => OperatingSystem.IsMacOS();
    /// <summary>The Windows key's counterpart: Command on a Mac.</summary>
    public static string SuperKey => OperatingSystem.IsMacOS() ? "Cmd" : OperatingSystem.IsWindows() ? "Win" : "Super";
    /// <summary>The system display settings, by their own name.</summary>
    public static string DisplaySettingsTip => OperatingSystem.IsMacOS() ? "Open System Settings › Displays"
        : OperatingSystem.IsWindows() ? "Open Windows Display Settings" : "Open the system display settings";
    public static string BreakPenLabel => OperatingSystem.IsWindows() ? "Turn off Windows Ink + tip + pressure + tilt" : "Turn off pen tip, pressure and tilt";
    /// <summary>Display name of a plugin folder. The Windows Ink plugin is shown by what it is for.</summary>
    public static string PluginDisplayName(string folderName) =>
        folderName.Equals("OpenTabletArtistDynamics", StringComparison.OrdinalIgnoreCase) ? "Tablet App for Mac plugin"
        : !OperatingSystem.IsWindows() && folderName.Equals("Windows Ink", StringComparison.OrdinalIgnoreCase)
            ? "Pressure output" : folderName;
    /// <summary>A short note under a plugin's name, or null.</summary>
    public static string? PluginNote(string folderName) =>
        !OperatingSystem.IsWindows() && folderName.Equals("Windows Ink", StringComparison.OrdinalIgnoreCase)
            ? "Not used on macOS. The app sends pressure and tilt itself." : null;
}
