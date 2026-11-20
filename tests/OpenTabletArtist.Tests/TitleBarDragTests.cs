using System;
using Avalonia;
using OpenTabletArtist.Helpers;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>V29 #164 (Zak 20 Nov, report 12:01): the app's window moves with the pen and every mouse drag, also when the Mac's own
/// drag doesn't start; Grok's review: moves by the pointer's distance from the grabbed spot, hands back to a late Mac drag.</summary>
public class TitleBarDragTests
{
    private static readonly DateTime T0 = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);
    private static PixelVector V(int x, int y) => new(x, y);

    [Fact]
    public void WhenTheMacsDragDoesNotStart_TheWindowFollowsThePointer()
    {
        var d = new TitleBarDrag();
        d.Press(new PixelPoint(100, 50), T0);
        Assert.Null(d.Move(new PixelPoint(100, 50), V(3, 1), T0.AddMilliseconds(20)));     // a wobble, not a drag
        var p = d.Move(new PixelPoint(100, 50), V(-40, -10), T0.AddMilliseconds(60));
        Assert.Equal(new PixelPoint(60, 40), p);
        Assert.True(d.ByApp);
        // the window is where the app put it; the pointer is again 30 px from the grabbed spot: the window follows by 30
        Assert.Equal(new PixelPoint(90, 40), d.Move(p!.Value, V(30, 0), T0.AddMilliseconds(900)));
        // a scale change between screens doesn't matter: only the distance from the grabbed spot, in screen units, is used
        Assert.Equal(new PixelPoint(90, 140), d.Move(new PixelPoint(90, 40), V(0, 100), T0.AddMilliseconds(950)));
        Assert.True(d.Release());
        Assert.False(d.Pressed);
    }

    [Fact]
    public void WhenTheMacsDragMovesTheWindow_TheAppLeavesItAlone()
    {
        var d = new TitleBarDrag();
        d.Press(new PixelPoint(100, 50), T0);
        Assert.Null(d.Move(new PixelPoint(80, 50), V(-20, 0), T0.AddMilliseconds(40)));   // the window already moved
        Assert.False(d.Pressed);
        Assert.False(d.Release());
    }

    [Fact]
    public void WhenTheMacsDragStartsLate_TheAppHandsBack()
    {
        var d = new TitleBarDrag();
        d.Press(new PixelPoint(100, 50), T0);
        var p = d.Move(new PixelPoint(100, 50), V(-20, 0), T0.AddMilliseconds(40))!.Value;
        Assert.Null(d.Move(new PixelPoint(p.X - 15, p.Y), V(-5, 0), T0.AddMilliseconds(80)));   // someone else moved it
        Assert.False(d.Pressed);
        Assert.False(d.ByApp);
    }

    [Fact]
    public void AWindowPushedDownFromUnderTheMenuBar_IsNotAHandOver()
    {
        var d = new TitleBarDrag();
        d.Press(new PixelPoint(100, 30), T0);
        var p = d.Move(new PixelPoint(100, 30), V(0, -20), T0.AddMilliseconds(40))!.Value;   // asked for y 10
        Assert.Equal(new PixelPoint(100, 35), d.Move(new PixelPoint(100, 25), V(0, 10), T0.AddMilliseconds(80)));   // macOS kept it at 25
        Assert.True(d.ByApp);
        _ = p;
    }

    [Fact]
    public void ALatePointerMove_DoesNotStartADrag()
    {
        // The Mac's drag can swallow the release: a later hover with the button seemingly held must not move the window.
        var d = new TitleBarDrag();
        d.Press(new PixelPoint(100, 50), T0);
        Assert.Null(d.Move(new PixelPoint(100, 50), V(-100, 0), T0 + TitleBarDrag.TakeOverWithin + TimeSpan.FromMilliseconds(1)));
        Assert.False(d.Pressed);
    }
}
