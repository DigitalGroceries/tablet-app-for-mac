using System;
using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>V29 #174 (Zak 20 Nov, build 8: the radial menu and the palette drew twice too big or too small after moving between his
/// Retina screen and the TV, and "fix themselves as long as you continue clicking"): a menu, palette or keyboard that goes to a screen of
/// another scale gets a new window made for that screen.</summary>
[Collection("FloatingKeyboardFiles")]
public class ScreenScaleRemakeTests
{
    private static RadialMenuOverlay.MenuState At(long seq, double x, string kind = "menu", bool moving = false) =>
        new("open", seq, x, 400, 50, 0,
            new[] { "Undo", "Redo", "Copy", "Paste", "Cut", "Select all", "Zoom out", "Zoom in" }.ToList(),
            Enumerable.Repeat(true, 8).ToList(), Enumerable.Repeat(false, 8).ToList(), Kind: kind, Moving: moving);

    private static void Pump() { for (int i = 0; i < 4; i++) Dispatcher.UIThread.RunJobs(); }

    // the built-in Retina screen left of x = 1000, the TV right of it
    private static double FakeScale(double x) => x < 1000 ? 2 : 1;

    [AvaloniaFact]
    public void TheMenuAndThePalette_GetANewWindow_OnAScreenOfAnotherScale_AndNotOnTheSameScreen()
    {
        _ = new ThemeViewModel();
        var keep = RadialMenuOverlay.ScaleAt; var keepR = RadialMenuOverlay.RenderScaleOf;
        RadialMenuOverlay.ScaleAt = (_, x, _) => FakeScale(x);
        RadialMenuOverlay.RenderScaleOf = w => FakeScale(w.Position.X + w.Width / 2);   // a window draws for the screen it is on
        foreach (var kind in new[] { "menu", "palette" })
        {
            var overlay = new RadialMenuOverlay(palette: kind == "palette");
            try
            {
                overlay.ShowNowForTest(At(1, 500, kind)); Pump();
                var first = overlay.MenuWindow;
                Assert.NotNull(first);
                overlay.ShowNowForTest(At(2, 700, kind)); Pump();          // same screen: same window
                Assert.Same(first, overlay.MenuWindow);
                Assert.Equal(0, overlay.Remade);
                overlay.ShowNowForTest(At(3, 1500, kind)); Pump();         // the TV: made again
                Assert.Equal(1, overlay.Remade);
                Assert.NotSame(first, overlay.MenuWindow);
                Assert.True(overlay.MenuWindow!.IsVisible);
                overlay.ShowNowForTest(At(4, 300, kind)); Pump();          // back on the Retina screen: made again
                Assert.Equal(2, overlay.Remade);
            }
            finally { overlay.Dispose(); }
        }
        RadialMenuOverlay.ScaleAt = keep; RadialMenuOverlay.RenderScaleOf = keepR;
    }

    [AvaloniaFact]
    public void ManyShowsOnTheNewScreen_BeforeTheMacUpdatesTheDrawingScale_MakeTheWindowOnlyOnce()
    {
        // Grok's review (finding 1): the Mac tells a moved window its new scale a moment later. Every pen move writes a state, so
        // several shows come before that; none of them may make the window again.
        _ = new ThemeViewModel();
        var keep = RadialMenuOverlay.ScaleAt; var keepR = RadialMenuOverlay.RenderScaleOf;
        RadialMenuOverlay.ScaleAt = (_, x, _) => FakeScale(x);
        RadialMenuOverlay.RenderScaleOf = w => FakeScale(w.Position.X + w.Width / 2);
        var overlay = new RadialMenuOverlay(palette: false);
        try
        {
            overlay.ShowNowForTest(At(1, 500)); Pump();
            for (int i = 0; i < 5; i++) overlay.ShowNowForTest(At(2 + i, 1500 + i));   // no pumping between them
            Pump();
            Assert.Equal(1, overlay.Remade);
        }
        finally { overlay.Dispose(); RadialMenuOverlay.ScaleAt = keep; RadialMenuOverlay.RenderScaleOf = keepR; }
    }

