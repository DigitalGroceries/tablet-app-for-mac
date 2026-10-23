using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OtdInterop;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>Build L: the user's Wheels tab (four ring modes) ran off the bottom of an 800 px window and couldn't be
/// scrolled. Root cause: MainWindow hosted pages in a StackPanel, so the tablet page's own tab ScrollViewers got
/// infinite height and never scrolled. These host the real TabletDetailView in the same shell layout as
/// MainWindow (Grid Auto/*/Auto → ScrollViewer Disabled → Border → DockPanel) at 800 px.</summary>
public class TabScrollTests
{
    private const double WindowHeight = 800, FooterHeight = 64;

    private static Settings Mapped()
    {
        var settings = new Settings { Profiles = new ProfileCollection { new Profile { OutputMode = new OpenTabletDriver.Desktop.Reflection.PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)), Tablet = "PTK-440" } } };
        settings.Profiles.First().AbsoluteModeSettings = new AbsoluteModeSettings
        { Display = new AreaSettings(), Tablet = new AreaSettings { Width = 269, Height = 168, X = 134, Y = 84 } };
        return settings;
    }

    internal static Control Shell(Control page, Control? padding = null) =>
        // Mirrors MainWindow.axaml's content row; keep in step with it.
        new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Children =
            {
                new Border { Height = 40 },
                new ScrollViewer
                {
                    [Grid.RowProperty] = 1,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = new Border { Padding = new Thickness(24, 0, 24, 18), Child = new DockPanel { Children = { page } } },
                },
                new Border { [Grid.RowProperty] = 2, Height = FooterHeight },
            },
        };

    private static (Window Window, ScrollViewer Tab) Open(double height)
    {
        var settings = Mapped();
        var vm = new TabletDetailViewModel(settings.Profiles.First(), settings,
            applyAction: _ => Task.FromResult(default(SettingsApplyOutcome)!),
            refreshAction: () => Task.FromResult<(Settings?, Profile?)>((settings, settings.Profiles.First())),
            tabletDigitizer: (269, 168));
        vm.RequestTab(TabletDetailTab.DisplayMapping);
        var view = new TabletDetailView { DataContext = vm };
        var window = new Window { Content = Shell(view), Width = 1100, Height = height };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var tab = view.GetVisualDescendants().OfType<ScrollViewer>()
            .First(s => s.Classes.Contains("tabScroll") && s.IsEffectivelyVisible);
        return (window, tab);
    }

    [AvaloniaFact]
    public void TabScrollViewer_IsLimitedToTheWindow_At800px()
    {
        var (_, tab) = Open(WindowHeight);
        Assert.True(double.IsFinite(tab.Viewport.Height));
        Assert.True(tab.Bounds.Height <= WindowHeight - FooterHeight, $"tab height {tab.Bounds.Height}");
        Assert.Equal(tab.Bounds.Height, tab.Viewport.Height, 0);
    }

    [AvaloniaFact]
    public void TallTab_ScrollsWithKeyboardAndWheel_AndTheFooterStaysInPlace()
    {
        var (window, tab) = Open(WindowHeight);
        // Make the content taller than any 800 px window, like four ring modes do.
        if (tab.Content is Control c) c.MinHeight = 3000;
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Assert.True(tab.Extent.Height > tab.Viewport.Height, $"extent {tab.Extent.Height} viewport {tab.Viewport.Height}");

        tab.Focus();
        window.KeyPress(Key.PageDown, RawInputModifiers.None, PhysicalKey.PageDown, null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(tab.Offset.Y > 0, "Page Down scrolled");
        var afterPage = tab.Offset.Y;
        window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(tab.Offset.Y > afterPage, "Down arrow scrolled");

        tab.Offset = new Vector(0, 0);
        var p = tab.TranslatePoint(new Point(tab.Bounds.Width / 2, tab.Bounds.Height / 2), window)!.Value;
        window.MouseWheel(p, new Vector(0, -3));
        Dispatcher.UIThread.RunJobs();
        Assert.True(tab.Offset.Y > 0, "mouse wheel / trackpad scrolled");

        Assert.True(tab.TranslatePoint(new Point(0, tab.Bounds.Height), window)!.Value.Y <= WindowHeight - FooterHeight + 0.5);
    }
}
