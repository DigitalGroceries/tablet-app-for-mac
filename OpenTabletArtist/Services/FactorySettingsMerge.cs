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
            ApplyFactoryRingModes(profile);
            ApplyFactoryTipFeel(profile);
            ApplyFactoryHoldKeyboard(profile);
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

    /// <summary>V29 #149 (Zak 20 Nov: "the Factory Settings restores the app to touch ring being Off ... Is this a good
    /// idea?", then: "Factory settings: Touch Ring On with its 4 modes (Rotate, Zoom, Brush size, Brush hardness); the
    /// centre button switches between them and the pop-up shows the mode"): after a factory restore every Touch Ring with a
    /// centre button works in Wacom's four modes at once. Modes can still be turned Off for plain Clockwise / Anticlockwise /
    /// Centre button rows (Zak: artists customise everything). Touch strips have no centre button and are left as the
    /// driver made them. Only the reset paths call this.</summary>
    public static bool ApplyFactoryRingModes(Profile profile)
    {
        var wheels = profile.BindingSettings?.WheelBindings;
        if (wheels == null) return false;
        bool changed = false;
        foreach (var w in wheels)
        {
            if (w == null || w.WheelButtons.Count == 0 || Domain.RingModeBinding.IsActive(w)) continue;
            changed |= Domain.RingModeBinding.Apply(w, Domain.RingModeBinding.Defaults);
        }
        return changed;
    }

    /// <summary>V29 #160 (Zak 20 Nov ~09:18-09:24: "Make a double click with pen button one opening the keyboard be the default
    /// factory settings", then a long press instead): pen button 1 right-clicks on a quick press and opens the floating keyboard when
    /// held. A pen with no buttons is left alone. Only the reset paths call this.</summary>
    public static bool ApplyFactoryHoldKeyboard(Profile profile)
    {
        var buttons = profile.BindingSettings?.PenButtons;
        if (buttons == null || buttons.Count < 1) return false;
        buttons[0] = Domain.HoldKeyboardBinding.Make();
        return true;
    }

    /// <summary>V29 #159 (Zak 20 Nov ~09:03: "make the default factory value for the Tip Feel to be 80"): after a factory
    /// restore the pen curve is the plain one at Tip Feel 80, no smoothing, pressure on. Only the reset paths call this.</summary>
    public static void ApplyFactoryTipFeel(Profile profile)
    {
        if (profile.Filters == null) return;
        PressureCurveProfile.WriteProfile(profile,
            new Domain.PenDynamicsSettings(Domain.PressureCurveSettings.Factory, 0, 0, SmoothAfterCurve: true),
            enable: true, PressureCurveProfile.ManualPressure.Off, eraser: Domain.PressureCurveSettings.Factory);   // V29 #180
    }

    private static Profile CloneProfile(Profile profile) =>
        SettingsCodec.Clone(new Settings { Profiles = new ProfileCollection { profile } })
            .Profiles[0];
}
