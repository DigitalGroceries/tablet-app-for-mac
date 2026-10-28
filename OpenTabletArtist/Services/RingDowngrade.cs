using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Services;

/// <summary>Version 16 BETA 3: before going back to Version 15 or earlier, every ring-mode table the app keeps is
/// put in the layout that version reads (<see cref="RingModeBinding.ToLegacyLayout"/>): the profiles (All), the
/// All stash, and each app's and each pen's table. Without this, one ring edit in Version 15 reversed every ring
/// mode after updating to 16 again.</summary>
public static class RingDowngrade
{
    public static bool IsNeededFor(int targetVersion) =>
        targetVersion < RingModeBinding.FirstAppVersionWithDirectionVersion;

    private static readonly HashSet<string> PreparedTablets = new(StringComparer.Ordinal);

    /// <summary>Version 17 BETA 2: true once this tablet's tables are in the Version 15 layout and the app is about
    /// to be replaced. Nothing may rewrite them as legacy then (the page did, for the app tile on screen).</summary>
    public static bool IsPrepared(string? tablet)
    {
        lock (PreparedTablets) return PreparedTablets.Contains(tablet ?? "");
    }

    /// <summary>The install failed after converting: this version keeps running, so its own migration may put the
    /// tables back in its layout.</summary>
    public static void Cancel()
    {
        lock (PreparedTablets) PreparedTablets.Clear();
    }

    /// <summary>Converts the app's own tables (saved here) for the tablets in <paramref name="settings"/>, and the
    /// profiles inside <paramref name="settings"/>, which the caller applies and saves. Returns true when a
    /// profile changed.</summary>
    public static bool Convert(Settings settings)
    {
        bool profilesChanged = false;
        foreach (var profile in settings.Profiles)
        {
            var tablet = profile.Tablet ?? "";
            lock (PreparedTablets) PreparedTablets.Add(tablet);
            profilesChanged |= Convert(profile.BindingSettings);

            var stash = AppSettings.Get(PerAppBindingRuntime.AllBindingsKey(tablet));
            if (!string.IsNullOrEmpty(stash)
                && Newtonsoft.Json.JsonConvert.DeserializeObject<BindingSettings>(stash) is { } all && Convert(all))
                PerAppBindingRuntime.SyncAllBindings(tablet, all);

            foreach (var app in PerAppBindingStore.List(tablet))
            {
                var copy = PerAppBindingStore.CloneBindings(app.Bindings);
                if (Convert(copy)) PerAppBindingStore.SaveBindings(tablet, app.BundleId, copy);
            }
            foreach (var pen in PerPenStore.List(tablet))
            {
                var copy = PerAppBindingStore.CloneBindings(pen.Bindings);
                if (Convert(copy)) PerPenStore.SaveBindings(tablet, pen.Key, copy);
            }
        }
        AppLog.Info($"Ring modes: put in the Version 15 layout before going back (profiles changed: {profilesChanged}).");
        return profilesChanged;
    }

    private static bool Convert(BindingSettings bindings)
    {
        bool changed = false;
        foreach (var wheel in bindings.WheelBindings) changed |= RingModeBinding.ToLegacyLayout(wheel);
        return changed;
    }
}
