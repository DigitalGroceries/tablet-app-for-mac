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

    private static Slider PositionSmoothing(PenBasicsPanel v) => v.FindControl<Slider>("PositionSmoothingSlider")!;
    private static TextBlock Caption(PenBasicsPanel v) => v.FindControl<TextBlock>("PositionSmoothingCaption")!;
    private static TextBlock Note(PenBasicsPanel v) => v.FindControl<TextBlock>("PositionSmoothingNote")!;
    private static TextBlock Number(PenBasicsPanel v) => v.FindControl<TextBlock>("PositionSmoothingValue")!;

    [AvaloniaFact]
    public void PositionSmoothing_IsInTheMovementColumn_BeforePressing()
    {
        var (window, view) = Page();
        var order = view.GetVisualDescendants().ToList();
        int slider = order.IndexOf(PositionSmoothing(view));
        int pressing = order.IndexOf(Heading(view, "Pressing"));
        Assert.True(slider >= 0 && pressing >= 0);
        Assert.True(slider < pressing, "position smoothing should come before the pressing section, not under it");
        window.Close();
    }

    [AvaloniaFact]
    public void PositionSmoothing_SharesTheMovementColumn()
    {
        var (window, view) = Page();
        var column = Heading(view, "Pressing").GetVisualAncestors().OfType<StackPanel>().Last(c => c.GetVisualDescendants().Contains(PositionSmoothing(view)));
        Assert.Contains(PositionSmoothing(view), column.GetVisualDescendants());
        window.Close();
    }

    // TASKS #55: the Movement choice is hidden by default and comes back with the Settings switch.
    [AvaloniaFact]
    public void MovementChoice_IsHiddenByDefault_AndShownWithTheSettingsSwitch()
    {
        var was = OpenTabletArtist.Services.PenMovementPolicy.ShowChoice;
        try
        {
            OpenTabletArtist.Services.PenMovementPolicy.ShowChoice = false;
            var (window, view) = Page();
            Assert.False(view.FindControl<ComboBox>("MovementBox")!.IsEffectivelyVisible);
            Assert.False(Heading(view, "Movement").IsEffectivelyVisible);
            OpenTabletArtist.Services.PenMovementPolicy.ShowChoice = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(view.FindControl<ComboBox>("MovementBox")!.IsEffectivelyVisible);
            Assert.True(Heading(view, "Movement").IsEffectivelyVisible);
            window.Close();
        }
        finally { OpenTabletArtist.Services.PenMovementPolicy.ShowChoice = was; }
    }

    // TASKS #55: the slider is UNDER its caption, at least as wide as the explanation below it, 0-10, with the number shown.
    [AvaloniaFact]
    public void PositionSmoothing_IsUnderItsCaption_AsWideAsTheNote_AndRunsZeroToTen()
    {
        var (window, view) = Page();
        var slider = PositionSmoothing(view);
        var caption = Caption(view);
        var note = Note(view);
        Assert.Equal((0.0, 10.0), (slider.Minimum, slider.Maximum));
        double captionY = caption.TranslatePoint(new Point(0, 0), view)!.Value.Y;
        double sliderY = slider.TranslatePoint(new Point(0, 0), view)!.Value.Y;
        double noteY = note.TranslatePoint(new Point(0, 0), view)!.Value.Y;
        Assert.True(sliderY > captionY + caption.Bounds.Height - 1, "the slider is under its caption");
        Assert.True(noteY >= sliderY + slider.Bounds.Height - 1, "the explanation is under the slider");
        Assert.True(slider.Bounds.Width >= note.Bounds.Width - 0.5, $"slider {slider.Bounds.Width} vs note {note.Bounds.Width}");
        Assert.Equal("0.0", Number(view).Text);
        ((OpenTabletArtist.ViewModels.TabletDetailViewModel)view.DataContext!).PositionSmoothing = 0.25;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("10.0", Number(view).Text);
        Assert.Equal(10, slider.Value, 3);
        window.Close();
    }

    // The slider sits just above the pressing heading, so the heading needs its own gap.
    [AvaloniaFact]
    public void ThereIsRoomBetweenThePositionSliderAndThePressingHeading()
    {
        var (window, view) = Page();
        var slider = Note(view);
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
