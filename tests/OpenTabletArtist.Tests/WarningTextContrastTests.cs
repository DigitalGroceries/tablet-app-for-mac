using System.Linq;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Item 14 (#23): Setup's warning text is readable (4.5:1) on every lightness step, light and dark.</summary>
public class WarningTextContrastTests
{
    public static TheoryData<int> AllSteps()
    {
        var d = new TheoryData<int>();
        for (int i = 0; i < LightnessService.Steps; i++) d.Add(i);
        return d;
    }

    [Theory, MemberData(nameof(AllSteps))]
    public void WarningText_HasAaContrast(int step)
    {
        var p = LightnessService.For(step);
        var c = LightnessService.WarningText(step);
        Assert.True(LightnessService.Contrast(c, p.Elevated) >= 4.5, $"step {step}: {LightnessService.Contrast(c, p.Elevated):0.00}:1 on sheet");
        Assert.True(LightnessService.Contrast(c, p.Base) >= 4.5, $"step {step}: on window");
    }

    [Fact]
    public void WarningText_IsAppliedPerStep()
        => Assert.Contains(LightnessService.BrushResources(LightnessService.Steps - 1), r => r.Key == "WarningTextBrush");

    [Fact]
    public void LightSteps_UseADarkerAmber()
    {
        var light = Enumerable.Range(0, LightnessService.Steps).Where(s => !LightnessService.For(s).Dark);
        Assert.All(light, s => Assert.True(LightnessService.Luminance(LightnessService.WarningText(s)) < LightnessService.Luminance(LightnessService.Warning)));
    }
}
