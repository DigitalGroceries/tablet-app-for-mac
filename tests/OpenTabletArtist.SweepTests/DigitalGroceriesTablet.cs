using System.Collections.Generic;
using System.Threading.Tasks;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.Tests;
using OpenTabletArtist.ViewModels;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OtdInterop;
using OtdInterop.Tests;

namespace OpenTabletArtist.SweepTests;

/// <summary>V30 build 4 (Zak 24 Nov 2026 ~15:50: "Dont create mockup pictures that are different from the app as they are so
/// confusing"): pictures for Zak use a tablet set up like his, never the sweep's PTK-470 test sample (not detected, pen button 2
/// changed to E): a connected Wacom PTK-440 (Intuos4 Small: two pen buttons, six ExpressKeys, the touch ring) with factory settings
/// (pen button 2 opens the radial menu, pressed twice the colour palette; pen button 1 right-clicks and, held, opens the keyboard).
/// The pager's KEEPs K6 / K7 remind of this rule.</summary>
internal static class ZakTablet
{
    public const string Name = "Wacom PTK-440";

    public static Settings Document()
    {
        var p = new Profile
        {
            Tablet = Name,
            OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)),
            AbsoluteModeSettings = new AbsoluteModeSettings
            {
                Display = new AreaSettings { Width = 2560, Height = 1440, X = 1280, Y = 720 },
                Tablet = new AreaSettings { Width = 157.5f, Height = 98.4f, X = 78.75f, Y = 49.2f },
            },
            BindingSettings = new BindingSettings(),
        };
        var b = p.BindingSettings;
        b.TipButton = PenSwitchBinding.MakeAdaptiveBinding(PenSwitchKind.Tip);
        b.EraserButton = PenSwitchBinding.MakeAdaptiveBinding(PenSwitchKind.Eraser);
        b.PenButtons = new PluginSettingStoreCollection
        {
            PenSwitchBinding.MakeAdaptiveBinding(PenSwitchKind.PenButton, 1),
            PenSwitchBinding.MakeAdaptiveBinding(PenSwitchKind.PenButton, 2),
        };
        b.AuxButtons = new PluginSettingStoreCollection();
        for (int i = 0; i < 6; i++) b.AuxButtons.Add(null!);
        b.WheelBindings = new List<WheelBindingSettings> { new() };
        return FactorySettingsMerge.Apply(null, new Settings { Profiles = new ProfileCollection { p } });
    }

    /// <summary>The Tablet page's view model for that tablet: connected, writable (a fake driver takes the changes).</summary>
    public static TabletDetailViewModel Page()
    {
        var s = Document();
        Settings current = s;
        return new TabletDetailViewModel(s.Profiles[0], s,
            applyAction: x => { current = x; return Task.FromResult(SettingsApplyOutcome.Live); },
            refreshAction: () => Task.FromResult<(Settings?, Profile?)>((current, current.Profiles[0])),
            tabletDigitizer: (157.5f, 98.4f),
            isDetected: () => true,
            editBinding: (_, _) => Task.FromResult<AuxBinding?>(null));   // as in the app: the cards offer Change… and Default
    }
}
