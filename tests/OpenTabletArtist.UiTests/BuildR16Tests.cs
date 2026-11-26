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
        PenPageTabs.Select(view, tab);
        Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        return (vm, view, w);
    }

    [AvaloniaFact]
    public void BasicsFrame_IsDarkerInset_WithSubtleBorder()
    {
        var (_, view, w) = Open("PenTab");
        var b = view.FindControl<Border>("PenBasicsBox")!;
        var bg = ((ISolidColorBrush)b.Background!).Color;
        // a little darker than the page it is sunk into (#262626 on his #343434, measured on the mockup)
        var page = LightnessService.For(LightnessService.Saved).Pane;
        Assert.Equal(Color.FromRgb((byte)(page.R - 0x0E), (byte)(page.G - 0x0E), (byte)(page.B - 0x0E)), bg);
        // V31 (task list L7.17, the mockup of 25 Nov 2026): no outline any more. The tray is sunk: a light band inside its
        // right and bottom edges (12.5 wide, 9.5 high) and a soft shade inside its top and left edges.
        Assert.Equal(0, b.BorderThickness.Top);
        Assert.Equal(16, b.CornerRadius.TopLeft);
        var shadows = Enumerable.Range(0, b.BoxShadow.Count).Select(i => b.BoxShadow[i]).ToList();
        Assert.All(shadows, s => Assert.True(s.IsInset));
        Assert.Contains(shadows, s => s.OffsetX == -12.5 && s.OffsetY == 0 && s.Color.R > 0xC0);
        Assert.Contains(shadows, s => s.OffsetX == 0 && s.OffsetY == -9.5 && s.Color.R > 0xC0);
        Assert.Contains(shadows, s => s.OffsetX > 0 && s.OffsetY > 0 && s.Color.R == 0);
        w.Close();
    }

    /// <summary>V29 #180 (Digital Groceries' Pen page mockup, 21 Nov): the eraser's action sits by the Eraser label, above BUTTON 2 and BUTTON 1,
    /// and the middle column is the same whichever end's settings are shown ("the 'Tip Feel' and the 'Eraser Feel' buttons share
    /// the same pen buttons setting").</summary>
    [AvaloniaFact]
    public void PenPage_EraserRowAboveTheButtons_SameForTipAndEraser()
    {
        var (vm, view, w) = Open("PenTab");
        try
        {
            var row = view.FindControl<StackPanel>("EraserActionRow")!;
            var cards = view.FindControl<StackPanel>("PenCardsColumn")!.Children.OfType<PenSwitchCard>().Where(c => c.IsVisible).ToList();
            Assert.Same(vm.PenButton2Row, cards[0].DataContext);
            Assert.Same(vm.PenButton1Row, cards[1].DataContext);
            double Y(Control c) => c.TranslatePoint(new Point(0, 0), w)!.Value.Y;
            Assert.True(Y(row) < Y(cards[0]) && Y(cards[0]) < Y(cards[1]));
            var before = cards.Select(Y).ToList();
            // V29 #180 (Grok 21 Nov): nothing below or beside moves either: the Movement box, the Manual Pressure frame, the right column.
            double X(Control c) => c.TranslatePoint(new Point(0, 0), w)!.Value.X;
            var movement = view.GetVisualDescendants().OfType<ComboBox>().First(c => c.Name == "MovementBox");
            var movementY = Y(movement);
            var manualY = Y(view.FindControl<Control>("ManualTipPressureFrame")!);
            var radialX = X(view.FindControl<Control>("RadialSpot")!);
            PenPageTabs.Select(view, "EraserTab"); w.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Assert.True(row.IsEffectivelyVisible);
            Assert.Equal(before, cards.Select(Y).ToList());
            Assert.All(cards, c => Assert.True(c.IsEffectivelyVisible));
            Assert.Equal(movementY, Y(movement));
            Assert.Equal(manualY, Y(view.FindControl<Control>("ManualEraserPressureFrame")!));
            Assert.Equal(radialX, X(view.FindControl<Control>("RadialSpot")!));
            // The test line wraps inside the drawing-and-cards width, so a pressed button never moves the right column.
            var line = view.FindControl<TextBlock>("PenButtonTestLine")!;
            Assert.Equal(TextWrapping.Wrap, line.TextWrapping);
            Assert.True(line.Bounds.Width <= view.FindControl<Grid>("PenMiddleBody")!.Bounds.Width + 0.5);
            // ...and its row keeps the height of the tallest text it can show (Grok 21 Nov: in fr and ru the drawing jumped up
            // ~18 px when BUTTON 2 PRESSED. took one line instead of two).
            var sizer = view.FindControl<ItemsControl>("PenButtonTestSizer")!;
            Assert.Equal(4, sizer.ItemCount);
            var eraserY = Y(view.FindControl<Control>("EraserEndLabel")!);
            vm.PenButton2Pressed = true; w.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(radialX, X(view.FindControl<Control>("RadialSpot")!));
            Assert.Equal(eraserY, Y(view.FindControl<Control>("EraserEndLabel")!));
            vm.PenButton1Pressed = true; w.UpdateLayout(); Dispatcher.UIThread.RunJobs();   // "BUTTONS 1 AND 2 PRESSED."
            Assert.Equal(eraserY, Y(view.FindControl<Control>("EraserEndLabel")!));
            vm.PenButton1Pressed = false; vm.PenButton2Pressed = false;
        }
        finally { w.Close(); }
    }

    /// <summary>V29 (Digital Groceries 16 Nov 16:11: 'the Buttons column on the Pen and Eraser pages is squished at the right, its text is
    /// cut ... Move the column right next to the pen and show the full text'; missed in V29 builds 1-2, Digital Groceries 19 Nov): on both
    /// pages the cards start right next to the pen drawing, and a long action is shown in full, never cut with an ellipsis.</summary>
    /// <summary>V29 #141 (Digital Groceries 18:46): the floating keyboard's transparency has a slider and a typed box on the Pen page, lined up
    /// with the radial menu's own rows.</summary>
    [AvaloniaFact]
    public void KeyboardTransparency_HasASliderAndABox_InLineWithTheMenuRows()
    {
        var (vm, view, w) = Open("KeyboardTab"); // #177 (Digital Groceries 20 Nov): the keyboard settings have their own tab now
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
        // V31 (L7.19): the words sit in a LayoutTransformControl (drawn 1.2 times as wide)
        var text = card2.FindControl<Border>("ValueBox")!.GetVisualDescendants().OfType<TextBlock>().SingleOrDefault();
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
        // V29 #180: no page shows the flipped drawing any more (the Eraser tab is gone); the control keeps the option.
        var d = new PenDiagram { Flipped = true };
        var w = new Window { Content = d, Width = 200, Height = 400 }; w.Show(); Dispatcher.UIThread.RunJobs();
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
