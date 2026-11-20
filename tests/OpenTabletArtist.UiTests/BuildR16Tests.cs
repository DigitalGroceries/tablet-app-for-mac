using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>Build R16 (the user's mock-up): dark inset Movement/Pen input frame, pen-button cards on the Eraser tab
/// (the same shared rows), flipped pen drawing with upright badges, redrawn cone + nib.</summary>
public class BuildR16Tests
{
    private static (TabletDetailViewModel, TabletDetailView, Window) Open(string tab)
    {
        _ = new ThemeViewModel();
        var profile = new Profile { Tablet = "Wacom PTK-440", OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)) };
        var settings = new OpenTabletDriver.Desktop.Settings { Profiles = new ProfileCollection { profile } };
        var vm = new TabletDetailViewModel(profile, settings);
        vm.PenButton1Row = new PenSwitchRowViewModel(PenSwitchKind.PenButton, 1, null, false, (_, _, _) => Task.CompletedTask);
        vm.PenButton2Row = new PenSwitchRowViewModel(PenSwitchKind.PenButton, 2, null, false, (_, _, _) => Task.CompletedTask);
        var view = new TabletDetailView { DataContext = vm };
        var w = new Window { Content = view, Width = 1100, Height = 1100 }; w.Show();
        view.FindControl<RadioButton>(tab)!.IsChecked = true;
        Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        return (vm, view, w);
    }

    [AvaloniaFact]
    public void BasicsFrame_IsDarkerInset_WithSubtleBorder()
    {
        var (_, view, w) = Open("PenTab");
        var b = view.FindControl<Border>("PenBasicsBox")!;
        var bg = ((ISolidColorBrush)b.Background!).Color; var br = ((ISolidColorBrush)b.BorderBrush!).Color;
        Assert.Equal(Color.Parse("#262626"), bg);
        Assert.Equal(Color.Parse("#4A4A4A"), br);
        Assert.Equal(1, b.BorderThickness.Top);
        w.Close();
    }

    [AvaloniaFact]
    public void EraserTab_HasTheSameButtonCards_InFlippedOrder()
    {
        var (vm, view, w) = Open("EraserTab");
        var cards = view.FindControl<StackPanel>("EraserCards")!.Children.OfType<PenSwitchCard>().Where(c => c.IsVisible).ToList();
        Assert.Same(vm.PenButton1Row, cards[0].DataContext);
        Assert.Same(vm.PenButton2Row, cards[1].DataContext);
        Assert.Same(vm.PenEraserRow, cards[^1].DataContext);
        var tops = cards.Select(c => c.TranslatePoint(new Point(0, 0), w)!.Value.Y).ToList();
        Assert.True(tops[0] < tops[1] && tops[1] < tops[^1]);
        w.Close();
    }

    /// <summary>V29 (Zak 16 Nov 16:11: 'the Buttons column on the Pen and Eraser pages is squished at the right, its text is
    /// cut ... Move the column right next to the pen and show the full text'; missed in V29 builds 1-2, Zak 19 Nov): on both
    /// pages the cards start right next to the pen drawing, and a long action is shown in full, never cut with an ellipsis.</summary>
    /// <summary>V29 #141 (Zak 18:46): the floating keyboard's transparency has a slider and a typed box on the Pen page, lined up
    /// with the radial menu's own rows.</summary>
    [AvaloniaFact]
    public void KeyboardTransparency_HasASliderAndABox_InLineWithTheMenuRows()
    {
        var (vm, view, w) = Open("KeyboardTab"); // #177 (Zak 20 Nov): the keyboard settings have their own tab now
        try
        {
            var slider = view.FindControl<Slider>("KeyboardTransparencySlider")!;
            var box = view.FindControl<TextBox>("KeyboardTransparencyBox")!;
            Assert.Equal(60, slider.Maximum);
            vm.KeyboardLook.Transparency = 33;
            Assert.Equal(33, slider.Value);
            Assert.Equal("33", box.Text);
            var menuBox = view.FindControl<TextBox>("RadialTransparencyBox")!;
            Assert.Equal(menuBox.Width, box.Width);
            Assert.Equal(view.FindControl<Grid>("RadialTransparencyRow")!.ColumnDefinitions.ToString(),
                         view.FindControl<Grid>("KeyboardTransparencyRow")!.ColumnDefinitions.ToString());
        }
        finally
        {
            vm.KeyboardLook.Transparency = FloatingKeyboard.DefaultTransparency;
            w.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("EraserTab", "EraserPenDiagram")]
    [InlineData("PenTab", null)]
    public void ButtonCards_SitNextToThePen_AndShowTheFullText(string tab, string? diagramName)
    {
        using var mac = UiPlatform.PretendMac();   // the value box (Change… cards) is a macOS feature
        var (vm, view, w) = Open(tab);
        Func<PenSwitchRowViewModel, Task> choose = _ => Task.CompletedTask;   // a real page can change the buttons: the value box shows
        vm.PenButton1Row = new PenSwitchRowViewModel(PenSwitchKind.PenButton, 1, null, true, (_, _, _) => Task.CompletedTask, choose);
        vm.PenButton2Row = new PenSwitchRowViewModel(PenSwitchKind.PenButton, 2, null, true, (_, _, _) => Task.CompletedTask, choose);
        const string longText = "Radial menu: Undo, Redo, Copy, Paste, Zoom in, Zoom out…";
        vm.PenButton2Row!.CurrentPlainLabel = longText;
        Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        var diagram = diagramName != null ? view.FindControl<PenDiagram>(diagramName)!
            : view.GetVisualDescendants().OfType<PenDiagram>().First(d => d.IsEffectivelyVisible && !d.Flipped);
        var penRight = diagram.TranslatePoint(new Point(diagram.Bounds.Width, 0), w)!.Value.X;
        var cards = view.GetVisualDescendants().OfType<PenSwitchCard>().Where(c => c.IsEffectivelyVisible).ToList();
        Assert.NotEmpty(cards);
        foreach (var c in cards)
        {
            var left = c.TranslatePoint(new Point(0, 0), w)!.Value.X;
            Assert.True(left - penRight <= 40, $"card starts {left - penRight:0} pt right of the pen (should sit next to it)");
        }
        var card2 = cards.First(c => ReferenceEquals(c.DataContext, vm.PenButton2Row));
        var text = card2.FindControl<Border>("ValueBox")!.Child as TextBlock;
        Assert.NotNull(text);
        Assert.Equal(longText, text!.Text);
        Assert.Equal(TextTrimming.None, text.TextTrimming);
        Assert.True(text.Bounds.Width > 0, "the value box is not shown");
        var layout = text.TextLayout;
        Assert.True(layout.TextLines.Count >= 2, "the long text should wrap onto more lines, not run past the box");
        Assert.True(layout.TextLines.Sum(l => l.Length) >= longText.Length, "some characters were cut");   // every character laid out (the last line may count its end mark)
        Assert.True(layout.Height <= text.Bounds.Height + 0.5, $"the text is cut off at the bottom: {layout.Height} > {text.Bounds.Height}");
        var box = card2.FindControl<Border>("ValueBox")!;
        Assert.True(box.Bounds.Height >= text.Bounds.Height, "the value box does not grow with its text");
        w.Close();
    }

    [AvaloniaFact]
    public void FlippedDiagram_RotatesShapes_BadgesUpright_OnTheirButtons()
    {
        var (_, view, w) = Open("EraserTab");
        var d = view.FindControl<PenDiagram>("EraserPenDiagram")!;
        Assert.True(d.Flipped);
        var shapes = d.FindControl<Canvas>("Shapes")!;
        Assert.Equal(180, ((RotateTransform)shapes.RenderTransform!).Angle);
        var b1 = d.FindControl<Border>("Badge1")!; var b2 = d.FindControl<Border>("Badge2")!;
        Assert.Null(b1.RenderTransform);
        Assert.Equal(360 - 204 - 26, Canvas.GetTop(b1)); Assert.Equal(360 - 172 - 26, Canvas.GetTop(b2));
        Assert.True(Canvas.GetTop(b1) < Canvas.GetTop(b2));   // tip now up: button 1 (nearest the tip) is the upper one
        var upright = new PenDiagram(); Assert.True(PenDiagram.BadgeTop(1, false) > PenDiagram.BadgeTop(2, false));
        w.Close();
    }

    [AvaloniaFact]
    public void Tip_IsSeparateCone_WithFlatEnd_AndSolidNib()
    {
        var d = new PenDiagram();
        var cone = d.FindControl<Avalonia.Controls.Shapes.Path>("Cone")!.Data!.Bounds;
        var nib = d.FindControl<Avalonia.Controls.Shapes.Path>("Nib")!;
        Assert.True(cone.Width >= 28 && cone.Height / 28 is > 1.8 and < 2.3);
        Assert.Null(nib.Stroke);
        Assert.True(nib.Data!.Bounds.Width <= 8 && nib.Data.Bounds.Top >= cone.Bottom);
    }

}
