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

    // ── V29 #176 (Zak 20 Nov, build 9): the window stuck at the edge between his screens. His layout: the MacBook's screen, the TV above it.
    private static readonly TitleBarDrag.ScreenArea Mac = new(new PixelRect(0, 0, 1496, 967), new PixelRect(0, 37, 1496, 930));
    private static readonly TitleBarDrag.ScreenArea Tv = new(new PixelRect(-200, -1080, 1920, 1080), new PixelRect(-200, -1055, 1920, 1055));
    private static readonly TitleBarDrag.ScreenArea[] Both = { Mac, Tv };
    private static readonly PixelSize Win = new(1100, 720);

    [Fact]
    public void OnTheSameScreen_TheWindowIsNotPushedAboveTheMenuBar()
    {
        // pointer still on the MacBook's screen, window asked to go 20 points above its top: macOS would push it back, so the app doesn't try
        var at = TitleBarDrag.Place(new PixelPoint(200, -20), Win, new PixelPoint(400, 5), new PixelRect(200, 60, 1100, 720), Both);
        Assert.Equal(new PixelPoint(200, 37), at);
        // an ordinary move on that screen is left alone
        Assert.Equal(new PixelPoint(250, 100), TitleBarDrag.Place(new PixelPoint(250, 100), Win, new PixelPoint(450, 110), new PixelRect(200, 60, 1100, 720), Both));
    }

    [Fact]
    public void OnceThePointerIsOnTheOtherScreen_TheWindowGoesWhollyOntoIt()
    {
        // dragging up: the pointer has crossed onto the TV while the window is still mostly on the MacBook's screen
        var at = TitleBarDrag.Place(new PixelPoint(200, -30), Win, new PixelPoint(400, -10), new PixelRect(200, 37, 1100, 720), Both);
        Assert.Equal(new PixelPoint(200, -720), at);                   // wholly on the TV: its bottom at the TV's bottom edge
        // dragging down: the pointer is on the MacBook's screen, the window still mostly on the TV
        var down = TitleBarDrag.Place(new PixelPoint(300, -10), Win, new PixelPoint(500, 20), new PixelRect(300, -700, 1100, 720), Both);
        Assert.Equal(new PixelPoint(300, 37), down);                    // wholly on the MacBook's screen, below its menu bar
    }

    [Fact]
    public void AfterTheDrag_AWindowAcrossTwoScreens_GoesOntoThePointersScreen()
    {
        Assert.Equal(new PixelPoint(300, 37), TitleBarDrag.SnapAfterDrag(new PixelRect(300, -560, 1100, 720), new PixelPoint(500, 40), Both));
        Assert.Equal(new PixelPoint(300, -720), TitleBarDrag.SnapAfterDrag(new PixelRect(300, -560, 1100, 720), new PixelPoint(500, -500), Both));
        Assert.Null(TitleBarDrag.SnapAfterDrag(new PixelRect(300, 100, 1100, 720), new PixelPoint(500, 120), Both));     // wholly on one screen
        Assert.Null(TitleBarDrag.SnapAfterDrag(new PixelRect(900, 100, 1100, 720), new PixelPoint(1000, 120), Both));    // hangs off the free right edge
        Assert.Equal(new PixelPoint(300, 37), TitleBarDrag.SnapAfterDrag(new PixelRect(300, 10, 1100, 720), new PixelPoint(500, 20), new[] { Mac }));   // under the menu bar
    }

    [Fact]
    public void APlaceTheAppChose_IsNotTakenForTheMacsDrag()
    {
        var d = new TitleBarDrag();
        d.Press(new PixelPoint(200, 300), T0);
        var to = d.Move(new PixelPoint(200, 300), V(0, -40), T0.AddMilliseconds(20));
        Assert.Equal(new PixelPoint(200, 260), to);
        d.PlacedAt(new PixelPoint(200, 37));                            // kept on the screen
        Assert.NotNull(d.Move(new PixelPoint(200, 37), V(0, -10), T0.AddMilliseconds(40)));
        Assert.True(d.ByApp);
    }

    [Fact]
    public void ALowerWindow_IsAMenuBarPush_OnlyAtTheScreensUsableTop()
    {
        // Grok's review of #176: lower than where the app put it AND at the usable top = macOS pushed it from under the menu bar: keep going
        var d = new TitleBarDrag();
        d.Press(new PixelPoint(200, 100), T0);
        Assert.Equal(new PixelPoint(200, 20), d.Move(new PixelPoint(200, 100), V(0, -80), T0.AddMilliseconds(20)));
        Assert.NotNull(d.Move(new PixelPoint(200, 37), V(0, -5), T0.AddMilliseconds(40), workTop: 37));
        Assert.True(d.ByApp);
        // lower anywhere else = the Mac's own drag started late going down: hand over
        var e = new TitleBarDrag();
        e.Press(new PixelPoint(200, 300), T0);
        Assert.Equal(new PixelPoint(200, 340), e.Move(new PixelPoint(200, 300), V(0, 40), T0.AddMilliseconds(20)));
        Assert.Null(e.Move(new PixelPoint(200, 380), V(0, 5), T0.AddMilliseconds(40), workTop: 37));
        Assert.False(e.Pressed);
        Assert.Equal(37, TitleBarDrag.WorkTopOf(new PixelRect(200, 300, 1100, 720), Both));
        Assert.Equal(-1055, TitleBarDrag.WorkTopOf(new PixelRect(200, -900, 1100, 720), Both));
    }
}
