using System.IO;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenTabletArtist.Controls;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>Version 29 (Zak, 17 Nov 2026; TASKS #85): Advanced › Daemon has "App window switching fix", Off by default.
/// While it is On the app keeps the driver's flag file; Off removes it. The driver only runs its app activator while the
/// file is there (OpenTabletDriver AppWindowSwitchingFixTests).</summary>
public class AppSwitchFixTests
{
    [AvaloniaFact]
    public void TheSwitch_IsOffByDefault_AndKeepsTheDriversFlagFileInStep()
    {
        var before = AppSettings.Get(AppSwitchFix.Key);
        var oldPath = AppSwitchFix.FlagPath; var oldShow = AppSwitchFixViewModel.ShowOnThisSystem;
        var dir = Path.Combine(Path.GetTempPath(), "ota-appswitch-" + System.Guid.NewGuid().ToString("N"));
        try
        {
            AppSettings.Remove(AppSwitchFix.Key);
            AppSwitchFix.FlagPath = Path.Combine(dir, "app-window-switching-fix");
            AppSwitchFixViewModel.ShowOnThisSystem = true;
            var card = new AppSwitchFixCard();
            var w = new Window { Content = card, Width = 600, Height = 400 };
            w.Show();
            Dispatcher.UIThread.RunJobs();

            var toggle = Assert.IsType<ToggleSetting>(card.FindControl<Control>("AppWindowSwitchingFixToggle"));
            Assert.True(toggle.IsEffectivelyVisible);
            Assert.False(toggle.IsChecked);
            Assert.Equal("App window switching fix", toggle.Label);
            Assert.StartsWith("Leave this off unless tapping another app's window", toggle.Description);
            Assert.False(File.Exists(AppSwitchFix.FlagPath));

            toggle.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(AppSwitchFix.Enabled);
            Assert.True(File.Exists(AppSwitchFix.FlagPath));

            toggle.IsChecked = false;
            Dispatcher.UIThread.RunJobs();
            Assert.False(AppSwitchFix.Enabled);
            Assert.False(File.Exists(AppSwitchFix.FlagPath));
            w.Close();
        }
        finally
        {
            if (before is null) AppSettings.Remove(AppSwitchFix.Key); else AppSettings.Set(AppSwitchFix.Key, before);
            AppSwitchFix.FlagPath = oldPath; AppSwitchFixViewModel.ShowOnThisSystem = oldShow;
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [AvaloniaFact]
    public void AtStart_AFlagFileLeftFromBefore_IsRemovedWhenTheSwitchIsOff_AndMadeWhenOn()
    {
        var before = AppSettings.Get(AppSwitchFix.Key); var oldPath = AppSwitchFix.FlagPath;
        var dir = Path.Combine(Path.GetTempPath(), "ota-appswitch-" + System.Guid.NewGuid().ToString("N"));
        try
        {
            AppSwitchFix.FlagPath = Path.Combine(dir, "app-window-switching-fix");
            Directory.CreateDirectory(dir); File.WriteAllText(AppSwitchFix.FlagPath, "on");   // e.g. the temp folder kept it
            AppSettings.Remove(AppSwitchFix.Key);
            Assert.True(AppSwitchFix.Sync());
            Assert.False(File.Exists(AppSwitchFix.FlagPath));

            AppSettings.Set(AppSwitchFix.Key, "true");
            Directory.Delete(dir, true);                                                    // macOS cleared the temp folder
            Assert.True(AppSwitchFix.Sync());
            Assert.True(File.Exists(AppSwitchFix.FlagPath));
        }
        finally
        {
            if (before is null) AppSettings.Remove(AppSwitchFix.Key); else AppSettings.Set(AppSwitchFix.Key, before);
            AppSwitchFix.FlagPath = oldPath;
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [AvaloniaFact]
    public void AFlagFileThatCannotBeWritten_IsReportedAsFailed_NotAsDone()
    {
        var before = AppSettings.Get(AppSwitchFix.Key); var oldPath = AppSwitchFix.FlagPath;
        var dir = Path.Combine(Path.GetTempPath(), "ota-appswitch-" + System.Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var blocker = Path.Combine(dir, "not-a-folder"); File.WriteAllText(blocker, "x");
            AppSwitchFix.FlagPath = Path.Combine(blocker, "app-window-switching-fix");    // its folder is a file: can't be made
            AppSettings.Set(AppSwitchFix.Key, "true");
            Assert.False(AppSwitchFix.Sync());
        }
        finally
        {
            if (before is null) AppSettings.Remove(AppSwitchFix.Key); else AppSettings.Set(AppSwitchFix.Key, before);
            AppSwitchFix.FlagPath = oldPath;
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
