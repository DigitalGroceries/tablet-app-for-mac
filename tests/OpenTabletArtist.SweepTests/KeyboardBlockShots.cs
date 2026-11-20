using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using Xunit;

namespace OpenTabletArtist.SweepTests;

/// <summary>V29 #177 (Zak 20 Nov ~20:04): mockup picture of the Pen tab with the Keyboard settings… button and the Pen tip in one
/// block under the pen buttons, set up like Zak's own Pen tab (defaults, radial menu on button 2). Output: $OTA_SHOTS/keyboard-block-*.png.</summary>
public class KeyboardBlockShots
{
    static string Dir => System.Environment.GetEnvironmentVariable("OTA_SHOTS") ?? "/workspace";

    [AvaloniaTheory]
    [InlineData(1046, 1365)]
    public void PenTab_KeyboardBlock(double w, double h)
    {
        Directory.CreateDirectory(Dir);
        using var mac = OpenTabletArtist.Services.UiPlatform.PretendMac();   // Change… / Default as on Zak's Mac
        _ = new ThemeViewModel();
        var p = new Profile
        {
            Tablet = "Wacom PTK-440",
            OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)),
            AbsoluteModeSettings = new AbsoluteModeSettings
            {
                Display = new AreaSettings { Width = 1496, Height = 967, X = 748, Y = 483 },
                Tablet = new AreaSettings { Width = 157.5f, Height = 98.4f, X = 78.75f, Y = 49.2f },
            },
            BindingSettings = new BindingSettings(),
        };
        var b = p.BindingSettings;
        b.TipButton = PenSwitchBinding.MakeAdaptiveBinding(PenSwitchKind.Tip);
        b.EraserButton = PenSwitchBinding.MakeAdaptiveBinding(PenSwitchKind.Eraser);
        b.PenButtons = new PluginSettingStoreCollection { HoldKeyboardBinding.Make(), RadialMenuBinding.Make() };
        b.AuxButtons = new PluginSettingStoreCollection();
        b.WheelBindings = new List<WheelBindingSettings> { new() };
        var settings = new Settings { Profiles = new ProfileCollection { p } };
        var vm = new TabletDetailViewModel(p, settings, applyAction: _ => Task.FromResult(OtdInterop.SettingsApplyOutcome.Live),
            editBinding: (x, _) => Task.FromResult<AuxBinding?>(null));
        vm.ShowHeader = false;
        var view = new TabletDetailView { DataContext = vm };
        var win = new Window { Content = new Border { Child = view, Margin = new Thickness(24, 86, 24, 62) }, Width = w, Height = h };
        win.Show();
        view.FindControl<RadioButton>("PenTab")!.IsChecked = true;
        RadialSpotShots.Settle(win);
        view.FindControl<Button>("KeyboardSettingsLink")!.BringIntoView();
        RadialSpotShots.Settle(win);
        win.CaptureRenderedFrame()!.Save($"{Dir}/keyboard-block-{(int)w}x{(int)h}.png");
        win.Close();
    }
}
