using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using Xunit;

namespace OpenTabletArtist.SweepTests;

/// <summary>V29 #158 screenshots: the Touch Ring page with the centre button's menu (Next mode, and a keyboard shortcut that
/// leaves only mode 1). Written to $OTA_SHOTS_DIR (default /workspace/ota-158-shots); run with OTA_APPDATA set to an empty folder.</summary>
public class V29RingMenuShots
{
    static string Dir => System.Environment.GetEnvironmentVariable("OTA_SHOTS_DIR") ?? "/workspace/ota-158-shots";

    private static Window Open(string tablet, RingCentre centre)
    {
        _ = new ThemeViewModel();
        var ring = new WheelBindingSettings
        {
            WheelButtons = new PluginSettingStoreCollection(new[] { (PluginSettingStore)null! }),
            ClockwiseActivationThreshold = 5, CounterClockwiseActivationThreshold = 5,
        };
        RingModeBinding.ApplyLayout(ring, RingModeBinding.Defaults, centre);
        var profile = new Profile { Tablet = tablet, OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)) };
        profile.BindingSettings.WheelBindings = new List<WheelBindingSettings> { ring };
        var settings = new OpenTabletDriver.Desktop.Settings { Profiles = new ProfileCollection { profile } };
        var vm = new TabletDetailViewModel(profile, settings, applyAction: _ => Task.FromResult(OtdInterop.SettingsApplyOutcome.Live));
        var view = new TabletDetailView { DataContext = vm };
        var w = new Window { Content = view, Width = 1300, Height = 1250 }; w.Show();
        view.FindControl<RadioButton>("WheelsTab")!.IsChecked = true;
        for (int i = 0; i < 6; i++) { Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); }
        return w;
    }

    [AvaloniaFact]
    public void Screenshots()
    {
        System.IO.Directory.CreateDirectory(Dir);
        var w1 = Open("Wacom Intuos4 PTK-440", RingCentre.Next);
        w1.CaptureRenderedFrame()!.Save($"{Dir}/touchring-centre-next-mode.png"); w1.Close();
        var w2 = Open("Wacom Intuos4 PTK-440 b", new RingCentre(false,
            new AuxBinding(AuxKind.Keyboard, new AuxCombo(false, false, false, "Z", true), AuxKeyBinding.None, AuxKeyBinding.None)));
        w2.CaptureRenderedFrame()!.Save($"{Dir}/touchring-centre-shortcut-mode1-only.png"); w2.Close();
    }
}