    [AvaloniaFact]
    public void AWindowThatStillDrawsAtTheWrongScale_IsMadeAgainOnce_ThenLeft()
    {
        _ = new ThemeViewModel();
        var keep = RadialMenuOverlay.ScaleAt; var keepR = RadialMenuOverlay.RenderScaleOf;
        RadialMenuOverlay.ScaleAt = (_, x, _) => FakeScale(x);
        RadialMenuOverlay.RenderScaleOf = _ => 2;                     // stuck at the Retina scale, even on the TV
        var overlay = new RadialMenuOverlay(palette: false);
        try
        {
            overlay.ShowNowForTest(At(1, 500)); Pump();
            Assert.Equal(0, overlay.Remade);
            overlay.ShowNowForTest(At(2, 1500)); Pump();               // the screen change, then one more after the check
            Assert.Equal(2, overlay.Remade);
            for (int i = 0; i < 5; i++) { overlay.ShowNowForTest(At(3 + i, 1500 + i)); Pump(); }
            Assert.Equal(2, overlay.Remade);                            // no loop
            Assert.True(overlay.MenuWindow!.IsVisible);
        }
        finally { overlay.Dispose(); RadialMenuOverlay.ScaleAt = keep; RadialMenuOverlay.RenderScaleOf = keepR; }
    }

    [AvaloniaFact]
    public void APenDragAcrossScreens_KeepsItsWindow_UntilTheDragEnds()
    {
        // Grok's review (finding 2): the driver says "moving" while the pen drags the menu; the window is made again once, when it ends.
        _ = new ThemeViewModel();
        var keep = RadialMenuOverlay.ScaleAt; var keepR = RadialMenuOverlay.RenderScaleOf;
        RadialMenuOverlay.ScaleAt = (_, x, _) => FakeScale(x);
        RadialMenuOverlay.RenderScaleOf = w => FakeScale(w.Position.X + w.Width / 2);
        var overlay = new RadialMenuOverlay(palette: true);
        try
        {
            overlay.ShowNowForTest(At(1, 900, "palette")); Pump();
            var first = overlay.MenuWindow;
            foreach (var x in new[] { 960.0, 1010, 990, 1020, 1100 })   // back and forth over the edge
            { overlay.ShowNowForTest(At(2, x, "palette", moving: true)); Pump(); }
            Assert.Equal(0, overlay.Remade);
            Assert.Same(first, overlay.MenuWindow);
            overlay.ShowNowForTest(At(3, 1100, "palette", moving: false)); Pump();   // let go on the TV
            Assert.Equal(1, overlay.Remade);
            Assert.True(overlay.MenuWindow!.IsVisible);
        }
        finally { overlay.Dispose(); RadialMenuOverlay.ScaleAt = keep; RadialMenuOverlay.RenderScaleOf = keepR; }
    }

    [AvaloniaFact]
    public void TheDriversMovingFlag_IsRead()
    {
        var st = RadialMenuOverlay.Parse("{\"state\":\"open\",\"seq\":3,\"x\":10,\"y\":20,\"moving\":true}");
        Assert.True(st!.Moving);
        Assert.False(RadialMenuOverlay.Parse("{\"state\":\"open\",\"seq\":3,\"x\":10,\"y\":20}")!.Moving);
    }

    [AvaloniaFact]
    public void TheKeyboard_GetsANewWindow_WhenItOpensOnAScreenOfAnotherScale()
    {
        _ = new ThemeViewModel();
        var keep = FloatingKeyboard.ScaleAt;
        FloatingKeyboard.ScaleAt = (_, x, _) => FakeScale(x);
        var kb = new FloatingKeyboard();
        try
        {
            kb.Show(500, 300); Pump();
            var first = kb.Window;
            kb.Hide();
            kb.Show(600, 300); Pump();
            Assert.Same(first, kb.Window);
            kb.Hide();
            kb.Show(1600, 300); Pump();
            Assert.Equal(1, kb.Remade);
            Assert.NotSame(first, kb.Window);
            Assert.True(kb.Window!.IsVisible);
        }
        finally { kb.Dispose(); FloatingKeyboard.ScaleAt = keep; }
    }
}
