using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletArtist.Services;
using OpenTabletArtist.Tests;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using OtdInterop.Tests;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>Build R4: Home without the big headings; six links as a 2×3 grid top-right; attention only
/// as a calm one-line notice under "Your tablets" when something needs it.</summary>
public class BuildR4HomeTests
{
    private static (DashboardView View, HealthService Health, AppSession Session) Home()
    {
        var session = new AppSession(FakeSession.Over(new FakeDaemonTransport(), new MemorySettingsFileStore()), new FakeLifecycle());
        var health = new HealthService(session, new FakeDeviceData(), new DriverConflictMonitor());
        var page = new DashboardViewModel(session, new DaemonStatusViewModel(session), new FakeDialogService(),
            navigateToTablet: (_, _) => { }, health, new TabletsOverviewViewModel());
        var view = new DashboardView { DataContext = page };
        var w = new Window { Content = view, Width = 1100, Height = 900 };
        w.Show();
        w.Measure(new Size(1100, 900));
        w.Arrange(new Rect(0, 0, 1100, 900));
        Dispatcher.UIThread.RunJobs();
        return (view, health, session);
    }

    private static string[] Texts(Visual v) =>
        v.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "").ToArray();

    [AvaloniaFact]
    public void NoHeadings_NoAllClearText_AndSixLinksInTwoRowsOfThree()
    {
        AppSettings.Set(HealthService.LegacyPathNoticeAcknowledgedKey, "");
        AppSettings.Set("daemon.userPath", "");
        var (view, _, session) = Home();
        using var _s = session;
        var texts = Texts(view);
        Assert.DoesNotContain("Needs attention", texts);
        Assert.DoesNotContain("Nothing needs attention.", texts);
        Assert.DoesNotContain("About", texts);
        Assert.Contains("Your tablets", texts);

        // R6: the six links moved into the nav row (MainWindow); BuildR6HomeAlignTests covers them there.
        var about = new AboutView { DataContext = view.DataContext is DashboardViewModel d ? d.About : null };
        var w2 = new Window { Content = about }; w2.Show(); Dispatcher.UIThread.RunJobs();
        var grid = about.GetVisualDescendants().OfType<UniformGrid>().Single(g => g.Name == "LinkGrid");
        Assert.Equal(3, grid.Columns);
        Assert.Equal(3, grid.Rows);   // R18: Feedback added
        var labels = grid.Children.OfType<Button>().Select(b => (b.Content as TextBlock)?.Text).ToArray();
        Assert.Equal(new[] { "Get help", "Supported tablets", "Source code", "Releases", "Feedback", "User manual", "Credits and licences" }, labels);
        Assert.Single(grid.Children.OfType<Button>().Select(b => System.Math.Round(b.Bounds.Width)).Distinct());
        w2.Close();
    }

    [AvaloniaFact]
    public void AnAttentionItem_ShowsAsOneSmallLine()
    {
        AppSettings.Set(HealthService.LegacyPathNoticeAcknowledgedKey, "");
        AppSettings.Set("daemon.userPath", @"D:\portable-otd\OpenTabletDriver.Daemon.exe");
        var (view, health, session) = Home();
        using var _s = session;
        try
        {
            var issue = Assert.Single(health.Issues, i => i.Id == "daemon.ignoredPath");
            var line = view.GetVisualDescendants().OfType<TextBlock>()
                .Single(t => t.Classes.Contains("attentionNotice") && t.Text == issue.Title);
            Assert.True(line.IsEffectivelyVisible);
            Assert.True(line.FontSize <= 13);
            Assert.Equal(TextTrimming.CharacterEllipsis, line.TextTrimming);   // one line
            Assert.DoesNotContain("Needs attention", Texts(view));
        }
        finally { AppSettings.Set("daemon.userPath", ""); }
    }
}
