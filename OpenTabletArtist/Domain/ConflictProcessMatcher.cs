using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTabletArtist.Domain;

/// <summary>
/// Running-process conflict check (idea and process list from MockTab's ConflictProcessMatcher by Cyzor,
/// GPL-3; reimplemented here). Process-based on purpose: a leftover launchd plist from an uninstalled
/// driver is not a conflict unless the program is actually running. Exact name matches only — prefix
/// matching flagged harmless helpers (an empty name, or "Xencelabs" vs "XencelabsDriver").
/// </summary>
public static class ConflictProcessMatcher
{
    /// <summary>Process names other tablet drivers run under, with a readable label.</summary>
    public static readonly (string Name, string Label)[] CompetingProcesses =
    {
        ("WacomTabletDriver", "Wacom Tablet Driver"),
        ("TabletDriver", "Wacom TabletDriver"),
        ("Wacom_IOManager", "Wacom I/O Manager"),
        ("WacomTabletSpringboard", "Wacom Springboard"),
        ("DataStoreMgr", "Wacom DataStore Manager"),
        ("XencelabsDriver", "Xencelabs Driver"),
    };

    /// <summary>OpenTabletDriver's daemon. This app runs exactly one; a second copy fights over the tablet.</summary>
    public const string OtdDaemonProcess = "OpenTabletDriver.Daemon";

    /// <summary>Labels of the competing processes in <paramref name="liveNames"/> (one name per running
    /// process, so duplicates count). A second OpenTabletDriver daemon is reported as a duplicate.</summary>
    public static IReadOnlyList<string> MatchedLabels(IEnumerable<string?> liveNames)
    {
        var names = liveNames.Where(n => !string.IsNullOrEmpty(n)).Select(n => n!).ToList();
        var set = new HashSet<string>(names, StringComparer.Ordinal);
        var found = CompetingProcesses.Where(p => set.Contains(p.Name)).Select(p => p.Label).ToList();
        if (names.Count(n => n == OtdDaemonProcess) > 1)
            found.Add("OpenTabletDriver Daemon (more than one running)");
        return found;
    }

    /// <summary>The process names behind <see cref="MatchedLabels"/>, for the "Processes:" line.</summary>
    public static IReadOnlyList<string> MatchedNames(IEnumerable<string?> liveNames)
    {
        var names = liveNames.Where(n => !string.IsNullOrEmpty(n)).Select(n => n!).ToList();
        var set = new HashSet<string>(names, StringComparer.Ordinal);
        var found = CompetingProcesses.Where(p => set.Contains(p.Name)).Select(p => p.Name).ToList();
        if (names.Count(n => n == OtdDaemonProcess) > 1) found.Add(OtdDaemonProcess);
        return found;
    }
}
