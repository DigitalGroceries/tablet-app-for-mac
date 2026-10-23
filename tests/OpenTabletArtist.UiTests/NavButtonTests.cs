using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletArtist.Converters;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>Build M: every nav option is a real button (rounded rectangle, border, title-case label) that
/// lights on hover and press and stays lit when selected — for the top row, sub-page rows and side rails.</summary>
public class NavButtonTests
{
    private static (Window W, RadioButton A, RadioButton B) Open(string theme, string a = "TABLET", string b = "wheels")
    {
        var ra = new RadioButton { Content = a, GroupName = "g" + theme };
        var rb = new RadioButton { Content = b, GroupName = "g" + theme, Margin = new Thickness(8, 0, 0, 0) };
        var w = new Window { Width = 800, Height = 200, Content = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20), Children = { ra, rb } } };
        w.Show();
        var t = (Avalonia.Styling.ControlTheme)w.FindResource(theme)!;
        ra.Theme = t;
        rb.Theme = t;
        Dispatcher.UIThread.RunJobs();
        return (w, ra, rb);
    }

    private static Border Bd(RadioButton r) => r.GetVisualDescendants().OfType<Border>().First(b => b.Name == "Bd");
    private static TextBlock Word(RadioButton r) => r.GetVisualDescendants().OfType<TextBlock>().First(b => b.Name == "Word");
    private static Color Fill(RadioButton r) => ((ISolidColorBrush)Bd(r).Background!).Color;
    private static Color Edge(RadioButton r) => ((ISolidColorBrush)Bd(r).BorderBrush!).Color;

    [AvaloniaTheory]
    [InlineData("WordmarkNav")] [InlineData("PivotTab")] [InlineData("SubtabRadioButton")]
    public void LooksLikeAButton_WithATitleCaseLabel(string theme)
    {
        var (_, a, b) = Open(theme);
        Assert.Equal("Tablet", Word(a).Text);
        Assert.Equal("Wheels", Word(b).Text);
        var bd = Bd(a);
        Assert.True(bd.CornerRadius.TopLeft >= 6, "rounded corners");
        Assert.True(bd.BorderThickness.Left >= 1, "visible border");
        Assert.NotEqual(Colors.Transparent, Edge(a));
        Assert.True(a.Bounds.Height >= 32 && a.Bounds.Width >= 90, $"button size {a.Bounds}");
        Assert.True(a.Focusable);
    }

    [AvaloniaTheory]
    [InlineData("WordmarkNav")] [InlineData("PivotTab")] [InlineData("SubtabRadioButton")]
    public void HoverAndPress_LightUp_AndTheSelectedOneStaysLit(string theme)
    {
        var (w, a, b) = Open(theme);
        var idle = Fill(b);
        var center = b.TranslatePoint(new Point(b.Bounds.Width / 2, b.Bounds.Height / 2), w)!.Value;

        w.MouseMove(center); Dispatcher.UIThread.RunJobs();
        var hover = Fill(b);
        Assert.NotEqual(idle, hover);

        w.MouseDown(center, MouseButton.Left); Dispatcher.UIThread.RunJobs();
        Assert.True(b.IsPressed);
        var pressed = Fill(b);
        Assert.NotEqual(hover, pressed);
        Assert.NotEqual(idle, pressed);

        w.MouseUp(center, MouseButton.Left); Dispatcher.UIThread.RunJobs();
        w.MouseMove(new Point(790, 190)); Dispatcher.UIThread.RunJobs();   // pointer away: still lit
        Assert.True(b.IsChecked);
        Assert.False(a.IsChecked);
        Assert.NotEqual(idle, Fill(b));
        Assert.Equal(Fill(a), idle);                                      // the other is back to normal
        Assert.NotEqual(Edge(a), Edge(b));                                // selected has the accent edge
        Assert.NotEqual(Word(a).Foreground, Word(b).Foreground);
    }

    [AvaloniaFact]
    public void TitleCase_FirstLetterOnly()
    {
        Assert.Equal("Home", TitleCaseConverter.Apply("HOME"));
        Assert.Equal("Advanced", TitleCaseConverter.Apply("advanced"));
        Assert.Equal("Scribble", TitleCaseConverter.Apply("sCRIBBLE"));
    }
}
