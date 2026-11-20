using System;
using Avalonia;
using OpenTabletArtist.Helpers;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>V29 #164 (Zak 20 Nov, report 12:01): the app's window moves with the pen and every mouse drag, also when the Mac's own
/// drag doesn't start.</summary>
public class TitleBarDragTests
{
    private static readonly DateTime T0 = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void WhenTheMacsDragDoesNotStart_TheAppMovesTheWindowWithThePointer()
    {
        var d = new TitleBarDrag();
        d.Press(new PixelPoint(100, 50), new PixelPoint(400, 60), T0);
        Assert.Null(d.Move(new PixelPoint(100, 50), new PixelPoint(403, 61), T0.AddMilliseconds(20)));   // a wobble, not a drag
        Assert.Equal(new PixelPoint(60, 40), d.Move(new PixelPoint(100, 50), new PixelPoint(360, 50), T0.AddMilliseconds(60)));
        Assert.True(d.ByApp);
        // once moving, it follows the pointer even though the window is no longer where it started
        Assert.Equal(new PixelPoint(-100, 90), d.Move(new PixelPoint(60, 40), new PixelPoint(200, 100), T0.AddMilliseconds(900)));
        Assert.True(d.Release());
        Assert.False(d.Pressed);
    }

    [Fact]
    public void WhenTheMacsDragMovesTheWindow_TheAppLeavesItAlone()
    {
        var d = new TitleBarDrag();
        d.Press(new PixelPoint(100, 50), new PixelPoint(400, 60), T0);
        Assert.Null(d.Move(new PixelPoint(80, 50), new PixelPoint(380, 60), T0.AddMilliseconds(40)));   // the window already moved
        Assert.False(d.Pressed);
        Assert.Null(d.Move(new PixelPoint(80, 50), new PixelPoint(300, 60), T0.AddMilliseconds(80)));
        Assert.False(d.Release());
    }

    [Fact]
    public void ALatePointerMove_DoesNotStartADrag()
    {
        // The Mac's drag can swallow the release: a later hover with the button seemingly held must not move the window.
        var d = new TitleBarDrag();
        d.Press(new PixelPoint(100, 50), new PixelPoint(400, 60), T0);
        Assert.Null(d.Move(new PixelPoint(100, 50), new PixelPoint(300, 60), T0 + TitleBarDrag.TakeOverWithin + TimeSpan.FromMilliseconds(1)));
        Assert.False(d.Pressed);
    }
}
