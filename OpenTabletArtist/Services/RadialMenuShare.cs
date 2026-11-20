using System.Collections.Generic;
using System.Linq;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Services;

/// <summary>
/// V29 #170 (Zak, 20 Nov: "I have a menu in front of me = I should be able to control it the same no matter what app
/// is open"): ONE radial menu and colour palette per tablet, with no switch. Every radial-menu binding in All, in every
/// application's table and in every extra pen's table holds the same menu (slices, size, transparency, palette), so a
/// slider, the pencil or a menu change works the same whatever app is in front. Before this, an application's table
/// kept its own copy of the menu (copied from All when the app was added), so the sliders changed only All's copy.
/// </summary>
public static class RadialMenuShare
{
    /// <summary>The tablet's menu, remembered for when All has no radial-menu button.</summary>
    public static string Key(string tablet) => $"RadialMenuShared:{tablet}";

    /// <summary>Grok's review of #170 (finding 2): set while the live All table still holds an older menu than <see cref="Key"/> (the
    /// driver wasn't there, or the change wasn't adopted); the remembered menu then wins until All has it too.</summary>
    public static string BehindKey(string tablet) => $"RadialMenuShared:AllBehind:{tablet}";

    public static bool AllIsBehind(string tablet) => AppSettings.Get(BehindKey(tablet)) == "true";

    public static void SetAllBehind(string tablet, bool behind)
    {
        if (AllIsBehind(tablet) != behind) AppSettings.Set(BehindKey(tablet), behind ? "true" : "");
    }

    private static readonly System.Threading.AsyncLocal<bool> _restoring = new();

    /// <summary>Grok's review of #170 (finding 1): while open, a binding put back from a kept copy (a key's or ring's own On, All
    /// buttons / Touch Ring On again) is not a menu edit: a radial menu in it gets the tablet's menu, not the other way round.</summary>
    public static bool IsRestoring => _restoring.Value;

    public static System.IDisposable Restoring()
    {
        var before = _restoring.Value;
        _restoring.Value = true;
        return new RestoreFlag(before);
    }

    private sealed class RestoreFlag(bool before) : System.IDisposable
    {
        public void Dispose() => _restoring.Value = before;
    }

    /// <summary>A menu an application's or pen's table held before it was given the shared one (kept once, for undo).</summary>
    public static string ReplacedKey(string tablet, string table) => $"RadialMenuShared:Replaced:{tablet}:{table}";

    public static string Canonical(string? json) => RadialMenuJson.Serialize(RadialMenuJson.Parse(json));

    /// <summary>The menu of the first radial-menu binding in a table, or null when it has none.</summary>
    public static string? FirstMenuJson(BindingSettings? bindings) =>
        PenScrollBinding.AllBindings(bindings).Where(RadialMenuBinding.Is)
            .Select(RadialMenuBinding.ReadMenuJson).FirstOrDefault(j => !string.IsNullOrWhiteSpace(j));

    /// <summary>Does a radial-menu binding in this table hold another menu than <paramref name="json"/>?</summary>
    public static bool Differs(BindingSettings? bindings, string json)
    {
        var want = Canonical(json);
        return PenScrollBinding.AllBindings(bindings)
            .Any(s => RadialMenuBinding.Is(s) && Canonical(RadialMenuBinding.ReadMenuJson(s)) != want);
    }

    /// <summary>Give every radial-menu binding in a table the menu <paramref name="json"/>. Returns how many changed.</summary>
    public static int Unify(BindingSettings? bindings, string json)
    {
        var want = Canonical(json);
        int n = 0;
        foreach (var store in PenScrollBinding.AllBindings(bindings))
        {
            if (!RadialMenuBinding.Is(store) || Canonical(RadialMenuBinding.ReadMenuJson(store)) == want) continue;
            var old = store!.Settings.FirstOrDefault(s => s.Property == RadialMenuBinding.MenuProperty);
            if (old != null) store.Settings.Remove(old);
            store.Settings.Add(new PluginSetting(RadialMenuBinding.MenuProperty, want));
            n++;
        }
        return n;
    }

    /// <summary>The tablet's one menu: All's (<paramref name="all"/>), else the one remembered, else the first
    /// application's or pen's. Null when no table of this tablet has a radial-menu button.</summary>
    public static string? Current(string tablet, BindingSettings? all)
    {
        var saved = AppSettings.Get(Key(tablet));
        if (AllIsBehind(tablet) && !string.IsNullOrWhiteSpace(saved)) return Canonical(saved);
        if (FirstMenuJson(all) is { } fromAll) return Canonical(fromAll);
        if (!string.IsNullOrWhiteSpace(saved)) return Canonical(saved);
        foreach (var app in PerAppBindingStore.List(tablet))
            if (FirstMenuJson(app.Bindings) is { } j) return Canonical(j);
        foreach (var pen in PerPenStore.List(tablet))
            if (FirstMenuJson(pen.Bindings) is { } j) return Canonical(j);
        return null;
    }

