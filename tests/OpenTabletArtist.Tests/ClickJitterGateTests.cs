using System.Numerics;
using Newtonsoft.Json;
using OpenTabletDriver.Desktop.Interop.Input;
using OpenTabletDriver.Desktop.Profiles;
using Xunit;

namespace OpenTabletArtist.Tests;

public class ClickJitterGateTests
{
    private static readonly Vector2 Down = new(100, 200);

    [Fact]
    public void Inside_S_and_T_no_dragged_position_is_emitted()
    {
        var gate = new ClickJitterGate();
        gate.OnTipDown(Down, 0, display: 50, pixelsPerMillimetre: 21);
        Assert.Null(gate.FilterDrag(Down + new Vector2(10, 0), 50));
        Assert.True(gate.IsGating);
    }

    [Fact]
    public void Distance_exceeding_S_emits_the_true_pen_position()
    {
        var gate = new ClickJitterGate();
        gate.OnTipDown(Down, 0, display: 50, pixelsPerMillimetre: 21);
        var current = Down + new Vector2(22, 0);
        Assert.Equal(current, gate.FilterDrag(current, 40));
        Assert.False(gate.IsGating);
        Assert.Equal("S", gate.LastCompletion!.Value.ReleasedBy);
    }

    [Fact]
    public void Time_exceeding_T_while_inside_S_emits_the_true_pen_then_passes_through()
    {
        var gate = new ClickJitterGate();
        gate.OnTipDown(Down, 0, display: 50, pixelsPerMillimetre: 21);
        var stillInside = Down + new Vector2(5, 0);
        Assert.Null(gate.FilterDrag(stillInside, 249));
        Assert.Equal(stillInside, gate.FilterDrag(stillInside, 250));
        Assert.Equal("T", gate.LastCompletion!.Value.ReleasedBy);
        var later = Down + new Vector2(80, 10);
        Assert.Equal(later, gate.FilterDrag(later, 300));
    }

    [Fact]
    public void Mouse_up_while_gated_uses_the_down_position()
    {
        var gate = new ClickJitterGate();
        gate.OnTipDown(Down, 0, display: 50, pixelsPerMillimetre: 21);
        var current = Down + new Vector2(8, 3);
        Assert.Equal(Down, gate.OnTipUp(current, 80));
        Assert.Equal("up", gate.LastCompletion!.Value.ReleasedBy);
        Assert.Equal(80, gate.LastCompletion!.Value.DurationMs);
        Assert.False(gate.IsGating);
    }

    [Fact]
    public void Two_independent_taps_each_get_their_own_gate()
    {
        var gate = new ClickJitterGate();
        gate.OnTipDown(Down, 0, display: 50, pixelsPerMillimetre: 21);
        Assert.Equal(Down, gate.OnTipUp(Down + new Vector2(4, 0), 40));

        var second = new Vector2(500, 600);
        gate.OnTipDown(second, 100, display: 50, pixelsPerMillimetre: 21);
        Assert.Equal(second, gate.DownPosition);
        Assert.Null(gate.FilterDrag(second + new Vector2(5, 0), 120));
        Assert.Equal(second, gate.OnTipUp(second + new Vector2(5, 0), 150));
    }

    [Fact]
    public void After_release_further_moves_pass_through()
    {
        var gate = new ClickJitterGate();
        gate.OnTipDown(Down, 0, display: 50, pixelsPerMillimetre: 21);
        var released = Down + new Vector2(30, 0);
        Assert.Equal(released, gate.FilterDrag(released, 20));
        var later = Down + new Vector2(90, 40);
        Assert.Equal(later, gate.FilterDrag(later, 40));
    }

    [Fact]
    public void Off_never_filters()
    {
        var gate = new ClickJitterGate();
        gate.OnTipDown(Down, 0, display: 0, pixelsPerMillimetre: 21);
        var current = Down + new Vector2(1, 1);
        Assert.Equal(current, gate.FilterDrag(current, 5));
        Assert.Equal(current, gate.OnTipUp(current, 10));
        Assert.Null(gate.LastCompletion);
        Assert.False(gate.IsGating);
    }

    [Theory]
    [InlineData(50, 21, 21)]
    [InlineData(100, 21, 42)]
    [InlineData(50, 1, 4)]
    [InlineData(100, 30, 48)]
    public void Gate_pixels_follow_millimetres_and_the_clamp(int display, double pxPerMm, int expected)
    {
        Assert.Equal(expected, ClickJitterGate.GatePixels(display, pxPerMm));
    }

    [Fact]
    public void Display_maps_linearly_onto_millimetres()
    {
        Assert.Equal(0, ClickJitterGate.DisplayToMillimetres(0));
        Assert.Equal(1.0, ClickJitterGate.DisplayToMillimetres(50));
        Assert.Equal(2.0, ClickJitterGate.DisplayToMillimetres(100));
    }

    [Fact]
    public void Missing_or_negative_saved_display_becomes_Default()
    {
        Assert.Equal(50, ClickJitterGate.ResolveSavedDisplay(null));
        Assert.Equal(50, ClickJitterGate.ResolveSavedDisplay(-1));
        Assert.Equal(0, ClickJitterGate.ResolveSavedDisplay(0));
        Assert.Equal(100, ClickJitterGate.ResolveSavedDisplay(100));
        Assert.Equal(50, ClickJitterGate.ResolveSavedDisplay(null));
    }

    [Fact]
    public void Old_settings_that_only_have_pixel_TipDoubleClickDistance_load_as_Default()
    {
        var settings = JsonConvert.DeserializeObject<BindingSettings>(
            """{"TipDoubleClickDistance":100}""")!;
        Assert.Null(settings.DoubleClickFix);
        Assert.Equal(100, settings.TipDoubleClickDistance);
        Assert.Equal(50, ClickJitterGate.ResolveSavedDisplay(settings.DoubleClickFix));
    }

    [Fact]
    public void Explicit_DoubleClickFix_zero_and_100_are_kept()
    {
        var off = JsonConvert.DeserializeObject<BindingSettings>(
            """{"TipDoubleClickDistance":100,"DoubleClickFix":0}""")!;
        Assert.Equal(0, ClickJitterGate.ResolveSavedDisplay(off.DoubleClickFix));
        var large = JsonConvert.DeserializeObject<BindingSettings>(
            """{"TipDoubleClickDistance":100,"DoubleClickFix":100}""")!;
        Assert.Equal(100, ClickJitterGate.ResolveSavedDisplay(large.DoubleClickFix));
    }
}
