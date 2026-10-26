using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenTabletArtist.Domain;

/// <summary>Version 7+: check for the official Wacom driver on macOS. Paths and names from Wacom's own
/// removal article (support.wacom.com, article 1500006264541). Detects the live driver, and can remove only
/// safe leftovers (empty Applications folders without Wacom Tablet Utility, plus known leftover paths). It
/// never deletes Wacom Tablet Utility.app and never kills processes.</summary>
public static class WacomDriverCheck
{
    public const string UtilityApp = "/Applications/Wacom Tablet.localized/Wacom Tablet Utility.app";
    public const string UtilityAppAlt = "/Applications/Wacom Tablet/Wacom Tablet Utility.app";
    public const string ApplicationsLocalized = "/Applications/Wacom Tablet.localized";
    public const string ApplicationsPlain = "/Applications/Wacom Tablet";
    public const string RemovalHelp = "https://support.wacom.com/hc/en-us/articles/1500006264541";
    public const string Warning = "The Wacom driver is still installed. It can stop this app from using your tablet. Remove it first with Wacom's own uninstaller, then restart your Mac.";

    /// <summary>The installed driver itself: if any of these exist, the driver can still load.</summary>
    public static readonly string[] Paths =
    {
        ApplicationsLocalized,
        ApplicationsPlain,
        UtilityApp,
        "/Library/PreferencePanes/WacomTablet.prefpane",
        "/Library/Extensions/WacomTablet.kext",
        "/Library/Extensions/Wacom Tablet.kext",
    };

    /// <summary>Launch files: com.wacom.* here start the driver at login.</summary>
    public static readonly string[] Folders = { "/Library/LaunchAgents", "/Library/LaunchDaemons" };

    /// <summary>Harmless leftovers after Wacom's uninstaller: safe for the in-app cleaner to try.</summary>
    public static readonly string[] LeftoverPaths =
    {
        "/Library/Application Support/Tablet",
        "/Library/Preferences/Tablet",
        "/Library/Frameworks/WacomMultiTouch.framework",
    };
    public static readonly string[] LeftoverFolders = { "/Library/PrivilegedHelperTools" };

    /// <summary>Wacom's own driver processes, matched by exact name. Never generic names like
    /// "TabletDriver", which could match other drivers (or ours).</summary>
    public static readonly string[] Processes = { "WacomTabletDriver", "WacomTouchDriver", "Wacom Center" };

    /// <summary>Applications trees that Find watches. Removable only when Utility.app is absent.</summary>
    public static readonly string[] ApplicationsTrees = { ApplicationsLocalized, ApplicationsPlain };

    static bool IsWacomLaunchFile(string n) => System.IO.Path.GetFileName(n).StartsWith("com.wacom.", StringComparison.OrdinalIgnoreCase);

    static string UtilityInside(string applicationsTree) =>
        System.IO.Path.Combine(applicationsTree, "Wacom Tablet Utility.app");

    /// <summary>True when Wacom Tablet Utility.app is present (localised or plain Applications path).</summary>
    public static bool HasUtility(Func<string, bool> exists) =>
        exists(UtilityApp) || exists(UtilityAppAlt);

    /// <summary>The driver is installed or running: a strong warning. Pure apart from the look-ups.</summary>
    public static IReadOnlyList<string> Find(Func<string, bool> exists, Func<string, IEnumerable<string>> list, IEnumerable<string> running)
    {
        var found = new List<string>();
        found.AddRange(Paths.Where(exists));
        foreach (var f in Folders) found.AddRange(list(f).Where(IsWacomLaunchFile));
        var names = running.ToHashSet(StringComparer.OrdinalIgnoreCase);
        found.AddRange(Processes.Where(names.Contains).Select(p => "process: " + p));
        return found;
    }

    /// <summary>Only leftovers remain: a gentle note.</summary>
    public static IReadOnlyList<string> FindLeftovers(Func<string, bool> exists, Func<string, IEnumerable<string>> list)
    {
        var found = new List<string>();
        found.AddRange(LeftoverPaths.Where(exists));
        foreach (var f in LeftoverFolders) found.AddRange(list(f).Where(IsWacomLaunchFile));
        return found;
    }

