using System.Linq;
using Avalonia.Media;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Version 31 test build 2 (Digital Groceries, 27 Nov 2026): the radial menu and the colour palette have a bevel
/// ALL the way round, and they are RAISED above the page like the buttons - by the logic of the light from the top left,
/// not by the mockup's pixels. The slope is lighter than the page at the top left and darker at the bottom right; the
/// line of shadow outside it is darker than the page at every point, so the circle is never broken.</summary>
public class FullCircleBevelTests
{
    public static TheoryData<int> Steps() { var d = new TheoryData<int>(); for (int s = 0; s < LightnessService.Steps; s++) d.Add(s); return d; }

    static LinearGradientBrush Brush(int step, string key) =>
        (LinearGradientBrush)LightnessService.SunkenResources(step).First(r => r.Key == key).Value;

    [Theory, MemberData(nameof(Steps))]
    public void The_slope_is_lit_at_the_top_left_and_in_shadow_at_the_bottom_right(int step)
    {
        var look = LightnessService.For(step); int pane = look.Pane.R;
        var stops = Brush(step, "EmbossRingBrush").GradientStops;
        Assert.True(stops.First().Color.R >= System.Math.Min(pane + 0x20, 0xE4), $"step {step}: lit #{stops.First().Color.R:X2} on page #{pane:X2}");
        Assert.True(stops.Last().Color.R <= pane - 0x20, $"step {step}: shadow #{stops.Last().Color.R:X2} on page #{pane:X2}");
    }

    [Theory, MemberData(nameof(Steps))]
    public void The_line_of_shadow_is_darker_than_the_page_all_the_way_round(int step)
    {
        int pane = LightnessService.For(step).Pane.R;
        var stops = Brush(step, "EmbossRingEdgeBrush").GradientStops;
        Assert.All(stops, s => Assert.True(s.Color.R <= pane - 0x10, $"step {step}: line #{s.Color.R:X2} on page #{pane:X2}"));
        Assert.True(stops.First().Color.R > stops.Last().Color.R, "darkest at the bottom right");
    }
}
