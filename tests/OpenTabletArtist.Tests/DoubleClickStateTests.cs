using System.Numerics;
using OpenTabletDriver.Desktop.Interop.Input;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Version 25: tip double-click counting (hover must not break; drag and distance must).</summary>
public class DoubleClickStateTests
{
    private static DoubleClickState New(int tolerance = 40, double intervalMs = 500) =>
        new() { MoveTolerance = tolerance, IntervalMs = intervalMs };

    [Fact]
    public void Hover_movement_between_taps_does_not_reset_the_count()
    {
        var s = New();
        Assert.Equal(1, s.OnMouseDown(new Vector2(100, 100), 0));
        s.OnMouseUp(10);
        // Hover while the tip is up — must not mark a drag.
        s.OnPosition(new Vector2(130, 100), buttonHeld: false);
        s.OnPosition(new Vector2(70, 140), buttonHeld: false);
        Assert.Equal(2, s.OnMouseDown(new Vector2(105, 102), 100));
    }

    [Fact]
    public void Movement_while_held_resets_the_count()
    {
        var s = New(tolerance: 40);
        Assert.Equal(1, s.OnMouseDown(new Vector2(100, 100), 0));
        s.OnPosition(new Vector2(160, 100), buttonHeld: true); // drag past tolerance
        Assert.True(s.MovedWhileHeld);
        s.OnMouseUp(50);
        Assert.Equal(1, s.OnMouseDown(new Vector2(100, 100), 100));
    }

    [Fact]
    public void Second_down_farther_than_tolerance_starts_a_new_single_click()
    {
        var s = New(tolerance: 40);
        Assert.Equal(1, s.OnMouseDown(new Vector2(100, 100), 0));
        s.OnMouseUp(10);
        Assert.Equal(1, s.OnMouseDown(new Vector2(200, 100), 80));
    }

    [Fact]
    public void Second_down_within_tolerance_and_interval_becomes_click_2()
    {
        var s = New(tolerance: 40, intervalMs: 500);
        Assert.Equal(1, s.OnMouseDown(new Vector2(100, 100), 0));
        s.OnMouseUp(20);
        Assert.Equal(2, s.OnMouseDown(new Vector2(110, 105), 100));
    }

    [Fact]
    public void Timeout_beyond_interval_starts_a_new_single_click()
    {
        var s = New(tolerance: 40, intervalMs: 500);
        Assert.Equal(1, s.OnMouseDown(new Vector2(100, 100), 0));
        s.OnMouseUp(20);
        Assert.Equal(1, s.OnMouseDown(new Vector2(100, 100), 600));
    }

    [Fact]
    public void ClampTolerance_maps_zero_and_out_of_range()
    {
        Assert.Equal(100, DoubleClickState.ClampTolerance(0));
        Assert.Equal(8, DoubleClickState.ClampTolerance(3));
        Assert.Equal(192, DoubleClickState.ClampTolerance(500));
        Assert.Equal(100, DoubleClickState.ClampTolerance(100));
        Assert.Equal(40, DoubleClickState.ClampTolerance(40));
    }

    [Fact]
    public void Double_Click_Fix_scale_is_Off_8_Default_100_Large_192()
    {
        Assert.Equal(8, DoubleClickState.MinMoveTolerance);
        Assert.Equal(100, DoubleClickState.DefaultMoveTolerance);
        Assert.Equal(192, DoubleClickState.MaxMoveTolerance);
        Assert.Equal(50, DoubleClickState.UiDefault);
        Assert.Equal(100, new OpenTabletDriver.Desktop.Profiles.BindingSettings().TipDoubleClickDistance);
    }

    [Theory]
    [InlineData(0, 8)]
    [InlineData(50, 100)]
    [InlineData(100, 192)]
    [InlineData(30, 63)] // ~64 px (legacy Default) ≈ UI 30
    public void Display_maps_to_physical_pixels(int display, int pixels)
    {
        Assert.Equal(pixels, DoubleClickState.DisplayToPixels(display));
        Assert.Equal(display, DoubleClickState.PixelsToDisplay(pixels));
    }

    [Fact]
    public void Legacy_saved_pixel_values_round_trip_through_the_UI_scale()
    {
        Assert.Equal(0, DoubleClickState.PixelsToDisplay(8));
        Assert.Equal(17, DoubleClickState.PixelsToDisplay(40)); // (40-8)/184*100 ≈ 17.4
        Assert.Equal(30, DoubleClickState.PixelsToDisplay(64));
        Assert.Equal(50, DoubleClickState.PixelsToDisplay(100));
        Assert.Equal(100, DoubleClickState.PixelsToDisplay(192));
    }

