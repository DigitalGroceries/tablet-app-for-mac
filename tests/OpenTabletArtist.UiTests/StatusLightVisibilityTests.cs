using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>Version 23 BETA (Jay, Discussion #31): Settings › System shows the Status light only with an Intuos4.</summary>
public class StatusLightVisibilityTests
{
    [AvaloniaFact]
    public void Status_light_setting_follows_whether_an_Intuos4_is_connected()
    {
        var vm = new StartupViewModel();
        var view = new StartupView { DataContext = vm };
        var w = new Window { Content = view, Width = 900, Height = 800 };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        var toggle = view.FindControl<Control>("StatusLedToggle")!;
        // The toggle's own section (its logical parent): a hidden section never builds its content.
        var section = Assert.IsType<OpenTabletArtist.Controls.Section>(toggle.Parent);
        Assert.False(section.IsVisible);
        vm.ShowStatusLight = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(section.IsVisible);
        Assert.True(toggle.IsEffectivelyVisible);
        w.Close();
    }
}
