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

    // V30 (Zak 23 Nov 2026: "These warning texts must be in bold yellow"): the yellow "Not set up" is readable on every step,
    // light and dark, and is applied per step like the warning text.
    [Theory, MemberData(nameof(AllSteps))]
    public void AttentionText_HasAaContrast(int step)
    {
        var p = LightnessService.For(step);
        var c = LightnessService.AttentionText(step);
        if (!p.Dark)
        {
            // Light steps (fresh check of test build 2, 23 Nov 2026): dark ink on the yellow highlight, never brown text.
            Assert.Equal(LightnessService.Attention, LightnessService.AttentionFill(step));
            Assert.True(LightnessService.Contrast(c, LightnessService.Attention) >= 4.5, $"step {step}: ink on yellow");
            return;
        }
        Assert.Equal(0, LightnessService.AttentionFill(step).A);
        Assert.True(LightnessService.Contrast(c, p.Elevated) >= 4.5, $"step {step}: {LightnessService.Contrast(c, p.Elevated):0.00}:1 on sheet");
        Assert.True(LightnessService.Contrast(c, p.Base) >= 4.5, $"step {step}: on window");
    }

    // A switch that is On must show its track on every step (WCAG 1.4.11: 3:1 for a control's parts), and its knob on the track.
    [Theory, MemberData(nameof(AllSteps))]
    public void SwitchOn_TrackAndKnobCanBeSeen(int step)
    {
        var p = LightnessService.For(step);
        var track = LightnessService.SwitchOnFill(step); var knob = LightnessService.SwitchOnKnob(step);
        Assert.True(LightnessService.Contrast(track, p.Base) >= 3, $"step {step}: track on window {LightnessService.Contrast(track, p.Base):0.0}:1");
        Assert.True(LightnessService.Contrast(track, p.Elevated) >= 3, $"step {step}: track on sheet");
        Assert.True(LightnessService.Contrast(knob, track) >= 3, $"step {step}: knob on track");
    }

    // V30 build 4 (Zak 24 Nov: "change the word CONNECTED to be in green"): green, and readable on every surface of every step.
    [Theory, MemberData(nameof(AllSteps))]
    public void Connected_IsGreen_AndReadable(int step)
    {
        var p = LightnessService.For(step);
        var c = LightnessService.ConnectedText(step);
        Assert.True(c.G > c.R + 40 && c.G > c.B + 40, $"step {step}: {c} is not green");
        foreach (var bg in new[] { p.Base, p.Elevated, p.Surface, p.Pane })
            Assert.True(LightnessService.Contrast(c, bg) >= 4.5, $"step {step}: {LightnessService.Contrast(c, bg):0.0}:1 on {bg}");
    }

    // V30 build 4 (Zak 24 Nov: "Again 2 designs of buttons"): the tab you are on is lit and its word reads clearly.
    [Theory, MemberData(nameof(AllSteps))]
    public void ChosenTab_TextIsClear(int step)
        => Assert.True(LightnessService.Contrast(LightnessService.TabOnText(step), LightnessService.TabOnFill(step)) >= 7, $"step {step}");

    [Fact]
    public void AttentionFill_AndSwitchTrack_AreAppliedPerStep()
    {
        var keys = LightnessService.BrushResources(LightnessService.Steps - 1);
        Assert.Contains(keys, r => r.Key == "AttentionFillBrush");
        Assert.Contains(keys, r => r.Key == "SwitchOnFillBrush");
    }

    [Fact]
    public void AttentionText_IsAppliedPerStep()
        => Assert.Contains(LightnessService.BrushResources(LightnessService.Steps - 1), r => r.Key == "AttentionTextBrush");

    [Fact]
    public void AttentionText_IsYellowNotAmber()
    {
        var y = LightnessService.Attention; var a = LightnessService.Warning;
        Assert.True(y.G > a.G, "the attention colour has more green than the amber warning, so it reads yellow");
    }
}
