using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Newtonsoft.Json.Linq;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using Xunit;
using OtdInterop;
using OtdInterop.Tests;
using OpenTabletArtist.Tests;

namespace OpenTabletArtist.UiTests;

/// <summary>Build R13: Tablet › Pen "Current pressure" bar follows the daemon's live pen reports.</summary>
public class BuildR13Tests
{
    private sealed class FakePressureFeed : IDaemonDebugSession
    {
        public event Action<JObject>? DeviceReport;
        public bool Enabled;
        public Task SetTabletDebugAsync(bool enabled) { Enabled = enabled; return Task.CompletedTask; }
        public void SendTool(int tool) => DeviceReport?.Invoke(new JObject
        {
            ["Tablet"] = new JObject { ["Properties"] = new JObject { ["Name"] = "Wacom PTK-440" } },
            ["Data"] = new JObject { ["Tool"] = tool, ["Serial"] = 1 },
        });
        public void Send(double rawPressure, double maxPressure = 1023, bool? eraser = null) => DeviceReport?.Invoke(new JObject
        {
            ["Tablet"] = new JObject { ["Properties"] = new JObject { ["Name"] = "Wacom PTK-440", ["Specifications"] = new JObject
            {
                ["Digitizer"] = new JObject { ["Width"] = 152.0, ["Height"] = 95.0, ["MaxX"] = 15200, ["MaxY"] = 9500 },
                ["Pen"] = new JObject { ["MaxPressure"] = maxPressure },
            } } },
            ["Data"] = new JObject { ["Position"] = new JObject { ["X"] = 7600, ["Y"] = 4750 }, ["Pressure"] = rawPressure, ["Eraser"] = eraser is { } e ? e : null },
        });
    }

