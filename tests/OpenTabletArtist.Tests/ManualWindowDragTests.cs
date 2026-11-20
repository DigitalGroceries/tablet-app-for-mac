using Avalonia;
using OpenTabletArtist.Helpers;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>V29 #176 build 12: on macOS 27.2+ the window follows the pointer by hand (the Mac's own drag of custom title bars is
/// unreliable there: warpdotdev/warp#16508): new place = place at the press + how far the pointer moved, nothing else.</summary>
public class ManualWindowDragTests
{
    [Fact]
    public void TheWindowMovesExactlyAsFarAsThePointer_AcrossScreensToo()
    {
        var d = new ManualWindowDrag();
        d.Press(new PixelPoint(300, 100), new PixelPoint(500, 115));
        Assert.Null(d.Move(new PixelPoint(501, 116)));                       // a wobble: a click moves nothing
        Assert.False(d.Moving);
        Assert.Equal(new PixelPoint(320, 60), d.Move(new PixelPoint(520, 75)));
        Assert.Equal(new PixelPoint(310, -700), d.Move(new PixelPoint(510, -685)));   // up onto the TV: no jump, no clamp
        Assert.Null(d.Move(new PixelPoint(510, -685)));                      // same place: nothing to do
        Assert.True(d.Release());
        Assert.False(d.Pressed);
        Assert.Null(d.Move(new PixelPoint(600, 600)));                       // after the release nothing moves
    }

    [Fact]
    public void AClickWithoutMoving_ReportsNoDrag()
    {
        var d = new ManualWindowDrag();
        d.Press(new PixelPoint(0, 0), new PixelPoint(10, 10));
        Assert.Null(d.Move(new PixelPoint(12, 11)));
        Assert.False(d.Release());
    }

    [Fact]
    public void AfterTheDrag_ATopUnderTheMenuBar_GoesDown()
    {
        var mac = new TitleBarDrag.ScreenArea(new PixelRect(0, 0, 1496, 967), new PixelRect(0, 37, 1496, 930));
        var tv = new TitleBarDrag.ScreenArea(new PixelRect(-200, -1080, 1920, 1080), new PixelRect(-200, -1055, 1920, 1055));
        var both = new[] { mac, tv };
        Assert.Equal(new PixelPoint(300, 37), TitleBarDrag.BelowMenuBar(new PixelRect(300, 10, 1100, 720), both));
        Assert.Null(TitleBarDrag.BelowMenuBar(new PixelRect(300, -500, 1100, 720), both));   // mostly on the TV, straddling: left alone
        Assert.Null(TitleBarDrag.BelowMenuBar(new PixelRect(300, 100, 1100, 720), both));
    }
}
