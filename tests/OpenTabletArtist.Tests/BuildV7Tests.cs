using System.IO;
using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Version 7: the bundled daemon path falls back to Daemon/&lt;exe&gt; when there is no per-architecture folder.</summary>
public class BuildV7Tests
{
    [Fact]
    public void BundledPath_WithoutArchFolder_IsTheFlatPath()
    {
        var d = Path.Combine(Path.GetTempPath(), "v7-" + Path.GetRandomFileName());
        Assert.Equal(Path.GetFullPath(Path.Combine(d, "Daemon", DaemonExePaths.DaemonExeName)), DaemonExePaths.BundledPath(d));
    }

    [Fact]
    public void ArchFolder_IsArm64OrX64() => Assert.Contains(DaemonExePaths.ArchFolder, new[] { "arm64", "x64" });

    static string Root()
    {
        var d = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "OpenTabletArtist.slnx"))) d = d.Parent;
        return d!.FullName;
    }

    [Fact]
    public void ScribbleOffNote_IsPlain_Dark_AndHasTheButton()
    {
        var view = File.ReadAllText(Path.Combine(Root(), "OpenTabletArtist", "Views", "TestView.axaml"));
        Assert.Contains("UsePenOnScreenButton", view);
        Assert.Contains("Content=\"Use pen on screen\"", view);
        Assert.Contains("Background=\"#1E1E1E\" Padding=\"32\"", view);
        var vm = File.ReadAllText(Path.Combine(Root(), "OpenTabletArtist", "ViewModels", "TestViewModel.cs"));
        Assert.Contains("Drawing is off because the pen isn't set to follow the screen.", vm);
        Assert.DoesNotContain("Absolute output mode on the active tablet", vm);
    }

    [Fact]
    public void Home_WarnsWhenAccessibilityIsMissing()
    {
        var view = File.ReadAllText(Path.Combine(Root(), "OpenTabletArtist", "Views", "DashboardView.axaml"));
        Assert.Contains("IsVisible=\"{Binding AccessibilityMissing}\"", view);
        Assert.Contains("Content=\"Open Device Control and Data Access\"", view);
        var shell = File.ReadAllText(Path.Combine(Root(), "OpenTabletArtist", "Services", "PlatformShell.cs"));
        Assert.Contains("Privacy_Accessibility", shell);
        Assert.True(OpenTabletArtist.Services.MacAccessibility.IsTrusted() || System.OperatingSystem.IsMacOS());
    }

    [Fact]
    public void PointerPane_UsesMacOS27Name()
    {
        Assert.Equal("Accessibility", OpenTabletArtist.Services.MacAccessibility.PointerPaneName(15));
        Assert.Equal("Device Control and Data Access", OpenTabletArtist.Services.MacAccessibility.PointerPaneName(27));
        var setup = File.ReadAllText(Path.Combine(Root(), "OpenTabletArtist", "Views", "SetupWindow.cs"));
        Assert.Contains("setup.restart.button", setup);
        Assert.Contains("setup.wacom.remove_leftovers", setup);
        Assert.DoesNotContain("app.check_again", setup);
        Assert.DoesNotContain("Kill(", setup);
    }

    [Fact]
    public void SetupShouldShow_WacomOnlyDismissedStopsReopen()
    {
        // Missing permissions always show.
        Assert.True(OpenTabletArtist.Views.SetupWindow.ShouldShow(false, true, false, false));
        Assert.True(OpenTabletArtist.Views.SetupWindow.ShouldShow(true, false, true, true));
        // Clear machine: never show.
        Assert.False(OpenTabletArtist.Views.SetupWindow.ShouldShow(true, true, false, false));
        Assert.False(OpenTabletArtist.Views.SetupWindow.ShouldShow(true, true, false, true));
        // Wacom leftovers: show once, then stay quiet after dismiss until next launch.
        Assert.True(OpenTabletArtist.Views.SetupWindow.ShouldShow(true, true, true, false));
        Assert.False(OpenTabletArtist.Views.SetupWindow.ShouldShow(true, true, true, true));
    }

    [Fact]
    public void SetupPlan_OneStepAtATime()
    {
        var S = typeof(OpenTabletArtist.Views.SetupWindow);
        Assert.NotNull(S);
        Assert.Equal(new[] { OpenTabletArtist.Views.SetupWindow.Step.Done }, OpenTabletArtist.Views.SetupWindow.Plan(false, true, true, false));
        // Permissions before Wacom so an empty leftover folder cannot block System Settings.
        Assert.Equal(new[] { OpenTabletArtist.Views.SetupWindow.Step.ReadPen, OpenTabletArtist.Views.SetupWindow.Step.MovePointer,
            OpenTabletArtist.Views.SetupWindow.Step.Wacom, OpenTabletArtist.Views.SetupWindow.Step.Restart, OpenTabletArtist.Views.SetupWindow.Step.Done },
            OpenTabletArtist.Views.SetupWindow.Plan(true, false, false, false));
        Assert.Equal(new[] { OpenTabletArtist.Views.SetupWindow.Step.Restart, OpenTabletArtist.Views.SetupWindow.Step.Done },
            OpenTabletArtist.Views.SetupWindow.Plan(false, true, true, true));
    }

    [Xunit.Fact]
    public void LoginTimeLaunchStartsHiddenOnlyAfterSetupAndSoonAfterLogin()
    {
        Xunit.Assert.True(OpenTabletArtist.Services.AutoStartCoordinator.IsLoginTimeLaunch(true, System.TimeSpan.FromSeconds(30)));
        Xunit.Assert.False(OpenTabletArtist.Services.AutoStartCoordinator.IsLoginTimeLaunch(false, System.TimeSpan.FromSeconds(30)));
        Xunit.Assert.False(OpenTabletArtist.Services.AutoStartCoordinator.IsLoginTimeLaunch(true, System.TimeSpan.FromMinutes(10)));
        Xunit.Assert.False(OpenTabletArtist.Services.AutoStartCoordinator.IsLoginTimeLaunch(true, null));
    }


    [Xunit.Fact]
    public void SessionAgeUsesDockNotLoginwindow()
    {
        var ps = "  1-02:00:00 /System/Library/CoreServices/loginwindow.app/Contents/MacOS/loginwindow\n      00:40 /System/Library/CoreServices/Dock.app/Contents/MacOS/Dock\n      00:39 /System/Library/CoreServices/Finder.app/Contents/MacOS/Finder\n";
        var (sig, age) = OpenTabletArtist.Services.AutoStartCoordinator.PickSessionAge(ps);
        Xunit.Assert.Equal("Dock", sig);
        Xunit.Assert.Equal(System.TimeSpan.FromSeconds(40), age);
        var (sig2, _) = OpenTabletArtist.Services.AutoStartCoordinator.PickSessionAge("05:00 /System/Library/CoreServices/loginwindow.app/Contents/MacOS/loginwindow");
        Xunit.Assert.Equal("loginwindow", sig2);
    }

    [Xunit.Fact]
    public void LoginStartIgnoresAutomaticShowRequestsForAMinute()
    {
        var now = new System.DateTime(2026, 10, 23, 20, 0, 0); var until = now.AddMinutes(1);
        Xunit.Assert.True(OpenTabletArtist.MainWindow.KeepHidden(false, true, now, until));
        Xunit.Assert.False(OpenTabletArtist.MainWindow.KeepHidden(true, true, now, until));    // pen menu click
        Xunit.Assert.False(OpenTabletArtist.MainWindow.KeepHidden(false, false, now, until));  // opened by hand
        Xunit.Assert.False(OpenTabletArtist.MainWindow.KeepHidden(false, true, until.AddSeconds(1), until));
    }
}