    /// <summary>Does any table of this tablet (All, an application's, a pen's) hold <paramref name="json"/>?</summary>
    public static bool AnyTableUses(string tablet, BindingSettings? all, string json) =>
        RadialMenuBinding.AnyUses(all, json)
        || RadialMenuBinding.AnyUses(PerAppBindingRuntime.StashedAllBindings(tablet), json)
        || PerAppBindingStore.List(tablet).Any(a => RadialMenuBinding.AnyUses(a.Bindings, json))
        || PerPenStore.List(tablet).Any(p => RadialMenuBinding.AnyUses(p.Bindings, json));

    /// <summary>Remember <paramref name="json"/> as the tablet's menu and put it in the All stash and in every
    /// application's and pen's table (saved). The live All profile is the caller's (it goes through the driver).
    /// Returns how many tables changed.</summary>
    public static int ShareToStores(string tablet, string json)
    {
        var want = Canonical(json);
        if (AppSettings.Get(Key(tablet)) != want) AppSettings.Set(Key(tablet), want);
        int changed = 0;
        if (PerAppBindingRuntime.StashedAllBindings(tablet) is { } all && Unify(all, want) > 0)
        {
            PerAppBindingRuntime.SyncAllBindings(tablet, all);
            changed++;
        }
        foreach (var app in PerAppBindingStore.List(tablet))
        {
            var copy = PerAppBindingStore.CloneBindings(app.Bindings);
            if (!Differs(copy, want)) continue;
            KeepReplaced(tablet, "app:" + app.BundleId, app.Bindings);
            Unify(copy, want);
            PerAppBindingStore.SaveBindings(tablet, app.BundleId, copy);
            AppLog.Info($"Radial menu: {app.BundleId}'s table now has the one shared menu (#170).");
            changed++;
        }
        foreach (var pen in PerPenStore.List(tablet))
        {
            var copy = PerAppBindingStore.CloneBindings(pen.Bindings);
            if (!Differs(copy, want)) continue;
            KeepReplaced(tablet, "pen:" + pen.Key, pen.Bindings);
            Unify(copy, want);
            PerPenStore.SaveBindings(tablet, pen.Key, copy);
            AppLog.Info($"Radial menu: pen {pen.Key}'s table now has the one shared menu (#170).");
            changed++;
        }
        return changed;
    }

    /// <summary>The first time a table's own menu is replaced by the shared one, keep it (nothing reads it back; it is
    /// there so a user who misses an application's own menu can have it restored by hand).</summary>
    private static void KeepReplaced(string tablet, string table, BindingSettings before)
    {
        var key = ReplacedKey(tablet, table);
        if (!string.IsNullOrEmpty(AppSettings.Get(key))) return;
        if (FirstMenuJson(before) is { } old) AppSettings.Set(key, old);
    }

    /// <summary>
    /// After an edit of one table (<paramref name="before"/> to <paramref name="after"/>): the menu the user gave that
    /// table, which becomes the shared one, or null when the edit didn't change a menu. A binding that already was a
    /// radial menu and now holds another menu is an edit. A button that just became a radial menu counts only when its
    /// menu isn't the default one (the default means "open the menu", so it gets the shared menu instead).
    /// </summary>
    public static string? EditedMenu(IReadOnlyList<string?> before, BindingSettings after, string? sharedBefore,
        string defaultMenu)
    {
        var shared = sharedBefore == null ? null : Canonical(sharedBefore);
        var plain = Canonical(defaultMenu);
        var now = MenusByPosition(after);
        string? fresh = null;
        if (now.Count != before.Count)
        {
            // Grok's review of #170 (finding 3): a binding was added or removed, so positions shifted. Compare what the table holds:
            // a menu it didn't hold before, other than the default menu on a new button.
            var had = new HashSet<string?>(before);
            foreach (var json in now)
                if (json != null && json != shared && !had.Contains(json) && (shared == null || json != plain)) return json;
            return null;
        }
        for (int i = 0; i < now.Count; i++)
        {
            if (now[i] is not { } json || json == shared) continue;
            var was = before[i];
            if (was == json) continue;
            if (was != null) return json;                  // an existing radial menu was changed
            if (shared == null || json != plain) fresh ??= json;   // a new radial button with a menu of its own
        }
        return fresh;
    }

    /// <summary>Each binding position of a table: its radial menu (canonical), or null when it isn't one.</summary>
    public static List<string?> MenusByPosition(BindingSettings? bindings) =>
        PenScrollBinding.AllBindings(bindings)
            .Select(s => RadialMenuBinding.Is(s) ? Canonical(RadialMenuBinding.ReadMenuJson(s)) : null)
            .ToList();
}