    /// <summary>Live driver pieces that need Wacom Tablet Utility (or an admin cleaner): preference pane,
    /// kexts, launch agents/daemons, running processes, or an Applications tree that still has Utility.</summary>
    public static IReadOnlyList<string> ClassifyLive(Func<string, bool> exists, Func<string, IEnumerable<string>> list, IEnumerable<string> running)
    {
        var live = new List<string>();
        foreach (var p in Paths)
        {
            if (!exists(p)) continue;
            if (ApplicationsTrees.Contains(p))
            {
                if (exists(UtilityInside(p))) live.Add(p);
                continue;
            }
            // Utility.app itself, preference pane, kexts
            live.Add(p);
        }
        if (exists(UtilityAppAlt) && !live.Contains(UtilityAppAlt)) live.Add(UtilityAppAlt);
        foreach (var f in Folders) live.AddRange(list(f).Where(IsWacomLaunchFile));
        var names = running.ToHashSet(StringComparer.OrdinalIgnoreCase);
        live.AddRange(Processes.Where(names.Contains).Select(p => "process: " + p));
        return live;
    }

    /// <summary>Safe for the app to delete without touching Utility.app or killing processes: Applications
    /// trees that exist but do not contain Utility, plus known leftover paths / PrivilegedHelperTools.</summary>
    public static IReadOnlyList<string> ClassifyRemovable(Func<string, bool> exists, Func<string, IEnumerable<string>> list)
    {
        var removable = new List<string>();
        foreach (var tree in ApplicationsTrees)
        {
            if (exists(tree) && !exists(UtilityInside(tree)))
                removable.Add(tree);
        }
        removable.AddRange(FindLeftovers(exists, list));
        return removable;
    }

    public readonly record struct RemoveResult(IReadOnlyList<string> Removed, IReadOnlyList<string> Failed);

    /// <summary>Deletes only <see cref="ClassifyRemovable"/> paths. Never deletes Utility.app. Injected
    /// delete keeps this unit-testable; failures (e.g. permission) are listed, not thrown.</summary>
    public static RemoveResult TryRemoveRemovable(
        Func<string, bool> exists,
        Func<string, IEnumerable<string>> list,
        Action<string> deletePath)
    {
        var removed = new List<string>();
        var failed = new List<string>();
        foreach (var path in ClassifyRemovable(exists, list))
        {
            if (IsProtectedUtility(path)) { failed.Add(path); continue; }
            try
            {
                deletePath(path);
                removed.Add(path);
            }
            catch
            {
                failed.Add(path);
            }
        }
        return new RemoveResult(removed, failed);
    }

    static bool IsProtectedUtility(string path)
    {
        var name = System.IO.Path.GetFileName(path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
        return name.Equals("Wacom Tablet Utility.app", StringComparison.OrdinalIgnoreCase);
    }

    public const string LeftoverNote = "Some files from the Wacom driver are still on this Mac. They're usually harmless. If the tablet misbehaves, see Wacom's removal guide.";
    public const string NoUninstaller = "Wacom's uninstaller isn't on this Mac, so the driver may already be removed. Opening Wacom's removal guide instead.";

    static bool Exists(string p) => Directory.Exists(p) || File.Exists(p);
    static IEnumerable<string> List(string d) { try { return Directory.Exists(d) ? Directory.EnumerateFileSystemEntries(d).ToList() : new List<string>(); } catch { return new List<string>(); } }

    static void DeletePath(string p)
    {
        if (IsProtectedUtility(p)) throw new InvalidOperationException("Refusing to delete Wacom Tablet Utility.app.");
        if (Directory.Exists(p)) Directory.Delete(p, recursive: true);
        else if (File.Exists(p)) File.Delete(p);
    }

    public static IReadOnlyList<string> LeftoversOnThisMac()
        => OperatingSystem.IsMacOS() ? FindLeftovers(Exists, List) : Array.Empty<string>();

    public static bool HasUtilityOnThisMac()
        => OperatingSystem.IsMacOS() && HasUtility(Exists);

    public static IReadOnlyList<string> RemovableOnThisMac()
        => OperatingSystem.IsMacOS() ? ClassifyRemovable(Exists, List) : Array.Empty<string>();

    public static IReadOnlyList<string> LiveOnThisMac()
        => OperatingSystem.IsMacOS()
            ? ClassifyLive(Exists, List, System.Diagnostics.Process.GetProcesses().Select(p => { try { return p.ProcessName; } catch { return ""; } }).ToList())
            : Array.Empty<string>();

    /// <summary>Removes only safe leftovers on this Mac. Returns what went and what needed permission.</summary>
    public static RemoveResult RemoveRemovableOnThisMac()
        => OperatingSystem.IsMacOS()
            ? TryRemoveRemovable(Exists, List, DeletePath)
            : new RemoveResult(Array.Empty<string>(), Array.Empty<string>());

    /// <summary>Checks this Mac. Empty off macOS.</summary>
    public static IReadOnlyList<string> FindOnThisMac()
    {
        if (!OperatingSystem.IsMacOS()) return Array.Empty<string>();
        return Find(Exists, List,
            System.Diagnostics.Process.GetProcesses().Select(p => { try { return p.ProcessName; } catch { return ""; } }).ToList());
    }
}
