using System;
using System.Collections.Generic;
using System.Linq;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;

namespace OpenTabletArtist.Domain;

/// <summary>
/// Discussion #31 (best attempt): keeps the touch trackpad filter
/// (<c>OpenTabletArtist.Dynamics.TouchTrackpadFilter</c>) in the profile of tablets where finger touch
/// should drive the cursor. Only the PTH-651 for now, as that's the tablet we were asked about; others
/// with touch can be added once someone tests them.
/// </summary>
public static class TouchTrackpad
{
    public const string FilterTypeName = "OpenTabletArtist.Dynamics.TouchTrackpadFilter";

    public static readonly IReadOnlySet<string> Tablets = new HashSet<string>(StringComparer.Ordinal)
    {
        "Wacom PTH-651",
    };

    public static bool Wanted(Profile profile) => Tablets.Contains(profile.Tablet);

    /// <summary>Adds the filter (enabled, once) to a wanted tablet's profile. Leaves it alone if the user
    /// switched it off. Never touches other filters. Returns true if it changed anything.</summary>
    public static bool SyncFilter(Profile profile)
    {
        if (profile.Filters == null || !Wanted(profile)) return false;
        var existing = profile.Filters.OfType<PluginSettingStore>().Where(f => f.Path == FilterTypeName).ToList();
        if (existing.Count == 0)
        {
            var filter = AuxKeyBinding.NewStore(FilterTypeName);
            filter.Enable = true;
            profile.Filters.Add(filter);
            return true;
        }
        bool changed = false;
        foreach (var extra in existing.Skip(1)) { profile.Filters.Remove(extra); changed = true; }
        return changed;
    }

    /// <summary>True when finger touch drives the cursor for this profile: a wanted tablet whose filter is
    /// on (or not written yet, as the next save adds it switched on).</summary>
    public static bool IsOn(Profile? profile)
    {
        if (profile == null || !Wanted(profile)) return false;
        var store = profile.Filters?.OfType<PluginSettingStore>().FirstOrDefault(f => f.Path == FilterTypeName);
        return store == null || store.Enable;
    }

    /// <summary>The tablet page's on/off switch. Mutates <paramref name="settings"/>; no-op for other tablets.</summary>
    public static void Write(Settings? settings, string tabletName, bool on)
    {
        var profile = settings?.Profiles?.FirstOrDefault(p => p.Tablet == tabletName);
        if (profile?.Filters == null || !Wanted(profile)) return;
        SyncFilter(profile);
        foreach (var f in profile.Filters.OfType<PluginSettingStore>().Where(f => f.Path == FilterTypeName)) f.Enable = on;
    }

    /// <summary>Item 6 (PR #38): true when a connected wanted tablet's profile has no touch trackpad filter
    /// yet, so the app should add it and apply once without waiting for the user to change something.
    /// A filter the user switched off is present, so it is never switched back on here.</summary>
    public static bool NeedsAutoAdd(Settings? settings, IEnumerable<string> connectedTablets)
    {
        if (settings?.Profiles == null) return false;
        var connected = new HashSet<string>(connectedTablets, StringComparer.Ordinal);
        return settings.Profiles.Any(p => p != null && connected.Contains(p.Tablet) && Wanted(p) && p.Filters != null
            && !p.Filters.OfType<PluginSettingStore>().Any(f => f.Path == FilterTypeName));
    }

    /// <summary>Adds the filter to every connected wanted tablet that lacks it. Returns true if anything changed.</summary>
    public static bool AutoAdd(Settings? settings, IEnumerable<string> connectedTablets)
    {
        if (!NeedsAutoAdd(settings, connectedTablets)) return false;
        var connected = new HashSet<string>(connectedTablets, StringComparer.Ordinal);
        bool changed = false;
        foreach (var p in settings!.Profiles.Where(p => p != null && connected.Contains(p.Tablet)))
            changed |= SyncFilter(p);
        return changed;
    }
}
