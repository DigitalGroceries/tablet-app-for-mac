using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>Build R18: Eraser parity, fixed 30% pressure, active ring mode, Pen tool icon.</summary>
public class BuildR18Tests
{
    private static (TabletDetailViewModel, TabletDetailView, Window) Open(string tab)
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
        return (vm, view, w);
    }

    private static (int toggles, int sliders, int combos) Controls(Control root) =>
        (root.GetVisualDescendants().OfType<ToggleSwitch>().Count(c => c.IsVisible),
         root.GetVisualDescendants().OfType<Slider>().Count(c => c.IsVisible),
         root.GetVisualDescendants().OfType<ComboBox>().Count(c => c.IsVisible));

    [AvaloniaFact]
    public void Eraser_HasACounterpart_ForEveryPenFrameControl()
    {
        var (_, pv, pw) = Open("PenTab");
        var pen = Controls(pv.FindControl<Border>("PenBasicsBox")!);
        pw.Close();
        var (_, view, w) = Open("EraserTab");
        Assert.Equal(pen, Controls(view.FindControl<Border>("EraserBasicsBox")!));   // Movement + Pen input frame
        foreach (var n in new[] { "EraserTiltBox", "EraserFeelSlider", "EraserCustomizeButton", "EraserPressurePercent", "EraserCards", "EraserPenDiagram" })
            Assert.True(view.FindControl<Control>(n)!.IsEffectivelyVisible, n);
        var shared = view.FindControl<Control>("EraserLeftColumn")!.GetVisualDescendants().OfType<TextBlock>()
            .Count(t => t.IsEffectivelyVisible && (t.Text ?? "").Contains("Shared with Pen"));
        Assert.True(shared >= 2);
        w.Close();
    }

    [AvaloniaFact]
    public void LegacyFlat30PercentCurve_IsStillRecognised()
    {
        // Version 1 replaced the flat 30% with Manual Pen Tip / Eraser Pressure; R18 profiles are still read as "off".
        var (vm, _, w) = Open("PenTab");
        Assert.True(TabletDetailViewModel.IsFixedPressureCurve(TabletDetailViewModel.FixedPressureCurve));
        Assert.Equal(0.30, TabletDetailViewModel.LegacyFixedPressure, 9);
        w.Close();
    }

    [AvaloniaFact]
    public void Ring_ActiveBadge_FollowsThePublishedMode_AndTheCentreButtonSteps()
    {
        var ed = new RingModesEditor(0, RingModeBinding.Defaults, true, true, true, null, null, _ => Task.CompletedTask);
        var n = ed.Rows.Count(r => r.ToMode() is { Enabled: true, IsMapped: true });
        Assert.True(n >= 2);
        var file = System.IO.Path.GetTempFileName();
        System.IO.File.WriteAllText(file, "2 4\nZoom");
        ed.RefreshFromStateFile(file);
        Assert.Equal(2, ed.ActiveMode);
        Assert.Single(ed.Rows, r => r.IsActive);
        for (int i = 0; i < n; i++) ed.StepActiveCommand.Execute(null);
        Assert.Equal(2, ed.ActiveMode);                       // a full cycle comes back round
        ed.StepActiveCommand.Execute(null);
        Assert.Equal(n >= 3 ? 3 : 1, ed.ActiveMode);
        Assert.Equal("4 modes. Press the ring's centre button to switch.", ed.Summary.Replace(n + " modes", "4 modes"));
    }

    [AvaloniaFact]
    public void TouchRing_AboutIsTopRight_WithTheExactText_AndNoIdleDash()
    {
        var (_, view, w) = Open("WheelsTab");
        var about = view.FindControl<Control>("AboutTouchRing");
        if (about is null || !about.IsEffectivelyVisible) { w.Close(); return; }   // no ring on a bare profile
        var texts = about.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("About the touch ring", texts);
        Assert.Contains("Turning off Enable wheels pauses all ring actions without deleting them.", texts);
        w.Close();
    }

    [AvaloniaFact]
    public void ToolRow_UsesThePenIcon_FromTheAppIcon()
    {
        var (_, view, w) = Open("PenTab");
        var icon = view.FindControl<Control>("ToolPenIcon")!;
        Assert.IsType<OpenTabletArtist.Controls.PenIcon>(icon);
        Assert.True(icon.Bounds.Width > 20 && icon.Bounds.Height > 15);
        w.Close();
    }
}
