using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTabletArtist.Services;

/// <summary>
/// TASKS #17 (Kert, 8 Nov): filters from other OpenTabletDriver plugins (for example Devocub Antichatter or
/// RadialFollow smoothing) that the user turned on in this app's Filters tab. The app still switches off any
/// other plugin filter it finds (left on by a standalone OpenTabletDriver, for instance), so the pen behaves the
/// same unless the user chose otherwise. Stored as one app setting: filter type paths separated by "|".
/// </summary>
public static class UserFilterChoices
{
    public const string SettingKey = "UserFilters";

    /// <summary>Tests replace the store.</summary>
    internal static Func<string?> Read { get; set; } = () => AppSettings.Get(SettingKey);
    internal static Action<string> Write { get; set; } = v => AppSettings.Set(SettingKey, v);

    public static IReadOnlySet<string> Allowed =>
        new HashSet<string>((Read() ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.Ordinal);

    public static bool IsAllowed(string? path) => !string.IsNullOrEmpty(path) && Allowed.Contains(path);

    public static void Set(string path, bool on)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var set = new HashSet<string>(Allowed, StringComparer.Ordinal);
        if (on) set.Add(path); else set.Remove(path);
        Write(string.Join("|", set.OrderBy(p => p, StringComparer.Ordinal)));
    }
}
