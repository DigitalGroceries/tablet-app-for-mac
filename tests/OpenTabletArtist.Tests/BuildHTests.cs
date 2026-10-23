using System.Collections.Generic;
using System.Linq;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Dynamics;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Build H: per-mode step size, and no duplicate "Ring" mode when the ring's keys arrive swapped.</summary>
public class BuildHTests
{
    private static AuxCombo K(string key, bool cmd = false, bool alt = false) => new(false, false, alt, key, cmd);

    private static WheelBindingSettings Wheel(float threshold = 5)
    {
        var w = new WheelBindingSettings { ClockwiseActivationThreshold = threshold, CounterClockwiseActivationThreshold = threshold };
        w.WheelButtons.Add(null!);
        return w;
    }

    private static RingStepCounter C(float ring, params float[] steps)
    {
        var c = new RingStepCounter(4) { RingStep = ring };
        for (int i = 0; i < steps.Length; i++) c[i] = steps[i];
        return c;
    }

    private static int Fires(RingStepCounter b, int mode, int ticks) =>
        Enumerable.Range(0, ticks).Count(_ => b.Count(mode));

    [Fact]
    public void CountStep_EightDegreeStep_OnFiveDegreeTicks_FiresFiveTimesPerEightTicks()
    {
        var b = C(5, 8);
        Assert.Equal(5, Fires(b, 0, 8));            // 40° of travel / 8° = 5 presses, remainder carried
        Assert.Equal(new[] { false, true, false, true, true }, Enumerable.Range(0, 5).Select(_ => b.Count(0)));
    }

    [Fact]
    public void CountStep_ThirtyDegrees_IsOncePerSixTicks()
    {
        var b = C(5, 0, 30);
        Assert.Equal(new[] { false, false, false, false, false, true }, Enumerable.Range(0, 6).Select(_ => b.Count(1)));
    }

    [Fact]
    public void CountStep_ZeroOrFinerThanTheRing_FiresEveryTick()
    {
        var b = C(5, 0, 3);
        Assert.Equal(4, Fires(b, 0, 4));
        Assert.Equal(4, Fires(b, 1, 4));
        Assert.Equal(4, Fires(C(0, 30), 0, 4));   // no RingStep: old behaviour
    }

    [Fact]
    public void CountStep_ResetsTravel_WhenTheModeChanges()
    {
        var b = C(5, 10, 10);
        Assert.False(b.Count(0));               // 5° into mode 1
        Assert.False(b.Count(1));               // switched: starts again, not 10° → fire
        Assert.True(b.Count(1));
        Assert.True(b.Count(2));                     // mode 3 has step 0: every tick
        Assert.False(b.Count(1));                   // …and coming back to mode 2 starts from 0 again
    }

    [Fact]
    public void Apply_WritesSteps_AndReadGivesThemBack()
    {
        var w = Wheel(threshold: 5);
        Assert.True(RingModeBinding.Apply(w, RingModeBinding.Defaults));
        var back = RingModeBinding.Read(w);
        Assert.Equal(new[] { 10f, 30f, 8f, 60f }, back.Select(m => m.StepDegrees));
        Assert.True(RingModeBinding.SameSteps(RingModeBinding.ActiveModes(RingModeBinding.Defaults), back));
        Assert.Equal("5", w.CounterClockwiseRotation!.Settings.First(s => s.Property == "RingStep").Value!.ToString());
    }

    [Fact]
    public void Read_ABindingFromBuildG_WithoutSteps_UsesTheDefaultSteps_AndIsSyncedOnce()
    {
        var w = Wheel();
        RingModeBinding.Apply(w, RingModeBinding.Defaults);
        foreach (var store in new[] { w.ClockwiseRotation!, w.CounterClockwiseRotation! })
            foreach (var s in store.Settings.Where(s => s.Property.EndsWith("Step")).ToList()) store.Settings.Remove(s);
        var read = RingModeBinding.Read(w);
        Assert.Equal(new[] { 10f, 30f, 8f, 60f }, read.Select(m => m.StepDegrees));
    }