    [Fact]
    public void Jitter_while_held_is_pinned_to_the_down_point_so_no_drag_reaches_macOS()
    {
        var s = New(tolerance: 100);
        s.OnMouseDown(new Vector2(100, 100), 0);
        s.OnPosition(new Vector2(130, 120), buttonHeld: true);
        Assert.Equal(new Vector2(100, 100), s.FilterPosition(new Vector2(130, 120), buttonHeld: true));
        Assert.False(s.MovedWhileHeld);
        // Hover is never pinned.
        Assert.Equal(new Vector2(130, 120), s.FilterPosition(new Vector2(130, 120), buttonHeld: false));
    }

    [Fact]
    public void A_real_drag_past_the_value_follows_from_the_hold_point_without_teleport()
    {
        var s = New(tolerance: 64);
        s.OnMouseDown(new Vector2(100, 100), 0);
        s.OnPosition(new Vector2(200, 100), buttonHeld: true);
        Assert.True(s.MovedWhileHeld);
        // First sample after exceed stays at the hold point (no catch-up jump to 200).
        Assert.Equal(new Vector2(100, 100), s.FilterPosition(new Vector2(200, 100), buttonHeld: true));
        // Further movement follows by the delta since the exceed point.
        Assert.Equal(new Vector2(120, 100), s.FilterPosition(new Vector2(220, 100), buttonHeld: true));
    }

    [Fact]
    public void Second_tap_on_a_different_pixel_does_not_move_the_pointer_to_the_first_tap()
    {
        var s = New(tolerance: 100);
        s.OnMouseDown(new Vector2(100, 100), 0);
        s.OnMouseUp(30);
        Assert.Equal(2, s.OnMouseDown(new Vector2(140, 125), 150));
        // Hold-still for the second tap pins to the second tap, not the first.
        Assert.Equal(new Vector2(140, 125), s.LastDownPosition);
        Assert.Equal(new Vector2(140, 125), s.FilterPosition(new Vector2(150, 130), buttonHeld: true));
    }

    [Fact]
    public void Tip_up_after_sub_threshold_wobble_stays_at_the_hold_point()
    {
        var s = New(tolerance: 100);
        s.OnMouseDown(new Vector2(100, 100), 0);
        s.OnPosition(new Vector2(130, 120), buttonHeld: true);
        Assert.Equal(new Vector2(100, 100), s.PositionForMouseUp(new Vector2(130, 120)));
    }

    [Fact]
    public void Our_app_bypass_disables_hold_still_for_the_stroke()
    {
        var s = New(tolerance: 100);
        s.BypassHoldStill = true;
        s.OnMouseDown(new Vector2(100, 100), 0);
        Assert.Equal(new Vector2(130, 120), s.FilterPosition(new Vector2(130, 120), buttonHeld: true));
        Assert.Equal(new Vector2(130, 120), s.PositionForMouseUp(new Vector2(130, 120)));
    }

    [Theory]
    [InlineData(8, false)]
    [InlineData(64, true)]
    [InlineData(100, true)]
    [InlineData(192, true)]
    public void The_slider_value_decides_whether_two_taps_3mm_apart_are_a_double_click(int value, bool doubleClick)
    {
        // ~3 mm apart on a ~3360 px wide All-displays desktop over a ~157 mm tablet ≈ 60 px.
        var s = New(tolerance: value);
        s.OnMouseDown(new Vector2(1000, 500), 0);
        s.OnMouseUp(40);
        Assert.Equal(doubleClick ? 2 : 1, s.OnMouseDown(new Vector2(1060, 500), 200));
    }

    [Fact]
    public void Down_without_a_pending_position_uses_the_last_known_position_not_zero()
    {
        // Two buttons change in one report: the first drains the pending position, the second has none.
        var s = New();
        s.OnPosition(new Vector2(500, 400), buttonHeld: false);
        Assert.Equal(1, s.OnMouseDown(s.DownPosition(new Vector2(500, 400)), 0));
        s.OnMouseUp(10);
        var down = s.DownPosition(null);
        Assert.Equal(new Vector2(500, 400), down);
        Assert.Equal(2, s.OnMouseDown(down, 100));
    }

    [Fact]
    public void Down_position_prefers_the_pending_position()
    {
        var s = New();
        s.OnPosition(new Vector2(10, 10), buttonHeld: false);
        Assert.Equal(new Vector2(20, 30), s.DownPosition(new Vector2(20, 30)));
    }
}
