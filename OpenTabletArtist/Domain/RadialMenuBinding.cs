using System;
using System.Collections.Generic;
using System.Linq;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;

namespace OpenTabletArtist.Domain;

/// <summary>
/// App side of the radial menu (TASKS #42): a pen button or ExpressKey binding whose "Menu" setting holds the
/// whole menu as JSON (<see cref="RadialMenuJson"/>). Because the menu is part of the binding, it is saved with
/// the tablet's settings, follows per-application and per-pen tables, Save as default, Revert and the settings
/// backup with no store of its own. Plugin types <c>OpenTabletArtist.Dynamics.RadialMenuBinding</c> and
/// <c>RadialMenuFilter</c> (the filter keeps the pen tip from the app under the menu).
/// </summary>
public static class RadialMenuBinding
{
    public const string BindingTypeName = "OpenTabletArtist.Dynamics.RadialMenuBinding";
    public const string FilterTypeName = "OpenTabletArtist.Dynamics.RadialMenuFilter";
    public const string MenuProperty = "Menu";

    public static bool Is(PluginSettingStore? store) => store?.Path == BindingTypeName;

    public static PluginSettingStore Make(RadialMenuDef? menu = null)
    {
        var store = AuxKeyBinding.NewStore(BindingTypeName);
        store.Settings.Add(new PluginSetting(MenuProperty, RadialMenuJson.Serialize(menu ?? RadialMenuDefaults.Create(Services.Localizer.Te))));
        return store;
    }

    public static PluginSettingStore Make(string? menuJson) => Make(RadialMenuJson.Parse(menuJson));

    /// <summary>The menu in a radial-menu binding (the default menu when it has none or it can't be read).</summary>
    public static RadialMenuDef ReadMenu(PluginSettingStore? store) =>
        RadialMenuJson.Parse(ReadMenuJson(store));

    public static string? ReadMenuJson(PluginSettingStore? store) =>
        Is(store) ? store!.Settings?.FirstOrDefault(s => s.Property == MenuProperty)?.GetValue<string>() : null;

    public static IEnumerable<PluginSettingStore?> AllBindings(Profile profile) =>
        PenScrollBinding.AllBindings(profile);

    public static bool IsUsed(Profile profile) => AllBindings(profile).Any(Is);

    private static string Canonical(string? json) => RadialMenuJson.Serialize(RadialMenuJson.Parse(json));

    /// <summary>Does a radial-menu binding in this binding table hold exactly this menu?</summary>
    public static bool AnyUses(BindingSettings? bindings, string menuJson)
    {
        var want = Canonical(menuJson);
        return PenScrollBinding.AllBindings(bindings).Any(s => Is(s) && Canonical(ReadMenuJson(s)) == want);
    }

    /// <summary>Give every radial-menu binding that holds <paramref name="oldJson"/> the menu <paramref name="newJson"/>
    /// (the same menu may be on several buttons). Returns how many were changed.</summary>
    public static int ReplaceMenu(BindingSettings? bindings, string oldJson, string newJson)
    {
        var want = Canonical(oldJson);
        var replacement = Canonical(newJson);
        int n = 0;
        foreach (var store in PenScrollBinding.AllBindings(bindings))
        {
            if (!Is(store) || Canonical(ReadMenuJson(store)) != want) continue;
            var old = store!.Settings.FirstOrDefault(s => s.Property == MenuProperty);
            if (old != null) store.Settings.Remove(old);
            store.Settings.Add(new PluginSetting(MenuProperty, replacement));
            n++;
        }
        return n;
    }

    /// <summary>The radial menu filter is in the profile (enabled, once) exactly while a radial-menu binding is.</summary>
    public static bool SyncFilter(Profile profile)
    {
        if (profile.Filters == null) return false;
        var existing = profile.Filters.OfType<PluginSettingStore>().Where(f => f.Path == FilterTypeName).ToList();
        bool changed = false;
        if (IsUsed(profile))
        {
            if (existing.Count == 0)
            {
                var filter = AuxKeyBinding.NewStore(FilterTypeName);
                filter.Enable = true;
                profile.Filters.Add(filter);
                return true;
            }
            foreach (var f in existing.Where(f => !f.Enable)) { f.Enable = true; changed = true; }
            foreach (var extra in existing.Skip(1)) { profile.Filters.Remove(extra); changed = true; }
        }
        else
        {
            foreach (var f in existing) { profile.Filters.Remove(f); changed = true; }
        }
        return changed;
    }

    /// <summary>A short summary for the binding card: "Radial menu: Undo, Redo, Copy…".</summary>
    public static string Describe(RadialMenuDef menu, Func<string, string, string> te)
    {
        var names = menu.Slices.Where(s => s.IsSet && !string.IsNullOrWhiteSpace(s.Label)).Select(s => s.Label).ToList();
        var title = te("radial.title", "Radial menu");
        if (names.Count == 0) return title;
        var shown = string.Join(", ", names.Take(3));
        return $"{title}: {shown}{(names.Count > 3 ? "…" : "")}";
    }
}
