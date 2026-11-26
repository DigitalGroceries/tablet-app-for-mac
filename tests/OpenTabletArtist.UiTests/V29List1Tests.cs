using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OtdInterop;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>Build 17 = Digital Groceries' pager task list 1 (21 Nov 2026 ~21:07, 9 Pen page fixes after testing build 16, plus his item 1 note
/// ~21:15), checked against his mockup Mockup_Pen_App_Page.png. Each test names the item it proves.</summary>
public class V29List1Tests
{
    private static Profile NewProfile()
    {
        var menu = RadialMenuDefaults.Create(Localizer.Te);
        var profile = new Profile { Tablet = "List 1 " + Guid.NewGuid().ToString("N")[..8], OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)) };
        profile.BindingSettings.PenButtons = new PluginSettingStoreCollection { HoldKeyboardBinding.Make(), RadialMenuBinding.Make(menu) };
        profile.BindingSettings.AuxButtons = new PluginSettingStoreCollection();
        return profile;
    }

    private static (TabletDetailViewModel Vm, TabletDetailView View, Window Window) Open(double w = 1046, double h = 1365)
    {
        _ = new ThemeViewModel();
        var profile = NewProfile();
        var settings = new Settings { Profiles = new ProfileCollection { profile } };
        var vm = new TabletDetailViewModel(profile, settings, applyAction: _ => System.Threading.Tasks.Task.FromResult(SettingsApplyOutcome.Live));
        vm.ShowHeader = false;
        var view = new TabletDetailView { DataContext = vm };
        var window = new Window { Content = new Border { Child = view, Margin = new Thickness(24, 86, 24, 62) }, Width = w, Height = h };
        window.Show();
        Settle(window);
        return (vm, view, window);
    }

    private static void Settle(Window w) { for (int i = 0; i < 6; i++) { Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); } }

    private static Rect In(Visual v, Visual root) =>
        new(v.TranslatePoint(new Point(0, 0), root) ?? default, v.TranslatePoint(new Point(v.Bounds.Width, v.Bounds.Height), root) ?? default);

    private static T Find<T>(TabletDetailView v, string name) where T : Control => v.FindControl<T>(name)!;

    private static double Alpha(IBrush? b) => b is ISolidColorBrush s ? s.Color.A / 255.0 * s.Opacity : -1;

    [AvaloniaFact]
    public void Items1and2_TheCirclesAreHollowAndSeeThrough_TheSelectedFeelEndIsMoreOpaque_AndTouchDoesNotLightThem()
    {
        var (vm, view, w) = Open();
        try
        {
            var eraser = Find<Border>(view, "EraserEndLabel"); var tip = Find<Border>(view, "TipEndLabel");
            foreach (var c in new[] { eraser, tip })
            {
                Assert.Equal(0, Alpha(c.Background), 3);                       // hollow: nothing hides the pen inside
                Assert.False(c.IsHitTestVisible);
            }
            // Tip Feel selected (the default): the Tip circle is the more opaque one, and both stay see-through
            Assert.False(vm.EraserSettingsShown);
            double tipOn = Alpha(tip.BorderBrush), eraserOff = Alpha(eraser.BorderBrush);
            Assert.True(tipOn > eraserOff + 0.3, $"tip {tipOn} vs eraser {eraserOff}");
            Assert.True(tipOn < 1 && tipOn > 0, $"the selected circle is translucent ({tipOn})");
            // Eraser Feel: the other way round
            Find<RadioButton>(view, "EraserFeelChip").IsChecked = true; Settle(w);
            Assert.True(vm.EraserSettingsShown);
            Assert.Equal(tipOn, Alpha(eraser.BorderBrush), 3);
            Assert.Equal(eraserOff, Alpha(tip.BorderBrush), 3);
            // touching the tablet with either end changes nothing (it used to light the circle)
            var before = (Alpha(eraser.BorderBrush), Alpha(tip.BorderBrush), Alpha(eraser.Background), Alpha(tip.Background));
            vm.EraserPressed = true; vm.TipPressed = true; Settle(w);
            Assert.Equal(before, (Alpha(eraser.BorderBrush), Alpha(tip.BorderBrush), Alpha(eraser.Background), Alpha(tip.Background)));
            Assert.DoesNotContain("pressed", eraser.Classes); Assert.DoesNotContain("pressed", tip.Classes);
        }
        finally { w.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("ru")]   // Наконечник
    [InlineData("de")]
    public void Item3_TheCirclesArePerfectlyRound_InEveryLanguage(string code)
    {
        Localizer.SetPreference(code);
        try
        {
            var (_, view, w) = Open();
            try
            {
                foreach (var name in new[] { "EraserEndLabel", "TipEndLabel" })
                {
                    var c = Find<Border>(view, name);
                    Assert.Equal(c.Bounds.Width, c.Bounds.Height, 1);
                    Assert.Equal(c.Bounds.Width / 2, c.CornerRadius.TopLeft, 1);
                    var word = c.GetVisualDescendants().OfType<TextBlock>().First();
                    var r = In(word, c);
                    Assert.True(r.Left >= 3 && r.Right <= c.Bounds.Width - 3, $"{code} {name}: the word {r} sticks out of the circle");
                }
            }
            finally { w.Close(); }
        }
        finally { Localizer.SetPreference("en"); }
    }

    [AvaloniaFact]
    public void Items4and5_TheCirclesAreBullseyesOnTheEraserCapAndThePoint_WithTheWordsBesideThePen()
    {
        var (_, view, w) = Open();
        try
        {
            var pen = Find<PenDiagram>(view, "PenDrawing");
            Rect Circle(string n) => In(Find<Border>(view, n), view);
            Point P(double x, double y) => pen.TranslatePoint(new Point(x, y), view)!.Value;
            static bool Inside(Rect c, Point p) =>
                Math.Pow(p.X - c.Center.X, 2) + Math.Pow(p.Y - c.Center.Y, 2) <= Math.Pow(c.Width / 2 - 3, 2);   // inside the 3 px ring
            var eraser = Circle("EraserEndLabel"); var tip = Circle("TipEndLabel");
            // the visible eraser cap (23..41 x 0..18 on the drawing; the barrel starts at 18) is inside the Eraser circle; the point (32, 351) and the end of the cone inside the Tip circle
            foreach (var p in new[] { P(23, 2), P(41, 2), P(23, 16), P(41, 16), P(32, 0) }) Assert.True(Inside(eraser, p), $"eraser cap {p} outside {eraser}");
            foreach (var p in new[] { P(32, 351), P(29, 344), P(35, 344), P(32, 340) }) Assert.True(Inside(tip, p), $"tip {p} outside {tip}");
            // concentric with the pen (a bullseye), the words just beyond its ends
            Assert.Equal(P(32, 0).X, eraser.Center.X, 1); Assert.Equal(P(32, 0).X, tip.Center.X, 1);
            var eWord = In(Find<Border>(view, "EraserEndLabel").GetVisualDescendants().OfType<TextBlock>().First(), view);
            var tWord = In(Find<Border>(view, "TipEndLabel").GetVisualDescendants().OfType<TextBlock>().First(), view);
            Assert.InRange(P(32, 0).Y - eWord.Bottom, 0, 10);     // "Eraser" sits right above the cap
            Assert.InRange(tWord.Top - P(32, 351).Y, 0, 10);      // "Tip" right below the point
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void Item6_TheHeadingIsBigger_AndRunsFromThePenToTheRightEdgeOfTheButtons()
    {
        var (vm, view, w) = Open();
        try
        {
            var pen = Find<PenDiagram>(view, "PenDrawing");
            var line = Find<TextBlock>(view, "PenButtonTestLine");
            var cards = In(Find<StackPanel>(view, "PenCardsColumn"), view);
            var barrelLeft = pen.TranslatePoint(new Point(PenDiagram.BarrelLeft, 0), view)!.Value.X;
            var row = In(Find<Grid>(view, "PenButtonTestRow"), view);
            Assert.Equal(barrelLeft, row.Left, 1);
            Assert.Equal(cards.Right, row.Right, 1);
            Assert.True(line.FontSize > 20, $"font {line.FontSize}");
            // one line that fills the width (his mockup: from the pen to the right edge of the buttons)
            Assert.True(line.TextLayout.TextLines.Count == 1, "the idle heading wraps");
            Assert.True(line.TextLayout.WidthIncludingTrailingWhitespace >= row.Width - 1.5, $"{line.TextLayout.WidthIncludingTrailingWhitespace} of {row.Width}");
            // a pressed button's text uses the same size and wraps inside the same width; nothing below moves
            var eraserY = In(Find<Border>(view, "EraserEndLabel"), view).Top;
            var size = line.FontSize;
            vm.PenButton1Pressed = true; vm.PenButton2Pressed = true; Settle(w);
            Assert.Equal(size, line.FontSize, 1);
            Assert.True(line.Bounds.Width <= row.Width + 0.5);
            Assert.Equal(eraserY, In(Find<Border>(view, "EraserEndLabel"), view).Top, 1);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void Item7_TheTransparencySliderIsAsLongAsTheSizeSliderLooks()
    {
        var (vm, view, w) = Open();
        try
        {
            var t = Find<Slider>(view, "RadialTransparencySlider"); var s = Find<Slider>(view, "RadialSizeSlider");
            s.Value = s.Maximum; Settle(w);
            var knob = s.GetVisualDescendants().OfType<Thumb>().First();
            var knobCentre = In(knob, view).Center.X;
            // the size slider's line ends at its knob's centre at 10; the transparency slider's line ends there too
            Assert.Equal(knobCentre, In(t, view).Right, 1.5);
            Assert.Equal(In(t, view).Left, In(s, view).Left, 0.5);
            // the number boxes still line up
            Assert.Equal(In(Find<TextBox>(view, "RadialTransparencyBox"), view).Left, In(Find<TextBox>(view, "RadialSizeBox"), view).Left, 0.5);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void Item8_TheRadialLineStartsWhereRadialMenuSizeStarts()
    {
        var (_, view, w) = Open();
        try
        {
            var lineLeft = In(Find<TextBlock>(view, "RadialSpotLine"), view).Left;
            Assert.Equal(In(Find<TextBlock>(view, "RadialSizeTitle"), view).Left, lineLeft, 0.5);
            Assert.Equal(In(Find<TextBlock>(view, "RadialTransparencyTitle"), view).Left, lineLeft, 0.5);
        }
        finally { w.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(1046, 1365)]
    [InlineData(1280, 900)]
    [InlineData(1500, 900)]
    public void Item9_TheRadialMenuAndThePaletteAreCentredBetweenTheButtonsAndThePageEdge(double width, double height)
    {
        var (_, view, w) = Open(width, height);
        try
        {
            var outer = Find<Grid>(view, "PenPageOuter");
            var pane = outer.FindAncestorOfType<Border>()!;
            double fieldLeft = In(Find<StackPanel>(view, "PenCardsColumn"), view).Right;
            double fieldRight = In(pane, view).Right - pane.BorderThickness.Right;
            double middle = (fieldLeft + fieldRight) / 2;
            Assert.Equal(middle, In(Find<Control>(view, "RadialSpotPie"), view).Center.X, 1);
            Assert.Equal(middle, In(Find<Control>(view, "PaletteSpotWheel"), view).Center.X, 1);
            // and it still stays clear of the button cards
            Assert.True(In(Find<StackPanel>(view, "PenRightColumn"), view).Left >= fieldLeft + V29List1Constants.MinGap - 0.5);
        }
        finally { w.Close(); }
    }
}

internal static class V29List1Constants { public const double MinGap = TabletDetailView.PenRightColumnMinGap; }
