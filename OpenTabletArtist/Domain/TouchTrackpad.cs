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
/// should drive the cursor. The PTH-651 (Discussion #31) and, from Version 28 (TASKS #77, Daniel's CTH-461), the
/// Bamboo Pen &amp; Touch models whose 20-byte touch report the driver now reads (CTH-460, CTH-461, CTH-661).
/// Version 28: the filter stays in the pipeline and its "TouchOn" setting says whether touch moves the pointer, so
/// a Touch on/off key (<see cref="TouchToggleBinding"/>) can turn touch on even when the switch is off.
/// The new models start OFF (agreed with Devin): until now their touch did nothing, so updating must not suddenly
/// move the pointer under a resting hand. The PTH-651 keeps starting on.
/// </summary>
public static class TouchTrackpad
{
    public const string FilterTypeName = "OpenTabletArtist.Dynamics.TouchTrackpadFilter";

    public static readonly IReadOnlySet<string> Tablets = new HashSet<string>(StringComparer.Ordinal)
    {
        "Wacom PTH-651",
        "Wacom CTH-460",
        "Wacom CTH-461",
        "Wacom CTH-661",
    };

    /// <summary>The filter setting that says whether finger touch moves the pointer (the plugin's TouchOn).</summary>
    public const string TouchOnProperty = "TouchOn";

    /// <summary>Version 29 (#90, Daniel): the plugin's Touch speed (1-100, 50 = Version 28's speed) and Scroll momentum.</summary>
    public const string TouchSpeedProperty = "TouchSpeed";
    public const string ScrollMomentumProperty = "ScrollMomentum";
    public const int DefaultTouchSpeed = 50;

    private static PluginSettingStore? Store(Profile? profile) =>
        profile?.Filters?.OfType<PluginSettingStore>().FirstOrDefault(f => f.Path == FilterTypeName);

    private static T Read<T>(Profile? profile, string property, T fallback)
    {
        var setting = Store(profile)?.Settings?.FirstOrDefault(x => x.Property == property);
        if (setting?.Value == null) return fallback;
        try { return setting.GetValue<T>(); }
        catch (Exception) { return fallback; }
    }

    /// <summary>The Touch speed stored for this tablet, 1-100 (50 when none is stored yet).</summary>
    public static int ReadSpeed(Profile? profile) => Math.Clamp(Read(profile, TouchSpeedProperty, DefaultTouchSpeed), 1, 100);

    /// <summary>Whether two-finger scrolling glides on after the fingers lift (off when nothing is stored).</summary>
    public static bool ReadMomentum(Profile? profile) => Read(profile, ScrollMomentumProperty, false);

    private static void Set(PluginSettingStore store, string property, object value)
    {
        store.Settings ??= new System.Collections.ObjectModel.ObservableCollection<PluginSetting>();
        foreach (var old in store.Settings.Where(x => x.Property == property).ToList()) store.Settings.Remove(old);
        store.Settings.Add(new PluginSetting(property, value));
    }

    /// <summary>The tablet page's Touch speed and Scroll momentum. Mutates <paramref name="settings"/>; adds the filter
    /// (with touch's starting state) if the tablet has none yet; no-op for other tablets.</summary>
    public static void WriteFeel(Settings? settings, string tabletName, int speed, bool momentum)
    {
        var profile = settings?.Profiles?.FirstOrDefault(p => p.Tablet == tabletName);
        if (profile?.Filters == null || !Wanted(profile)) return;
        SyncFilter(profile);
        foreach (var f in profile.Filters.OfType<PluginSettingStore>().Where(f => f.Path == FilterTypeName))
        {
            Set(f, TouchSpeedProperty, Math.Clamp(speed, 1, 100));
            Set(f, ScrollMomentumProperty, momentum);
        }
    }

    /// <summary>Whether touch starts on for a tablet the app hasn't set up yet: only the PTH-651.</summary>
    public static bool StartsOn(string? tabletName) => tabletName == "Wacom PTH-651";

    /// <summary>Pure: the switch's state from the stored filter. Before Version 28 "off" meant the filter itself was
    /// disabled and there was no TouchOn setting, which reads as on.</summary>
    public static bool StoreIsOn(PluginSettingStore store) =>
        store.Enable && (store.Settings?.FirstOrDefault(x => x.Property == TouchOnProperty)?.GetValue<bool>() ?? true);

    private static void SetTouchOn(PluginSettingStore store, bool on)
    {
        store.Enable = true;   // the filter always runs, so a Touch on/off key works even when the switch is off
        store.Settings ??= new System.Collections.ObjectModel.ObservableCollection<PluginSetting>();
        foreach (var old in store.Settings.Where(x => x.Property == TouchOnProperty).ToList()) store.Settings.Remove(old);
        store.Settings.Add(new PluginSetting(TouchOnProperty, on));
    }

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
            SetTouchOn(filter, StartsOn(profile.Tablet));
            profile.Filters.Add(filter);
            return true;
        }
        bool changed = false;
        foreach (var extra in existing.Skip(1)) { profile.Filters.Remove(extra); changed = true; }
        return changed;
    }

    /// <summary>True when finger touch drives the cursor for this profile: a wanted tablet whose filter is
    /// on (or not written yet and the tablet starts on, as the next save adds it that way).</summary>
    /// With an outside daemon (<paramref name="appOwnedDaemon"/> false) the app never adds the filter, so a
    /// missing filter reads as off there.
    public static bool IsOn(Profile? profile, bool appOwnedDaemon = true)
    {
        if (profile == null || !Wanted(profile)) return false;
        var store = profile.Filters?.OfType<PluginSettingStore>().FirstOrDefault(f => f.Path == FilterTypeName);
        return store == null ? appOwnedDaemon && StartsOn(profile.Tablet) : StoreIsOn(store);
    }

    /// <summary>The tablet page's on/off switch. Mutates <paramref name="settings"/>; no-op for other tablets.</summary>
    public static void Write(Settings? settings, string tabletName, bool on)
    {
        var profile = settings?.Profiles?.FirstOrDefault(p => p.Tablet == tabletName);
        if (profile?.Filters == null || !Wanted(profile)) return;
        SyncFilter(profile);
        foreach (var f in profile.Filters.OfType<PluginSettingStore>().Where(f => f.Path == FilterTypeName)) SetTouchOn(f, on);
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
