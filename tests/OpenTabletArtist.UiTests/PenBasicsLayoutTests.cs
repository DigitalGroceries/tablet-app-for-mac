using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletArtist.Controls;
using OpenTabletArtist.Views;

namespace OpenTabletArtist.UiTests;

/// <summary>
/// R14: the pen basics panel on Tablet › Pen (formerly the Pen page): movement, with position smoothing directly under its choice, then
/// pressing. Position smoothing steadies where the pen lands on screen, so it belongs with movement; it
/// used to sit last in the column, under pressing, which put it with the wrong setting.
/// </summary>
public class PenBasicsLayoutTests
{
    private static (Window Window, PenBasicsPanel View) Page()
    {
        var view = new PenBasicsPanel { DataContext = new OpenTabletArtist.ViewModels.TabletDetailViewModel() };
        var window = new Window { Content = view, Width = 1400, Height = 900 };
        window.Show();
        window.Measure(new Size(1400, 900));
        window.Arrange(new Rect(0, 0, 1400, 900));
        Dispatcher.UIThread.RunJobs();
        return (window, view);
    }

    private static TextBlock Heading(PenBasicsPanel v, string text) =>
        v.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == text && t.Classes.Contains("basicsHeading"));

    private static LabeledSlider PositionSmoothing(PenBasicsPanel v) =>
        v.GetVisualDescendants().OfType<LabeledSlider>().Single(s => s.Label == "Position smoothing");

    [AvaloniaFact]
    public void PositionSmoothing_IsInTheMovementColumn_BetweenMovementAndPressing()
    {
        var (window, view) = Page();
        var order = view.GetVisualDescendants().ToList();
        int movement = order.IndexOf(Heading(view, "Movement"));
        int slider = order.IndexOf(PositionSmoothing(view));
        int pressing = order.IndexOf(Heading(view, "Pressing"));

        Assert.True(movement >= 0 && slider >= 0 && pressing >= 0);
        Assert.True(movement < slider, "position smoothing should come after the movement heading");
        Assert.True(slider < pressing, "position smoothing should come before the pressing section, not under it");
        window.Close();
    }

    [AvaloniaFact]
    public void PositionSmoothing_SharesTheMovementColumn()
    {
        var (window, view) = Page();
        var column = Heading(view, "Movement").GetVisualAncestors().OfType<StackPanel>().First();
        Assert.Contains(PositionSmoothing(view), column.GetVisualDescendants());
        Assert.Contains(Heading(view, "Pressing"), column.GetVisualDescendants());
        window.Close();
    }

    // The slider's tick marks sit just above the pressing heading, so the heading needs its own gap.
    [AvaloniaFact]
    public void ThereIsRoomBetweenThePositionSliderAndThePressingHeading()
    {
        var (window, view) = Page();
        var slider = PositionSmoothing(view);
        var heading = Heading(view, "Pressing");
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.False(heading.IsEffectivelyVisible, "Pressing is Windows-only");   // R14: hidden on macOS/Linux
            window.Close();
            return;
        }
        var sliderBottom = slider.TranslatePoint(new Point(0, slider.Bounds.Height), view)!.Value.Y;
        var headingTop = heading.TranslatePoint(new Point(0, 0), view)!.Value.Y;

        Assert.True(headingTop - sliderBottom >= 16, $"only {headingTop - sliderBottom}px between them");
        window.Close();
    }
}
