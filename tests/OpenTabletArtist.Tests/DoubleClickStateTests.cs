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
        Assert.Equal(40, DoubleClickState.ClampTolerance(0));
        Assert.Equal(8, DoubleClickState.ClampTolerance(3));
        Assert.Equal(64, DoubleClickState.ClampTolerance(100));
        Assert.Equal(40, DoubleClickState.ClampTolerance(40));
    }
}
