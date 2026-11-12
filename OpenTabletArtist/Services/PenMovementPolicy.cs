using System;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;

namespace OpenTabletArtist.Services;

/// <summary>TASKS #55: the Pen page's Movement choice (Normal (Absolute) / Mouse-like (Relative)) is hidden by
/// default and the app always uses Absolute. A switch in Settings › System shows the choice again. While it is
/// hidden, a saved Relative output mode falls back to Absolute (and is logged). The Relative code stays.</summary>
public static class PenMovementPolicy
{
    public const string SettingKey = "ShowMovementChoice";

    /// <summary>Raised after <see cref="ShowChoice"/> changes.</summary>
    public static event Action? Changed;

    /// <summary>Off by default. On: the Pen page offers Absolute and Relative again.</summary>
    public static bool ShowChoice
    {
        get => AppSettings.Get(SettingKey) == "1";
        set
        {
            if (value == ShowChoice) return;
            AppSettings.Set(SettingKey, value ? "1" : "0");
            AppLog.Info($"Pen movement choice {(value ? "shown" : "hidden")} (Settings).");
            Changed?.Invoke();
        }
    }

    /// <summary>True when an output-mode path is a Relative (Mouse-like) mode.</summary>
    public static bool IsRelativePath(string? path) =>
        !string.IsNullOrEmpty(path) && path.Contains("Relative", StringComparison.OrdinalIgnoreCase);

    /// <summary>The Absolute twin of a Relative output-mode path (WinInkRelativeMode becomes WinInkAbsoluteMode,
    /// RelativeMode becomes AbsoluteMode); any other path is returned unchanged.</summary>
    public static string AbsolutePathFor(string path) =>
        path.Replace("Relative", "Absolute", StringComparison.Ordinal);

    /// <summary>While the choice is hidden: switch every profile in <paramref name="settings"/> that is on a
    /// Relative output mode to the Absolute twin. Returns how many profiles changed (each one is logged).</summary>
    public static int FallBackToAbsolute(Settings settings)
    {
        if (ShowChoice || settings?.Profiles == null) return 0;
        int n = 0;
        foreach (var profile in settings.Profiles)
        {
            var path = profile?.OutputMode?.Path;
            if (profile == null || !IsRelativePath(path)) continue;
            var abs = AbsolutePathFor(path!);
            AppLog.Info($"Saved movement was Relative ({path}); using Normal (Absolute) ({abs}) for '{profile.Tablet}'.");
            profile.OutputMode!.Path = abs;
            n++;
        }
        return n;
    }

    /// <summary>The slider on the Pen page runs 0-10; the driver value runs 0-0.25. 10 is the old 0.25.</summary>
    public const double SmoothingUiMax = 10;

    public static double SmoothingToUi(double internalValue, double internalMax) =>
        Math.Clamp(Math.Round(internalValue / internalMax * SmoothingUiMax, 1), 0, SmoothingUiMax);

    public static double SmoothingFromUi(double ui, double internalMax) =>
        Math.Clamp(ui, 0, SmoothingUiMax) / SmoothingUiMax * internalMax;
}
