using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using Xunit;

namespace OpenTabletArtist.SweepTests;

/// <summary>TASKS #63: pictures of Tablet › Pen with the Radial Menu Settings picture at 1100x720 (Zak's window), 900x600 and larger, with the page hosted like
/// the app does (below the nav row, above the footer). Output: $OTA_SHOTS/radial-spot-*.png or /workspace.</summary>
public class RadialSpotShots
{
    static string Dir => System.Environment.GetEnvironmentVariable("OTA_SHOTS") ?? "/workspace";

    internal static (TabletDetailViewModel Vm, TabletDetailView View, Window Window) Open(double w, double h, bool radialOnButton2, string tab = "PenTab")
    {
        _ = new ThemeViewModel();
        var profile = new Profile { Tablet = "Wacom Intuos4 PTK-440", OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)) };
        profile.BindingSettings.PenButtons = new PluginSettingStoreCollection
        {
            AuxKeyBinding.MakeKeyBinding("B"),
            radialOnButton2 ? RadialMenuBinding.Make() : AuxKeyBinding.MakeKeyBinding("E"),
        };
        profile.BindingSettings.AuxButtons = new PluginSettingStoreCollection();
        var settings = new Settings { Profiles = new ProfileCollection { profile } };
        var vm = new TabletDetailViewModel(profile, settings, applyAction: _ => Task.FromResult(OtdInterop.SettingsApplyOutcome.Live));
        vm.ShowHeader = false;   // the Tablet page host draws its own header
        var view = new TabletDetailView { DataContext = vm };
        // The app hosts the page under the nav row (about 86 px) and above the footer (about 62 px).
        var window = new Window { Content = new Border { Child = view, Margin = new Thickness(24, 86, 24, 62) }, Width = w, Height = h };
        window.Show();
        view.FindControl<RadioButton>(tab)!.IsChecked = true;
        Settle(window);
        // the page scrolls in a short window: scroll the picture (and its line / button) into view
        var spot = view.FindControl<StackPanel>("RadialSpot")!;
        var use = view.FindControl<Button>("RadialUseButton")!;
        (use.IsEffectivelyVisible ? (Control)use : view.FindControl<TextBlock>("RadialSpotLine")!).BringIntoView();
        Settle(window);
        spot.FindControl<TextBlock>("RadialSpotHeading")!.BringIntoView();
        Settle(window);
        return (vm, view, window);
    }

    internal static void Settle(Window w) { for (int i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); } }

    [AvaloniaTheory]
    [InlineData(1100, 720)]
    [InlineData(900, 600)]
    [InlineData(1100, 1000)]
    [InlineData(1046, 1365)]   // Zak's window (16:39 screenshot)
    public void RadialSpot(double w, double h)
    {
        Directory.CreateDirectory(Dir);
        var (vm, _, win) = Open(w, h, radialOnButton2: true);
        win.CaptureRenderedFrame()!.Save($"{Dir}/radial-spot-{(int)w}x{(int)h}.png");
        win.Close();
    }

    [AvaloniaTheory]
    [InlineData(1100, 720)]
    [InlineData(900, 600)]
    [InlineData(1100, 1000)]
    public void RadialSpot_NoButtonYet(double w, double h)
    {
        Directory.CreateDirectory(Dir);
        var (vm, _, win) = Open(w, h, radialOnButton2: false);
        win.CaptureRenderedFrame()!.Save($"{Dir}/radial-spot-none-{(int)w}x{(int)h}.png");
        win.Close();
    }
}
