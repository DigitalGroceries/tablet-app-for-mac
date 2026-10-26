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
