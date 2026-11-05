using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenTabletArtist.Dynamics;
using Xunit;
using static OpenTabletArtist.Dynamics.TouchButtonAction;

namespace OpenTabletArtist.Tests;

/// <summary>Discussion #31 (Jay, PTH-651, 2 Nov 2026): finger taps and drags on the touch trackpad.</summary>
public class TouchTapDragTests
{
    static readonly DateTime T0 = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
    static DateTime Ms(int ms) => T0.AddMilliseconds(ms);
    static readonly Vector2 P = new(50, 30);

    /// <summary>Runs touch steps (time ms, fingers, x offset mm) and the timer every 50 ms, collecting the actions.</summary>
    static List<TouchButtonAction> Run(TouchTapGestures g, params (int ms, int fingers, float dx)[] steps)
    {
        var acts = new List<TouchButtonAction>();
        foreach (var (ms, fingers, dx) in steps)
            acts.AddRange(g.Touch(fingers, P + new Vector2(dx, 0), Ms(ms)));
        var end = steps.Max(s => s.ms);
        for (var t = end; t <= end + 1000; t += 50) acts.AddRange(g.Tick(Ms(t)));
        return acts;
    }

    [Fact]
    public void One_finger_tap_clicks_once_after_the_second_tap_window()
    {
        var g = new TouchTapGestures();
        Assert.Empty(g.Touch(1, P, Ms(0)));
        Assert.Empty(g.Touch(0, P, Ms(100)));               // lifted: click waits for a possible second tap
        Assert.Empty(g.Tick(Ms(300)));
        Assert.Equal(new[] { LeftClick }, g.Tick(Ms(400)));
        Assert.False(g.Busy);
    }

    [Fact]
    public void A_long_touch_or_a_slide_is_not_a_tap()
    {
        Assert.Empty(Run(new TouchTapGestures(), (0, 1, 0), (400, 1, 0), (420, 0, 0)));
        Assert.Empty(Run(new TouchTapGestures(), (0, 1, 0), (60, 1, 5), (100, 0, 5)));
    }

    [Fact]
    public void Tap_then_touch_and_move_drags_and_lets_go_on_lift()
    {
        var g = new TouchTapGestures();
        var acts = Run(g, (0, 1, 0), (80, 0, 0), (200, 1, 0), (300, 1, 4), (600, 1, 12), (700, 0, 12));
        Assert.Equal(new[] { LeftDown, LeftUp }, acts);
        Assert.False(g.Dragging);
    }

    [Fact]
    public void Two_quick_taps_double_click()
    {
        var acts = Run(new TouchTapGestures(), (0, 1, 0), (80, 0, 0), (200, 1, 0), (260, 0, 0));
        Assert.Equal(new[] { LeftDown, LeftUp, LeftSecondClick }, acts);
    }

    [Fact]
    public void Two_finger_tap_right_clicks_and_two_finger_scroll_does_not()
    {
        Assert.Equal(new[] { RightClick }, Run(new TouchTapGestures(), (0, 1, 0), (20, 2, 0), (120, 2, 0.5f), (140, 0, 0)));
        Assert.Empty(Run(new TouchTapGestures(), (0, 2, 0), (100, 2, 8), (200, 0, 8)));
    }

    [Fact]
    public void A_late_second_touch_is_a_new_gesture_and_the_first_click_still_happens()
    {
        var g = new TouchTapGestures();
        g.Touch(1, P, Ms(0)); g.Touch(0, P, Ms(80));
        Assert.Equal(new[] { LeftClick }, g.Tick(Ms(400)));   // window over: click
        Assert.Empty(g.Touch(1, P, Ms(500)));                 // a fresh touch, not a drag
        Assert.False(g.Dragging);
    }

    [Fact]
    public void Two_fingers_during_a_waiting_click_send_the_click_first()
    {
        var g = new TouchTapGestures();
        g.Touch(1, P, Ms(0)); g.Touch(0, P, Ms(80));
        Assert.Equal(new[] { LeftClick }, g.Touch(2, P, Ms(150)));
    }

    [Fact]
    public void Pen_near_lets_go_of_a_held_button_and_drops_a_waiting_click()
    {
        var g = new TouchTapGestures();
        g.Touch(1, P, Ms(0)); g.Touch(0, P, Ms(80)); g.Touch(1, P, Ms(200));
        Assert.True(g.Dragging);
        Assert.Equal(new[] { LeftUp }, g.Cancel());
        Assert.False(g.Busy);

        var h = new TouchTapGestures();
        h.Touch(1, P, Ms(0)); h.Touch(0, P, Ms(80));
        Assert.Empty(h.Cancel());
        Assert.Empty(h.Tick(Ms(1000)));                       // the click was dropped
    }

    [Fact]
    public void Reports_that_stop_mid_drag_never_leave_the_button_down()
    {
        var g = new TouchTapGestures();
        g.Touch(1, P, Ms(0)); g.Touch(0, P, Ms(80)); g.Touch(1, P, Ms(200)); g.Touch(1, P + new Vector2(5, 0), Ms(260));
        Assert.True(g.Dragging);
        Assert.Empty(g.Tick(Ms(400)));
        Assert.Equal(new[] { LeftUp }, g.Tick(Ms(700)));       // no report for over 300 ms: lifted
        Assert.False(g.Dragging);
    }

    [Fact]
    public void Dispose_releases_a_held_button_and_is_safe_to_call_twice()
    {
        var filter = new TouchTrackpadFilter();
        Assert.IsAssignableFrom<IDisposable>(filter);
        var taps = (TouchTapGestures)typeof(TouchTrackpadFilter)
            .GetField("_taps", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(filter)!;
        taps.Touch(1, P, Ms(0)); taps.Touch(0, P, Ms(80)); taps.Touch(1, P, Ms(200));
        Assert.True(taps.Dragging);
        filter.Dispose();
        Assert.False(taps.Dragging);
        Assert.False(taps.Busy);
        filter.Dispose();
    }
}
