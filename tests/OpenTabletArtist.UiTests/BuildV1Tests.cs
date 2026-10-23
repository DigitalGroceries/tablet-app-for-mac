using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>Version 1 (second round): manual pressure frames, this-app-only clicks, "More", fitted boxes.</summary>
public class BuildV1Tests
{
    private static (TabletDetailViewModel, TabletDetailView, Window, Profile, OpenTabletDriver.Desktop.Settings) Open(string tab)
    {
        _ = new ThemeViewModel();
        var profile = TestPens.Arm(new Profile { Tablet = "Wacom PTK-440", OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)) });
        var settings = new OpenTabletDriver.Desktop.Settings { Profiles = new ProfileCollection { profile } };
        var vm = new TabletDetailViewModel(profile, settings);
        vm.PenButton1Row = new PenSwitchRowViewModel(PenSwitchKind.PenButton, 1, null, false, (_, _, _) => Task.CompletedTask);
        vm.PenButton2Row = new PenSwitchRowViewModel(PenSwitchKind.PenButton, 2, null, false, (_, _, _) => Task.CompletedTask);
        var view = new TabletDetailView { DataContext = vm };
        var w = new Window { Content = view, Width = 1100, Height = 1100 }; w.Show();
        view.FindControl<RadioButton>(tab)!.IsChecked = true;
        Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        return (vm, view, w, profile, settings);
    }

    [AvaloniaTheory]
    [InlineData("PenTab", "ManualTipPressureFrame", "Manual Pen Tip Pressure")]
    [InlineData("EraserTab", "ManualEraserPressureFrame", "Manual Eraser Pressure")]
    public void ManualPressureFrame_IsShown_With50PercentDefault(string tab, string frame, string title)
    {
        var (vm, view, w, _, _) = Open(tab);
        var f = view.FindControl<HeaderedContentControl>(frame)!;
        Assert.True(f.IsEffectivelyVisible);
        Assert.Equal(title, f.Header);
        Assert.Equal(0.5, vm.ManualTipPressure, 9);
        Assert.Equal(0.5, vm.ManualEraserPressure, 9);
        w.Close();
    }

    [Fact]
    public void ManualPressure_RoundTripsThroughTheFilter_AndCurveEditsKeepIt()
    {
        var profile = new Profile { Tablet = "T", OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)) };
        var settings = new OpenTabletDriver.Desktop.Settings { Profiles = new ProfileCollection { profile } };
        PressureCurveProfile.Write(settings, "T", PenDynamicsSettings.Default, true, new(true, 0.7, 0.2));
        Assert.Equal(new PressureCurveProfile.ManualPressure(true, 0.7, 0.2), Round(PressureCurveProfile.ReadManual(profile)));
        PressureCurveProfile.Write(settings, "T", PenDynamicsSettings.Default, true);   // a curve edit
        Assert.Equal(new PressureCurveProfile.ManualPressure(true, 0.7, 0.2), Round(PressureCurveProfile.ReadManual(profile)));
        // The written curve is the user's curve, not a flat 30%.
        Assert.False(TabletDetailViewModel.IsFixedPressureCurve(PressureCurveProfile.ReadProfile(profile)!.Value.Dynamics.Curve));
    }

    private static PressureCurveProfile.ManualPressure Round(PressureCurveProfile.ManualPressure m) =>
        new(m.On, System.Math.Round(m.Tip, 4), System.Math.Round(m.Eraser, 4));

    [Fact]
    public void DisabledEnd_ClicksInThisAppOnly_AndCountsAsOff()
    {
        var b = PenSwitchBinding.MakeOwnWindowBinding();
        Assert.Equal(PenSwitchBinding.OwnWindowBindingPath, b.Path);
        Assert.True(PenSwitchBinding.IsOff(b));
        Assert.True(PenSwitchBinding.IsOff(null));
        Assert.False(PenSwitchBinding.IsOff(PenSwitchBinding.MakeAdaptiveBinding(PenSwitchKind.Tip)));
        Assert.Equal("Off (clicks in this app only)", PenSwitchBinding.GetPlainCurrentLabel(b, PenSwitchKind.Tip));
    }

    [AvaloniaFact]
    public void ToolStrip_SaysMore_NotOta()
    {
        var (_, view, w, _, _) = Open("PenTab");
        var texts = view.FindControl<Control>("ToolScroll")!.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("More", texts);
        Assert.DoesNotContain(texts, t => (t ?? "").Contains("OTA"));
        w.Close();
    }

    [AvaloniaFact]
    public void DropDown_IsNoWiderThanItsLongestChoice()
    {
        var cb = new ComboBox { ItemsSource = new[] { "Short", "A much longer choice" } };
        var w = new Window { Content = new StackPanel { Children = { cb } }, Width = 800, Height = 200 }; w.Show();
        Dispatcher.UIThread.RunJobs(); w.UpdateLayout();
        Assert.True(cb.Bounds.Width < 400, $"width {cb.Bounds.Width}");
        Assert.True(cb.Bounds.Width >= Controls.FitWidth.MinFit);
        w.Close();
    }
}
