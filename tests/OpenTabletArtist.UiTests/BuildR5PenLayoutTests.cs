using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletArtist.Views;

namespace OpenTabletArtist.UiTests;

/// <summary>Build R5 (the user's mock-ups): Pen page in the pen's own order (eraser top, buttons, tip bottom)
/// beside a centred upright diagram whose button numbers are black on light badges; selected tabs are a
/// light fill with dark text.</summary>
public class BuildR5PenLayoutTests
{
    private static Window Show(Control c)
    {
        var w = new Window { Content = c, Width = 1400, Height = 900 };
        w.Show(); w.Measure(new Size(1400, 900)); w.Arrange(new Rect(0, 0, 1400, 900));
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    [AvaloniaFact]
    public void TabletPen_CardsBesideCentredDiagram_PenPageHasNone()
    {
        // R13/R14: the cards and pen drawing live on Tablet › Pen (the PEN page is gone).
        Window w;
        var tablet = new TabletDetailView { DataContext = new OpenTabletArtist.ViewModels.TabletDetailViewModel() };
        w = Show(tablet);
        var cards = tablet.GetVisualDescendants().OfType<PenSwitchCard>().Where(c => c.IsEffectivelyVisible).ToList();
        Assert.NotEmpty(cards);
        Assert.All(cards, c => Assert.DoesNotContain("hero", c.Classes));   // R12: all headings one size
        var diagram = tablet.GetVisualDescendants().OfType<PenDiagram>().First(d => d.IsEffectivelyVisible);
        Assert.Equal(Avalonia.Layout.VerticalAlignment.Center, diagram.VerticalAlignment);
        w.Close();
    }

    [AvaloniaFact]
    public void DiagramNumbers_AreBlack_OnLightBadges()
    {
        var d = new PenDiagram();
        var w = Show(d);
        var badges = d.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("penBadge")).ToList();
        Assert.Equal(3, badges.Count);
        foreach (var b in badges)
        {
            var fill = ((ISolidColorBrush)b.Background!).Color;
            var text = ((ISolidColorBrush)((TextBlock)b.Child!).Foreground!).Color;
            Assert.True(fill.R >= 0xC0 && fill.R <= 0xE4, $"badge fill {fill}");
            Assert.True(text.R <= 0x20, $"number colour {text}");
        }
        w.Close();
    }

    [AvaloniaFact]
    public void SelectedTab_IsLightFill_WithDarkText_AndRoundCorners()
    {
        var theme = (Avalonia.Styling.ControlTheme)Application.Current!.FindResource("WordmarkNav")!;
        var rb = new RadioButton { Theme = theme, Content = "Home", IsChecked = true };
        var w = Show(new StackPanel { Children = { rb } });
        var fill = ((ISolidColorBrush)rb.Background!).Color;
        var fg = ((ISolidColorBrush)rb.Foreground!).Color;
        Assert.True(fill.R >= 0xC0 && fill.R <= 0xE4, $"fill {fill}");
        Assert.True(fg.R <= 0x20, $"text {fg}");
        var bd = rb.GetVisualDescendants().OfType<Border>().First(b => b.Name == "Bd");
        Assert.True(bd.CornerRadius.TopLeft >= 4);
        w.Close();
    }
}
