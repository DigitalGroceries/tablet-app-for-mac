using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using OpenTabletArtist.Domain;
using OpenTabletDriver.Desktop.Profiles;

namespace OpenTabletArtist.Services;

/// <summary>
/// Persists per-application binding tables in <see cref="AppSettings"/> (not in OTD's settings.json).
/// OTD's live profile stays the All / All-other defaults; overlays are applied at runtime.
/// </summary>
public static class PerAppBindingStore
{
    private static string Key(string tablet) => $"PerAppTables:{tablet}";

    public static IReadOnlyList<PerAppProfile> List(string tablet)
    {
        if (string.IsNullOrEmpty(tablet)) return Array.Empty<PerAppProfile>();
        var raw = AppSettings.Get(Key(tablet));
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<PerAppProfile>();
        try
        {
            var list = JsonConvert.DeserializeObject<List<PerAppProfile>>(raw);
            return list?.Where(p => !string.IsNullOrWhiteSpace(p.BundleId)).ToList()
                   ?? (IReadOnlyList<PerAppProfile>)Array.Empty<PerAppProfile>();
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Couldn't read per-app binding tables for {tablet}.", ex);
            return Array.Empty<PerAppProfile>();
        }
    }

    public static PerAppProfile? Find(string tablet, string bundleId) =>
        List(tablet).FirstOrDefault(p =>
            string.Equals(p.BundleId, bundleId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Clone <paramref name="fromAll"/> into a new profile for <paramref name="bundleId"/>.
    /// No-op (returns existing) when that bundle id is already listed.</summary>
    public static PerAppProfile Add(string tablet, string bundleId, string displayName, string appPath,
        BindingSettings fromAll)
    {
        var list = List(tablet).ToList();
        var existing = list.FirstOrDefault(p =>
            string.Equals(p.BundleId, bundleId, StringComparison.OrdinalIgnoreCase));
        if (existing != null) return existing;

        var profile = new PerAppProfile
        {
            BundleId = bundleId,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? bundleId : displayName,
            AppPath = appPath ?? "",
            Bindings = CloneBindings(fromAll),
        };
        // TASKS #15: a new app follows All's Double Click Fix until the user gives it its own value.
        profile.Bindings.DoubleClickFix = null;
        list.Add(profile);
        Save(tablet, list);
        return profile;
    }

    public static bool Remove(string tablet, string bundleId)
    {
        var list = List(tablet).ToList();
        var n = list.RemoveAll(p =>
            string.Equals(p.BundleId, bundleId, StringComparison.OrdinalIgnoreCase));
        if (n == 0) return false;
        Save(tablet, list);
        return true;
    }

    public static void SaveBindings(string tablet, string bundleId, BindingSettings bindings)
    {
        var list = List(tablet).ToList();
        var i = list.FindIndex(p =>
            string.Equals(p.BundleId, bundleId, StringComparison.OrdinalIgnoreCase));
        if (i < 0) return;
        list[i].Bindings = CloneBindings(bindings);
        Save(tablet, list);
    }

    static string DoubleClickFixMigratedKey(string tablet) => $"DoubleClickFixPerApp:{tablet}";

    /// <summary>TASKS #15: up to Version 25 the Double Click Fix slider only ever wrote All, so the value an
    /// app or extra pen holds is just a copy of All taken when the app was added or the pen first seen.
    /// Clear those copies once per tablet, so the app (or pen) follows All until the user gives it its own
    /// value. Returns true when it ran.</summary>
    public static bool ClearCopiedDoubleClickFixOnce(string tablet)
    {
        if (string.IsNullOrEmpty(tablet) || AppSettings.Get(DoubleClickFixMigratedKey(tablet)) == "1") return false;
        foreach (var app in List(tablet).Where(a => a.Bindings.DoubleClickFix != null))
        {
            var copy = CloneBindings(app.Bindings);
            copy.DoubleClickFix = null;
            SaveBindings(tablet, app.BundleId, copy);
        }
        foreach (var pen in PerPenStore.List(tablet).Where(p => p.Bindings.DoubleClickFix != null))
        {
            var copy = CloneBindings(pen.Bindings);
            copy.DoubleClickFix = null;
            PerPenStore.SaveBindings(tablet, pen.Key, copy);
        }
        AppSettings.Set(DoubleClickFixMigratedKey(tablet), "1");
        return true;
    }

    /// <summary>Bindings for the frontmost app, or null when All / All-other should apply.</summary>
    public static BindingSettings? Resolve(string tablet, string? frontmostBundleId)
    {
        if (string.IsNullOrWhiteSpace(frontmostBundleId)) return null;
        return Find(tablet, frontmostBundleId)?.Bindings;
    }

    public static BindingSettings CloneBindings(BindingSettings source) =>
        JsonConvert.DeserializeObject<BindingSettings>(JsonConvert.SerializeObject(source))
        ?? new BindingSettings();

    private static void Save(string tablet, List<PerAppProfile> list) =>
        AppSettings.Set(Key(tablet), JsonConvert.SerializeObject(list));
}
