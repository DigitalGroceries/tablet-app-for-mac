using System;
using System.Linq;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Interop.Input;
using OpenTabletDriver.Desktop.Profiles;
using OtdInterop;

namespace OpenTabletArtist.Services;

/// <summary>
/// After the daemon's ResetSettings, only currently connected tablets get fresh factory profiles.
/// Not-detected tablets keep their pre-restore BindingSettings in open editors and in the All stash,
/// so "Restore factory settings" looked like a no-op for Double Click Fix (Zak, build 6). This merge
/// re-attaches those profiles with factory Double Click Fix and refreshes the All stash from the
/// post-restore document so a later All restore cannot put the old value back.
/// </summary>
public static class FactorySettingsMerge
{
    /// <summary>
    /// Returns settings that include every pre-restore tablet, with factory Double Click Fix on each
    /// profile, and syncs the All bindings stash to match.
    /// </summary>
    public static Settings Apply(Settings? before, Settings after)
    {
        var merged = SettingsCodec.Clone(after);
        if (before is not null)
        {
            foreach (var old in before.Profiles)
            {
                var name = old.Tablet ?? "";
                if (string.IsNullOrEmpty(name)) continue;
                if (merged.Profiles.Any(p =>
                        string.Equals(p.Tablet, name, StringComparison.OrdinalIgnoreCase)))
                    continue;
                // Daemon dropped this not-detected tablet. Keep its shell so the open page still has a
                // profile, but put Double Click Fix back to the factory default.
                var kept = CloneProfile(old);
                ApplyFactoryDoubleClickFix(kept.BindingSettings);
                merged.Profiles.Add(kept);
            }
        }

        foreach (var profile in merged.Profiles)
        {
            ApplyFactoryDoubleClickFix(profile.BindingSettings);
            ApplyFactoryRadialButton(profile);
        }

        SyncAllStashes(merged);
        return merged;
    }

    /// <summary>Write each profile's BindingSettings into the All stash so focus-restore matches factory.</summary>
    public static void SyncAllStashes(Settings settings)
    {
        foreach (var profile in settings.Profiles)
        {
            var tablet = profile.Tablet ?? "";
            if (string.IsNullOrEmpty(tablet)) continue;
            PerAppBindingRuntime.SyncAllBindings(tablet, profile.BindingSettings);
        }
    }

    public static void ApplyFactoryDoubleClickFix(BindingSettings bindings)
    {
        bindings.DoubleClickFix = ClickJitterGate.UiDefault;
        bindings.TipDoubleClickDistance = DoubleClickState.DefaultMoveTolerance;
    }

    /// <summary>TASKS #56 (Zak 17:12): factory settings put the Radial menu on pen button 2 (and its filter in the
    /// profile). Button 1 and every other binding stay as the daemon made them. A pen with fewer than two
    /// buttons is left alone. Only the reset paths call this, never a normal load.</summary>
    public static bool ApplyFactoryRadialButton(Profile profile)
    {
        var buttons = profile.BindingSettings?.PenButtons;
        if (buttons == null || buttons.Count < 2) return false;
        buttons[1] = Domain.RadialMenuBinding.Make();
        Domain.RadialMenuBinding.SyncFilter(profile);
        return true;
    }

    private static Profile CloneProfile(Profile profile) =>
        SettingsCodec.Clone(new Settings { Profiles = new ProfileCollection { profile } })
            .Profiles[0];
}
