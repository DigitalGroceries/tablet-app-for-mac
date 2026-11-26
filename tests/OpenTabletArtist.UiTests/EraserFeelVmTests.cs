using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Newtonsoft.Json.Linq;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OtdInterop;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>V29 #180 (Digital Groceries 21 Nov: "1 Can you make them two different setting values?"), the page side of the eraser's own
/// feel, as Grok asked in his 13:27 review: an older profile, a tablet switch, the live dot, and the "opens with" lines.</summary>
public class EraserFeelVmTests
{
    private const string Tablet = "Wacom PTK-440";

    private static (Profile, OpenTabletDriver.Desktop.Settings) OldProfile(double tipSoftness)
    {
        var profile = new Profile
        {
            Tablet = Tablet, OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)),
            Filters = new PluginSettingStoreCollection(),
        };
        TestPens.Arm(profile);
        // Written the way an older app wrote it: the tip's curve only, no eraser curve.
        PressureCurveProfile.WriteProfile(profile,
            new PenDynamicsSettings(PressureCurveSettings.Default with { Softness = tipSoftness }, 0, 0, true), true);
        Assert.Null(PressureCurveProfile.ReadEraserCurve(profile));
        return (profile, new OpenTabletDriver.Desktop.Settings { Profiles = new ProfileCollection { profile } });
    }

    private static async Task WaitFor(Func<bool> done)
    {
        for (int i = 0; i < 300 && !done(); i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
    }

    [AvaloniaFact]
    public async Task AnOlderProfile_TheEraserStartsAsTheTip_AndKeepsThatFeelWhenTheTipChanges()
    {
        var (profile, settings) = OldProfile(0.2);
        var vm = new TabletDetailViewModel(profile, settings, applyAction: _ => Task.FromResult(SettingsApplyOutcome.Live));
        Assert.Equal(0.2, vm.EraserCurve.Softness, 5);           // the eraser shows the tip's curve...
        Assert.Null(PressureCurveProfile.ReadEraserCurve(profile)); // ...and opening the page saves nothing

        vm.Curve = vm.Curve with { Softness = 0.6 };              // the user changes the tip
        await WaitFor(() => PressureCurveProfile.ReadEraserCurve(profile) is not null);
        Assert.Equal(0.2, PressureCurveProfile.ReadEraserCurve(profile)!.Value.Softness, 5);   // the eraser keeps the old feel
        Assert.Equal(0.6, PressureCurveProfile.ReadProfile(profile)!.Value.Dynamics.Curve.Softness, 5);

        vm.Curve = vm.Curve with { Softness = -0.4 };             // and again
        await WaitFor(() => Math.Abs(PressureCurveProfile.ReadProfile(profile)!.Value.Dynamics.Curve.Softness + 0.4) < 1e-4);
        Assert.Equal(0.2, PressureCurveProfile.ReadEraserCurve(profile)!.Value.Softness, 5);
    }

    [AvaloniaFact]
    public void SwitchingToAnotherProfile_LoadsItsEraserCurve()
    {
        var (profile, settings) = OldProfile(0.2);
        var vm = new TabletDetailViewModel(profile, settings, applyAction: _ => Task.FromResult(SettingsApplyOutcome.Live));
        var (other, otherSettings) = OldProfile(0.1);
        PressureCurveProfile.WriteProfile(other, new PenDynamicsSettings(PressureCurveSettings.Default with { Softness = 0.1 }, 0, 0, true), true,
            eraser: PressureCurveSettings.Default with { Softness = -0.7 });
        vm.ReconcileExternalChange(otherSettings, other);
        Assert.Equal(0.1, vm.Curve.Softness, 5);
        Assert.Equal(-0.7, vm.EraserCurve.Softness, 5);
    }

    private sealed class Feed : IDaemonDebugSession
    {
        public event Action<JObject>? DeviceReport;
        public Task SetTabletDebugAsync(bool enabled) => Task.CompletedTask;
        public void Generic(double pressure, bool eraser) => DeviceReport?.Invoke(new JObject
        {
            ["Tablet"] = new JObject { ["Properties"] = new JObject { ["Name"] = Tablet, ["Specifications"] = new JObject
            {
                ["Digitizer"] = new JObject { ["Width"] = 157.5, ["Height"] = 98.4, ["MaxX"] = 31496, ["MaxY"] = 19685 },
                ["Pen"] = new JObject { ["MaxPressure"] = 2047 },
            } } },
            ["Data"] = new JObject { ["Position"] = new JObject { ["X"] = 100, ["Y"] = 100 }, ["Pressure"] = pressure, ["Eraser"] = eraser },
        });
    }

    [AvaloniaFact]
    public void TheLiveDot_UsesTheEraserCurveForTheEraser_TheBarsShowWhatTheTabletSends()
    {
        _ = new ThemeViewModel();
        var feed = new Feed();
        var (profile, settings) = OldProfile(0.0);
        var vm = new TabletDetailViewModel(profile, settings, penInput: feed);
        var view = new TabletDetailView { DataContext = vm };
        var w = new Window { Content = view, Width = 1100, Height = 1000 }; w.Show();
        PenPageTabs.Select(view, "EraserTab"); Dispatcher.UIThread.RunJobs();
        vm.Curve = PressureCurveSettings.Default with { Softness = 0.0 };         // tip: straight
        vm.EraserCurve = PressureCurveSettings.Default with { Softness = 0.8 };   // eraser: soft

        feed.Generic(1024, eraser: true); Dispatcher.UIThread.RunJobs();
        var eraserDot = vm.LiveProcessed;
        Assert.Equal(0.5, vm.LiveEraserPressure!.Value, 2);   // the bar: the pressure the tablet sends (as before #180)
        feed.Generic(0, eraser: true); feed.Generic(1024, eraser: false); Dispatcher.UIThread.RunJobs();
        var tipDot = vm.LiveProcessed;
        Assert.NotNull(eraserDot); Assert.NotNull(tipDot);
        Assert.True(eraserDot > tipDot + 0.05, $"the eraser's soft curve gives more at half press ({eraserDot} vs {tipDot})");
        w.Close();
    }

    [AvaloniaFact]
    public void TheOpensWithLines_NameTheButtonTheUserChose()
    {
        _ = new ThemeViewModel();
        var (profile, settings) = OldProfile(0.0);
        // Button 1 opens the radial menu instead of button 2 (the factory set-up).
        profile.BindingSettings.PenButtons = new PluginSettingStoreCollection { RadialMenuBinding.Make(), AuxKeyBinding.MakeKeyBinding("E") };
        var vm = new TabletDetailViewModel(profile, settings, applyAction: _ => Task.FromResult(SettingsApplyOutcome.Live));
        vm.RefreshRadialSpot();
        Assert.Equal("Radial Menu opens with Pen Button 1", vm.RadialSpotLine);
        Assert.Equal("Colour Palette opens with Pen button 1 double click", vm.PaletteSpotLine);
        // The factory set-up keeps "By default".
        profile.BindingSettings.PenButtons = new PluginSettingStoreCollection { AuxKeyBinding.MakeKeyBinding("B"), RadialMenuBinding.Make() };
        vm.RefreshRadialSpot();
        Assert.Equal("By default Radial Menu opens with Pen Button 2", vm.RadialSpotLine);
        Assert.Equal("By default Colour Palette opens with Pen button 2 double click", vm.PaletteSpotLine);
    }

    [AvaloniaFact]
    public void AnEraserSearchRoute_OpensThePenPageWithTheEraserSettings()
    {
        _ = new ThemeViewModel();
        var (profile, settings) = OldProfile(0.0);
        var vm = new TabletDetailViewModel(profile, settings, applyAction: _ => Task.FromResult(SettingsApplyOutcome.Live));
        var view = new TabletDetailView { DataContext = vm };
        var w = new Window { Content = view, Width = 1100, Height = 1000 }; w.Show();
        // An older index's route (the Eraser tab and its feel panel) and the new one land in the same place.
        Assert.True(view.OpenSearchPlace("ToolPenButton", "EraserTab", "EraserFeelDetails")); Dispatcher.UIThread.RunJobs();
        Assert.True(view.FindControl<RadioButton>("PenTab")!.IsChecked);
        Assert.True(view.FindControl<RadioButton>("EraserFeelChip")!.IsChecked);
        Assert.True(vm.EraserSettingsShown);
        Assert.True(view.FindControl<Control>("FeelDetails")!.IsVisible);
        // The Pen tab's own route shows the tip's settings again.
        Assert.True(view.OpenSearchPlace("ToolPenButton", "PenTab")); Dispatcher.UIThread.RunJobs();
        Assert.True(view.FindControl<RadioButton>("TipFeelChip")!.IsChecked);
        Assert.False(vm.EraserSettingsShown);
        w.Close();
    }

    [AvaloniaFact]
    public void APenWithNoButtons_HasNoButtonTestLine()
    {
        // Grok 21 Nov 14:01: the line lost its IsVisible when it got its sizer; a pen without barrel buttons shows no test line.
        _ = new ThemeViewModel();
        var (profile, settings) = OldProfile(0.0);
        var vm = new TabletDetailViewModel(profile, settings, applyAction: _ => Task.FromResult(SettingsApplyOutcome.Live));
        vm.PenButton1Row = null; vm.PenButton2Row = null;
        var view = new TabletDetailView { DataContext = vm };
        var w = new Window { Content = view, Width = 1100, Height = 1000 }; w.Show();
        PenPageTabs.Select(view, "PenTab");
        Assert.False(view.FindControl<Grid>("PenButtonTestRow")!.IsEffectivelyVisible);
        vm.PenButton1Row = new PenSwitchRowViewModel(PenSwitchKind.PenButton, 1, null, false, (_, _, _) => Task.CompletedTask);
        Dispatcher.UIThread.RunJobs();
        Assert.True(view.FindControl<Grid>("PenButtonTestRow")!.IsEffectivelyVisible);
        w.Close();
    }
}
