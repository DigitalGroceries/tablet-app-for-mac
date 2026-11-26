using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenTabletArtist.Controls;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>V28 #80 Option A (Digital Groceries, 16 Nov 2026): Settings › System has "Save changes automatically", on by default,
/// and the switch changes the remembered choice both ways.</summary>
public class AutoSaveSwitchTests
{
    [AvaloniaFact]
    public void TheSwitch_IsShown_OnByDefault_AndTurnsAutoSaveOffAndOn()
    {
        var before = AppSettings.Get(AutoSave.Key);
        try
        {
            AppSettings.Remove(AutoSave.Key);
            var vm = new StartupViewModel();
            var view = new StartupView { DataContext = vm };
            var w = new Window { Content = view, Width = 900, Height = 900 };
            w.Show();
            Dispatcher.UIThread.RunJobs();

            var toggle = Assert.IsType<ToggleSetting>(view.FindControl<Control>("AutoSaveToggle"));
            Assert.True(toggle.IsEffectivelyVisible);
            Assert.True(toggle.IsChecked);
            Assert.Equal("Save changes automatically", toggle.Label);

            toggle.IsChecked = false;
            Dispatcher.UIThread.RunJobs();
            Assert.False(AutoSave.Enabled);

            toggle.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(AutoSave.Enabled);
            w.Close();
        }
        finally
        {
            if (before is null) AppSettings.Remove(AutoSave.Key);
            else AppSettings.Set(AutoSave.Key, before);
        }
    }
}
