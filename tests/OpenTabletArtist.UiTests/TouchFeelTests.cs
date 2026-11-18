using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OtdInterop;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>TASKS #90 (Daniel, CTH-461): Touch speed and Scroll momentum under the Touch trackpad switch on
/// Tablet › More › About, only for tablets with finger touch; a change is applied to the driver.</summary>
public class TouchFeelTests
{
    static (Window W, TabletDetailView View, TabletDetailViewModel Vm, List<Settings> Applied) Open(string tablet)
    {
        var profile = new Profile { Tablet = tablet, OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)) };
        var settings = new Settings { Profiles = new ProfileCollection { profile } };
        var applied = new List<Settings>();
        var vm = new TabletDetailViewModel(profile, settings,
            applyAction: s => { applied.Add(s); return Task.FromResult(SettingsApplyOutcome.Live); },
            refreshAction: () => Task.FromResult<(Settings?, Profile?)>((settings, profile)));
        var view = new TabletDetailView { DataContext = vm };
        var w = new Window { Content = view, Width = 1100, Height = 900 };
        w.Show();
        view.OpenSearchPlace("ToolMoreButton", "AboutTab");
        for (int i = 0; i < 3; i++) { Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); }
        return (w, view, vm, applied);
    }

    [AvaloniaFact]
    public async Task A_touch_tablet_shows_speed_and_momentum_and_a_change_reaches_the_driver()
    {
        var (w, view, vm, applied) = Open("Wacom CTH-461");
        try
        {
            Assert.True(view.FindControl<StackPanel>("TouchFeel")!.IsEffectivelyVisible);
            Assert.Equal(50, vm.TouchSpeed);
            Assert.False(vm.ScrollMomentum);

            vm.TouchSpeed = 70;
            vm.ScrollMomentum = true;
            await vm.FlushPendingEditsAsync();
            Dispatcher.UIThread.RunJobs();

            var last = Assert.Single(applied.TakeLast(1));
            var profile = last.Profiles.First(p => p.Tablet == "Wacom CTH-461");
            Assert.Equal(70, TouchTrackpad.ReadSpeed(profile));
            Assert.True(TouchTrackpad.ReadMomentum(profile));
            Assert.Equal("70", vm.TouchSpeedText);
        }
        finally { w.Close(); vm.Dispose(); }
    }

    [AvaloniaFact]
    public void A_tablet_without_finger_touch_has_neither()
    {
        var (w, view, vm, _) = Open("Wacom PTK-440");
        try
        {
            Assert.False(view.FindControl<StackPanel>("TouchFeel")!.IsEffectivelyVisible);
        }
        finally { w.Close(); vm.Dispose(); }
    }
}
