using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>Build R6 (the user's guide lines): on Home the six-link grid's top edge equals the main tab row's top
/// edge, its first row is one tab high at the tabs' height, its right edge has the same right margin as the
/// tablet switcher, and it does not overlap "Your tablets". Plus: checked boxes are light with a dark tick.</summary>
public class BuildR6HomeAlignTests
{
    [AvaloniaFact]
    public void HomeLinks_TopAndHeightMatchTheTabs_RightMarginMatchesTheSwitcher_NoOverlap()
    {
        var w = new MainWindow { Width = 1100, Height = 720 };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var tabs = w.GetVisualDescendants().OfType<RadioButton>().Where(r => r.IsEffectivelyVisible && r.Theme ==
                    (Avalonia.Styling.ControlTheme?)Application.Current!.FindResource("WordmarkNav")).ToList();
        Assert.NotEmpty(tabs);
        var tab = tabs[0];
        var tabTop = tab.TranslatePoint(new Point(0, 0), w)!.Value.Y;

        var grid = w.GetVisualDescendants().OfType<UniformGrid>().Single(g => g.Name == "LinkGrid");
        Assert.True(grid.IsEffectivelyVisible);
        var gridTopLeft = grid.TranslatePoint(new Point(0, 0), w)!.Value;
        Assert.True(System.Math.Abs(gridTopLeft.Y - tabTop) < 0.5, $"grid top {gridTopLeft.Y} vs tab top {tabTop}");
        var first = grid.Children.OfType<Button>().First();
        Assert.True(System.Math.Abs(first.Bounds.Height - tab.Bounds.Height) < 0.5, $"link {first.Bounds.Height} vs tab {tab.Bounds.Height}");
        Assert.True(System.Math.Abs(first.TranslatePoint(new Point(0, 0), w)!.Value.Y - tabTop) < 0.5);

        // right margin: the same right-cluster column the tablet switcher uses on other pages
        var host = w.GetVisualDescendants().OfType<ContentControl>().Single(c => c.Name == "HomeLinksHost");
        var cluster = (StackPanel)host.GetVisualParent()!;
        var switcher = cluster.Children.OfType<OpenTabletArtist.Views.TabletSwitcherBar>().Single();
        Assert.False(switcher.IsVisible); // Home has no switcher
        var gridRight = w.Bounds.Width - (gridTopLeft.X + grid.Bounds.Width);
        var clusterRight = w.Bounds.Width - (cluster.TranslatePoint(new Point(0, 0), w)!.Value.X + cluster.Bounds.Width);
        Assert.True(System.Math.Abs(gridRight - clusterRight) < 0.5, $"grid right margin {gridRight} vs cluster {clusterRight}");
        Assert.Equal(20, clusterRight, 0); // the nav row's right margin, the same as the switcher's

        // no overlap with "Your tablets"
        var heading = w.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Your tablets");
        var hb = new Rect(heading.TranslatePoint(new Point(0, 0), w)!.Value, heading.Bounds.Size);
        var links = w.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "HomeLinks");
        var lb = new Rect(links.TranslatePoint(new Point(0, 0), w)!.Value, links.Bounds.Size);
        Assert.False(hb.Intersects(lb), $"heading {hb} overlaps links {lb}");
        w.Close();
    }

    [AvaloniaFact]
    public void CheckedCheckBox_IsLightGreyBox_WithNearBlackTick_AfterThemeRefresh()
    {
        // The R5 escape: ThemeViewModel's skin refresh cleared the lightness keys, so Fluent's white tick
        // came back. Build the VM (it runs that refresh) before measuring.
        _ = new OpenTabletArtist.ViewModels.ThemeViewModel();
        var cb = new CheckBox { Content = "Force proportions", IsChecked = true };
        var w = new Window { Content = new StackPanel { Children = { cb } } };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        var box = cb.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "NormalRectangle");
        var glyph = cb.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single(p => p.Name == "CheckGlyph");
        var fill = ((ISolidColorBrush)box.Background!).Color;
        var tick = ((ISolidColorBrush)glyph.Fill!).Color;
        Assert.Equal(Color.FromRgb(0xD8, 0xD8, 0xD8), fill);
        Assert.Equal(Color.FromRgb(0x14, 0x14, 0x14), tick);
        w.Close();
    }
}

public class BuildR6ResourceWhiteTests
{
    /// <summary>Runtime check, after the theme refresh that let R5's white tick through: every brush the
    /// checkbox / switch / radio / slider / selection templates read resolves no brighter than #E4E4E4.</summary>
    [AvaloniaFact]
    public void TemplateBrushes_AreNoBrighterThanE4_AfterThemeRefresh()
    {
        _ = new OpenTabletArtist.ViewModels.ThemeViewModel();
        var app = Application.Current!;
        var keys = new[]
        {
            "CheckBoxCheckGlyphForegroundChecked", "CheckBoxCheckGlyphForegroundCheckedPointerOver", "CheckBoxCheckGlyphForegroundCheckedPressed",
            "CheckBoxCheckGlyphForegroundIndeterminate", "ToggleSwitchKnobFillOn", "ToggleSwitchKnobFillOnPointerOver", "ToggleSwitchKnobFillOnPressed",
            "AccentBrush", "SelectionFillBrush", "SelectionTextBrush", "SelectionBorderBrush", "TextPrimaryBrush", "TextInverseBrush",
            "GlassBgBrush", "BgBaseBrush", "BgSurfaceBrush", "WacomPaneBrush", "PenDiagramStrokeBrush", "ButtonForeground", "ComboBoxForeground",
        };
        foreach (var k in keys)
        {
            Assert.True(app.TryGetResource(k, app.ActualThemeVariant, out var v), k);
            var c = v switch { ISolidColorBrush b => b.Color, Color col => col, _ => throw new Xunit.Sdk.XunitException($"{k}: {v}") };
            Assert.False(c.R > 0xE4 && c.G > 0xE4 && c.B > 0xE4, $"{k} = {c}");
        }
        Assert.True(OpenTabletArtist.Services.UiColors.SoftWhite.R <= 0xD8);
    }
}