    // Build Q: replaces the build-G test retired in K. the user's saved lists were written by G (no steps) and H (steps);
    // both must load under K's format with sensible steps and the seeded "Ring" mode moved to Brush hardness.
    [Fact]
    public void Deserialize_AListSavedByBuildG_LoadsUnderKFormat()
    {
        var json = "[{\"Name\":\"Rotate\",\"Clockwise\":\"Alt+F13\",\"CounterClockwise\":\"Alt+F14\",\"Enabled\":true}," +
                   "{\"Name\":\"Zoom\",\"Clockwise\":\"Application+Equal\",\"CounterClockwise\":\"Application+Minus\",\"Enabled\":true}," +
                   "{\"Name\":\"Brush size\",\"Clockwise\":\"RightBracket\",\"CounterClockwise\":\"LeftBracket\",\"Enabled\":true}," +
                   "{\"Name\":\"Ring\",\"Clockwise\":\"LeftBracket\",\"CounterClockwise\":\"RightBracket\",\"Enabled\":true}]";
        AssertKLayout(RingModeBinding.Deserialize(json)!);
    }

    [Fact]
    public void Deserialize_AListSavedByBuildH_LoadsUnderKFormat()
    {
        var json = "[{\"Name\":\"Rotate\",\"Clockwise\":\"Alt+F13\",\"CounterClockwise\":\"Alt+F14\",\"Enabled\":true,\"Step\":10}," +
                   "{\"Name\":\"Zoom\",\"Clockwise\":\"Application+Equal\",\"CounterClockwise\":\"Application+Minus\",\"Enabled\":true,\"Step\":30}," +
                   "{\"Name\":\"Brush size\",\"Clockwise\":\"RightBracket\",\"CounterClockwise\":\"LeftBracket\",\"Enabled\":true,\"Step\":8}," +
                   "{\"Name\":\"Ring\",\"Clockwise\":\"RightBracket\",\"CounterClockwise\":\"LeftBracket\",\"Enabled\":true,\"Step\":8}]";
        AssertKLayout(RingModeBinding.Deserialize(json)!);
    }

    private static void AssertKLayout(List<RingMode> list)
    {
        Assert.Equal(new[] { "Rotate", "Zoom", "Brush size", "Brush hardness" }, list.Select(m => m.Name));
        Assert.Equal(new[] { 10f, 30f, 8f, 60f }, list.Select(m => m.StepDegrees));
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(RingModeBinding.Defaults[i].Clockwise, list[i].Clockwise);
            Assert.Equal(RingModeBinding.Defaults[i].CounterClockwise, list[i].CounterClockwise);
        }
        Assert.All(list, m => Assert.False(m.UsesDragGesture));
        var again = RingModeBinding.Deserialize(RingModeBinding.Serialize(list))!;
        Assert.Equal(list, again);
    }

    [Theory]
    [InlineData("LeftBracket", "RightBracket")]     // the G bug: OTA's internal swap delivers them reversed
    [InlineData("RightBracket", "LeftBracket")]
    [InlineData("RightBracket", null)]               // only one direction bound
    [InlineData(null, "LeftBracket")]
    public void Load_NeverSeedsADuplicateOfADefaultMode(string? cw, string? ccw)
    {
        var list = RingModeBinding.Load(new WheelBindingSettings(), null,
            cw == null ? null : K(cw), ccw == null ? null : K(ccw));
        Assert.Equal(RingModeBinding.Defaults, list);
    }

    [Fact(Skip = "Superseded by build K: the old ring keys are no longer seeded as a 'Ring' mode; see BuildKTests")]
    public void Load_StillKeepsTheRingsOwnKeys_WhenNotADefault_EvenSwapped()
    {
        var list = RingModeBinding.Load(new WheelBindingSettings(), null, K("A"), K("B"));
        Assert.Equal("Ring", list[^1].Name);
        Assert.Equal(K("A"), list[^1].Clockwise);
    }

    [Fact]
    public void SendsTheSame_MatchesEitherDirection_ButNotDifferentKeys()
    {
        var brush = RingModeBinding.Defaults[2];
        Assert.True(RingModeBinding.SendsTheSame(brush, brush.CounterClockwise, brush.Clockwise));
        Assert.False(RingModeBinding.SendsTheSame(brush, brush.Clockwise, brush.Clockwise));
        Assert.False(RingModeBinding.SendsTheSame(brush, K("A"), AuxCombo.Unbound));
    }
}
