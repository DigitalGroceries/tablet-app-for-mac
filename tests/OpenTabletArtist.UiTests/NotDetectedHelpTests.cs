using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>#27 + #30 (Mike Milo, Wacom comparison point 6): a tablet that isn't found says what to try, with
/// Restart driver and Send a report, instead of a dead end; the Home card says where to look.</summary>
public class NotDetectedHelpTests
{
    private static (TabletDetailViewModel Vm, int[] Calls) Page(bool detected)
    {
        var profile = new Profile { Tablet = "Wacom DTK-2200", OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)) };
        TestPens.Arm(profile);
        var settings = new OpenTabletDriver.Desktop.Settings { Profiles = new ProfileCollection { profile } };
        var calls = new int[2];
        var vm = new TabletDetailViewModel(profile, settings, isDetected: () => detected,
            restartDriver: () => { calls[0]++; return Task.CompletedTask; },
            sendReport: () => calls[1]++);
        vm.RefreshDetectionStatus();
        return (vm, calls);
    }

    [AvaloniaFact]
    public void NotFound_ShowsWhatToTry_AndBothButtonsWork()
    {
        var (vm, calls) = Page(detected: false);
        Assert.True(vm.ShowNotDetectedHelp);
        Assert.Contains("Restart driver", vm.NotDetectedHelp);
        vm.RestartDriverHereCommand.Execute(null);
        vm.SendReportHereCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, calls[0]);
        Assert.Equal(1, calls[1]);

        var view = new TabletDetailView { DataContext = vm };
        var w = new Window { Content = view, Width = 1100, Height = 800 };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.True(view.FindControl<StackPanel>("NotDetectedHelpPanel")!.IsVisible);
        Assert.Equal("Restart driver", view.FindControl<Button>("NotDetectedRestartBtn")!.Content);
        w.Close();
    }

    [AvaloniaFact]
    public void Found_ShowsNoHelp()
    {
        var (vm, _) = Page(detected: true);
        Assert.False(vm.ShowNotDetectedHelp);
    }

    [AvaloniaFact]
    public void WithoutTheActions_ThereIsNothingToOffer()
    {
        var vm = new TabletDetailViewModel();
        vm.RefreshDetectionStatus();
        Assert.False(vm.ShowNotDetectedHelp);
    }

    [Fact]
    public void HomeCard_HasTheHint_ForATabletThatIsNotFound()
    {
        var item = new TabletOverviewItemViewModel("Wacom DTK-2200", false, "Not detected", null, () => { }, () => Task.CompletedTask);
        Assert.Contains("Double-click", item.NotFoundHint);
    }
}