    [AvaloniaFact]
    public void CurrentPressureBar_FollowsLivePenPressure()
    {
        _ = new ThemeViewModel();
        var feed = new FakePressureFeed();
        var profile = new Profile { Tablet = "Wacom PTK-440", OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)) }; TestPens.Arm(profile);
        var settings = new OpenTabletDriver.Desktop.Settings { Profiles = new ProfileCollection { profile } };
        var vm = new TabletDetailViewModel(profile, settings, penInput: feed);
        var view = new TabletDetailView { DataContext = vm };
        var w = new Window { Content = view, Width = 1100, Height = 900 }; w.Show();
        view.FindControl<RadioButton>("PenTab")!.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(feed.Enabled, "the Pen tab should switch the daemon's report stream on");

        var label = view.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Current pressure" && t.IsEffectivelyVisible);
        var bar = ((Panel)label.Parent!.Parent!).Children.OfType<ProgressBar>().Single();

        feed.Send(767.25); Dispatcher.UIThread.RunJobs();
        Assert.Equal(0.75, bar.Value, 3);
        Assert.Equal(0.75, vm.LivePressure!.Value, 3);
        var ind = bar.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "PART_Indicator");
        Assert.NotNull(ind);
        w.UpdateLayout();
        Assert.True(ind!.Bounds.Width > bar.Bounds.Width * 0.6, $"indicator {ind.Bounds.Width} of {bar.Bounds.Width}");

        // The fill must stand out from the track: luminance gap of at least 0x50 over the dark window.
        var fill = ((Avalonia.Media.ISolidColorBrush)ind.Background!).Color;
        var track = (Avalonia.Media.ISolidColorBrush)bar.Background!;
        double bg = 0x2D, trackL = bg + (track.Color.R - bg) * track.Color.A / 255.0 * track.Opacity;
        Assert.True(Math.Abs(fill.R - trackL) >= 0x50, $"fill {fill} vs track {trackL:0}");
        feed.Send(0); Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, bar.Value, 3);
        w.Close();
    }

    private static (FakePressureFeed, TabletDetailViewModel, TabletDetailView, Window) Open(string tab)
    {
        _ = new ThemeViewModel();
        var feed = new FakePressureFeed();
        var profile = new Profile { Tablet = "Wacom PTK-440", OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)) }; TestPens.Arm(profile);
        var settings = new OpenTabletDriver.Desktop.Settings { Profiles = new ProfileCollection { profile } };
        var vm = new TabletDetailViewModel(profile, settings, penInput: feed);
        var view = new TabletDetailView { DataContext = vm };
        var w = new Window { Content = view, Width = 1100, Height = 900 }; w.Show();
        view.FindControl<RadioButton>(tab)!.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        return (feed, vm, view, w);
    }

    private static ProgressBar VisibleBar(TabletDetailView view)
    {
        var label = view.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Current pressure" && t.IsEffectivelyVisible);
        return ((Panel)label.Parent!.Parent!).Children.OfType<ProgressBar>().Single();
    }

    [AvaloniaTheory]
    [InlineData("PenTab", true)]   // Intuos protocol: tool report says Eraser, then plain pen reports
    [InlineData("EraserTab", true)]
    [InlineData("PenTab", false)]  // tablets whose pen reports carry Eraser = true
    [InlineData("EraserTab", false)]
    public void EraserPressure_FeedsTheSameLiveDisplays(string tab, bool viaToolReport)
    {
        var (feed, vm, view, w) = Open(tab);
        int padSamples = 0; double padRaw = -1;
        vm.PreviewSample += (s, _) => { if (s.IsDown) { padSamples++; padRaw = s.Pressure; } };
        if (viaToolReport) { feed.SendTool(1); feed.Send(511.5); } else feed.Send(511.5, eraser: true);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(tab == "EraserTab" ? 0.5 : 0, VisibleBar(view).Value, 3);   // R15: each bar its own end
        Assert.Equal("Eraser", vm.LiveEndText);
        Assert.Equal("0.5000", vm.LiveInputText);
        Assert.NotNull(vm.LiveProcessed);
        Assert.Equal(1, padSamples);
        Assert.Equal(0.5, padRaw, 3);

        if (viaToolReport) feed.SendTool(0);
        feed.Send(1023, eraser: viaToolReport ? null : false); Dispatcher.UIThread.RunJobs();
        Assert.Equal("Pen tip", vm.LiveEndText);
        Assert.Equal(tab == "PenTab" ? 1 : 0, VisibleBar(view).Value, 3);
        w.Close();
    }

    [AvaloniaFact]
    public void PenPage_HasNoPressurePivot_AndPenDynamicsLinkOpensFeelDetails()
    {
        _ = new ThemeViewModel();
        var vm = new TabletDetailViewModel();
        Window w;
        vm.RequestTab(TabletDetailTab.PenDynamics);
        var tab = new TabletDetailView { DataContext = vm };
        w = new Window { Content = tab, Width = 1100, Height = 900 }; w.Show(); Dispatcher.UIThread.RunJobs();
        Assert.True(tab.FindControl<RadioButton>("PenTab")!.IsChecked);
        Assert.True(tab.FindControl<Border>("FeelDetails")!.IsVisible);
        Assert.Single(tab.GetVisualDescendants().OfType<DynamicsView>(), d => d.IsEffectivelyVisible);
        w.Close();
    }

    [AvaloniaFact]
    public void PenBasics_HasNoTiltSwitch_TabletPenHasIt()
    {
        _ = new ThemeViewModel();
        var vm = new TabletDetailViewModel();
        var pen = new PenBasicsPanel { DataContext = vm };
        var w = new Window { Content = pen, Width = 1100, Height = 800 }; w.Show(); Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(pen.GetVisualDescendants().OfType<OpenTabletArtist.Controls.ToggleSetting>(), t => t.Label?.Contains("tilt", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Contains(pen.GetVisualDescendants().OfType<OpenTabletArtist.Controls.ToggleSetting>(), t => t.Label == "Disable pen tip");
        Assert.Contains(pen.GetVisualDescendants().OfType<OpenTabletArtist.Controls.ToggleSetting>(), t => t.Label == "Disable pressure sensitivity");
        w.Close();
        vm.RequestTab(TabletDetailTab.PenTilt);
        var tab = new TabletDetailView { DataContext = vm };
        w = new Window { Content = tab, Width = 1100, Height = 900 }; w.Show(); Dispatcher.UIThread.RunJobs();
        Assert.True(tab.FindControl<RadioButton>("PenTab")!.IsChecked);
        Assert.Contains(tab.GetVisualDescendants().OfType<CheckBox>(), c => c.Content as string == "Turn tilt off" && c.IsEffectivelyVisible);
        w.Close();
    }

    [AvaloniaFact]
    public void HomeTiltLink_OpensTabletPen()
    {
        var session = new AppSession(FakeSession.Over(new FakeDaemonTransport(), new MemorySettingsFileStore()), new FakeLifecycle());
        var health = new HealthService(session, new FakeDeviceData(), new DriverConflictMonitor());
        (string, TabletDetailTab?)? tablet = null;
        var page = new DashboardViewModel(session, new DaemonStatusViewModel(session), new FakeDialogService(),
            navigateToTablet: (n, t) => tablet = (n, t), health, new TabletsOverviewViewModel());
        page.FollowLinkCommand.Execute(new OpenTabletArtist.Domain.Health.HealthLink("Tilt is disabled", "Tablet › Pen", OpenTabletArtist.Domain.Health.RemediationArea.TabletPenTilt, "Wacom PTK-440"));
        Assert.Equal(("Wacom PTK-440", (TabletDetailTab?)TabletDetailTab.PenTilt), tablet);
    }
}
