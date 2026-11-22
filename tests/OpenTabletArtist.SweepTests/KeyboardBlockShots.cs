using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
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
        RadialSpotShots.Settle(win);
        win.CaptureRenderedFrame()!.Save($"{Dir}/keyboard-block-{(int)w}x{(int)h}.png");
        // V29 #180: the same page with the eraser's settings shown (Eraser Feel), and with the eraser touching the tablet.
        vm.EraserSettingsShown = true;
        vm.EraserPressed = true;
        RadialSpotShots.Settle(win);
        win.CaptureRenderedFrame()!.Save($"{Dir}/pen-page-eraser-{(int)w}x{(int)h}.png");
        // Grok 21 Nov: the bottom of the page too (palette size and its line, Smoother fast curves), in both views.
        var sv = view.FindControl<Grid>("PenPageOuter")!.GetVisualAncestors().OfType<ScrollViewer>().First();
        sv.Offset = new Vector(0, sv.Extent.Height);
        RadialSpotShots.Settle(win);
        win.CaptureRenderedFrame()!.Save($"{Dir}/pen-page-eraser-scrolled-{(int)w}x{(int)h}.png");
        vm.EraserSettingsShown = false; vm.EraserPressed = false;
        view.FindControl<RadioButton>("TipFeelChip")!.IsChecked = true;
        RadialSpotShots.Settle(win);
        sv.Offset = new Vector(0, sv.Extent.Height);
        RadialSpotShots.Settle(win);
        win.CaptureRenderedFrame()!.Save($"{Dir}/pen-page-tip-scrolled-{(int)w}x{(int)h}.png");
        win.Close();
    }

    /// <summary>V29 #180 (Grok 21 Nov): the capitals test line in a long language wraps inside the drawing-and-cards width.</summary>
    [AvaloniaTheory]
    [InlineData("fr")]
    [InlineData("ru")]
    [InlineData("bg")]   // V29 #183/#198
    [InlineData("sv")]   // V29 #183: 'Suddgummi'
    public void PenPage_InALongLanguage(string code)
    {
        Directory.CreateDirectory(Dir);
        using var mac = OpenTabletArtist.Services.UiPlatform.PretendMac();   // Change… / Default as on Zak's Mac
        OpenTabletArtist.Services.Localizer.SetPreference(code);
        try
        {
            var (vm, view, win) = RadialSpotShots.Open(1046, 1365, radialOnButton2: true);
            RadialSpotShots.Settle(win);
            win.CaptureRenderedFrame()!.Save($"{Dir}/pen-page-{code}-1046x1365.png");
            vm.PenButton2Pressed = true; RadialSpotShots.Settle(win);
            win.CaptureRenderedFrame()!.Save($"{Dir}/pen-page-{code}-button2-1046x1365.png");
            vm.PenButton1Pressed = true; RadialSpotShots.Settle(win);   // V29 #183: the join word ('ET', 'И')
            win.CaptureRenderedFrame()!.Save($"{Dir}/pen-page-{code}-buttons12-1046x1365.png");
            win.Close();
        }
        finally { OpenTabletArtist.Services.Localizer.SetPreference("en"); }
    }
}
