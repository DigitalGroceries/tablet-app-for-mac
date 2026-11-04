using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>Version 23 (owner's report, 4 Nov 2026): on Home, "Go back to Version 21" showed as "Go back to Ver…" at
/// every window size, because the three update buttons didn't fit in one row. The row now wraps: a button that
/// doesn't fit goes under, whole.</summary>
public class HomeUpdateButtonsFitTests
{
    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("bg")]
    [InlineData("de")]
    [InlineData("ru")]
    public void Every_update_button_shows_its_whole_text(string lang)
    {
        Localizer.SetPreference(lang);
        var state = AppUpdateState.Shared;
        try
        {
            state.SetChoicesForTest(new Updater.UpdateChoices(
                Stable: new Updater.Release(21, "z", "s"),
                Beta: new Updater.Release(24, "z", "s", Beta: 1, Numbered: false),
                StableIsGoBack: true));
            var view = new AboutView { DataContext = new AboutViewModel() };
            var w = new Window { Content = view, Width = 1000, Height = 700 };
            w.Show();
            Dispatcher.UIThread.RunJobs();
            w.UpdateLayout();

            var links = view.FindControl<StackPanel>("HomeLinks")!;
            var row = view.FindControl<Panel>("UpdateChoicesRow")!;
            var names = new[] { "EarlierVersionsButton", "TryBetaButton", "GoBackButton", "CheckForUpdatesButton" };
            foreach (var n in names)
            {
                var b = view.FindControl<Button>(n)!;
                Assert.True(b.IsVisible, $"{n} is hidden");
                // The button got all the width it asked for (nothing cut), and it lies inside the Home links column.
                Assert.True(b.Bounds.Width + 0.5 >= b.DesiredSize.Width, $"{n} is cut: {b.Bounds.Width} < {b.DesiredSize.Width}");
                var topLeft = b.TranslatePoint(new Point(0, 0), links)!.Value;
                Assert.True(topLeft.X >= -0.5 && topLeft.X + b.Bounds.Width <= links.Bounds.Width + 0.5,
                    $"{n} sticks out of the column: x {topLeft.X}, width {b.Bounds.Width}, column {links.Bounds.Width}");
                var text = b.GetVisualDescendants().OfType<TextBlock>().First();
                Assert.False(text.TextLayout.TextLines.Any(l => l.HasCollapsed), $"{n} text is shortened");
            }
            Assert.True(row.Bounds.Width <= links.Bounds.Width + 0.5);
            w.Close();
        }
        finally
        {
            state.SetChoicesForTest(Updater.UpdateChoices.None);
            Localizer.SetPreference("en");
        }
    }
}
