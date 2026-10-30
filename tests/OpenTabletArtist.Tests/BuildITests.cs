using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;
using OpenTabletDriver.Desktop.Profiles;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Build I: the step box commits once (not per keystroke), forgiving parsing, 180° range, and the
/// Option+scroll smooth-zoom action.</summary>
public class BuildITests
{
    private static AuxCombo K(string key, bool cmd = false) => new(false, false, false, key, cmd);

    private static (RingModeRow Row, List<int> Changes) Row(RingMode? mode = null)
    {
        var changes = new List<int>();
        var row = new RingModeRow(1, mode ?? new RingMode("Zoom", K("Equal", true), K("Minus", true), StepDegrees: 30),
            canEdit: true, editBinding: null, changed: () => { changes.Add(1); return Task.CompletedTask; });
        return (row, changes);
    }

    [Theory]
    [InlineData("50", 50f)]
    [InlineData("50°", 50f)]
    [InlineData(" 7,5 ", 7.5f)]
    [InlineData("7.5°", 7.5f)]
    [InlineData("500", 180f)]
    [InlineData("-3", 0f)]
    public void TryParseDegrees_IsForgiving(string text, float expected)
    {
        Assert.True(RingModeRow.TryParseDegrees(text, out var v));
        Assert.Equal(expected, v, 3);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("5x0")]
    public void TryParseDegrees_RejectsNonNumbers(string text) =>
        Assert.False(RingModeRow.TryParseDegrees(text, out _));

    [Fact]
    public void StepText_CommitsOnce_AndAnInvalidEntryPutsTheOldValueBack()
    {
        var (row, changes) = Row();
        Assert.Equal("30", row.StepText);
        row.StepText = "50";
        Assert.Equal(50f, row.ToMode().StepDegrees);
        Assert.Single(changes);
        row.StepText = "oops";
        Assert.Equal("50", row.StepText);
        Assert.Single(changes);
    }

    [Fact]
    public void Slider_PreviewsWhileDragging_AndCommitsOnlyOnRelease()
    {
        var (row, changes) = Row();
        row.SliderDegrees = 120;
        row.SliderDegrees = 150;
        Assert.Empty(changes);                       // dragging: no save, no row rebuild
        Assert.Equal("150", row.StepText);
        row.CommitSlider();
        Assert.Single(changes);
        Assert.Equal(150f, row.ToMode().StepDegrees);
    }

    [Fact]
    public void Step_RangeIsNow180_AndSurvivesApplyAndRead()
    {
        var w = new WheelBindingSettings { ClockwiseActivationThreshold = 5, CounterClockwiseActivationThreshold = 5 };
        w.WheelButtons.Add(null!);
        Assert.True(RingModeBinding.Apply(w, new[] { new RingMode("Rotate", K("A"), K("B"), StepDegrees: 180) }));
        Assert.Equal(180f, RingModeBinding.Read(w)[0].StepDegrees);
    }

    [Fact]
    public void SmoothZoom_WritesOppositeScrollPerDirection_AndReadsBack()
    {
        var w = new WheelBindingSettings();
        w.WheelButtons.Add(null!);
        var zoom = new RingMode("Zoom", AuxCombo.Unbound, AuxCombo.Unbound, ScrollAmount: 20);
        Assert.True(zoom.IsMapped);                  // a scroll mode needs no keys
        Assert.True(RingModeBinding.Apply(w, new[] { zoom }));
        // Version 16 BETA 2: physical clockwise is OTD's ClockwiseRotation; clockwise scrolls up (= zoom in), one
        // wheel line (10) per step, and the Speed (20) sets the step on the keys scale.
        string? Get(OpenTabletDriver.Desktop.Reflection.PluginSettingStore s, string p = "Mode1Scroll") =>
            s.Settings.First(x => x.Property == p).Value?.ToString();
        Assert.Equal("10", Get(w.ClockwiseRotation!));
        Assert.Equal("-10", Get(w.CounterClockwiseRotation!));
        Assert.Equal(RingMode.DegreesOfSpeed(20).ToString(System.Globalization.CultureInfo.InvariantCulture),
            Get(w.ClockwiseRotation!, "Mode1Step"));
        Assert.Equal(20, RingModeBinding.Read(w)[0].ScrollAmount);
    }

    [Fact]
    public void SmoothZoom_SurvivesSaveAndLoad_AndKeysModesSaveNoScroll()
    {
        var list = new List<RingMode>
        {
            new("Zoom", AuxCombo.Unbound, AuxCombo.Unbound, ScrollAmount: 35),
            new("Brush", K("RightBracket"), K("LeftBracket"), StepDegrees: 8),
        };
        var json = RingModeBinding.Serialize(list);
        Assert.DoesNotContain("\"Scroll\":0", json);
        var back = RingModeBinding.Deserialize(json)!;
        Assert.Equal(35, back[0].ScrollAmount);
        Assert.Equal(0, back[1].ScrollAmount);
    }

    [Fact]
    public void UseScroll_SwitchesTheModeToScroll_WithTheSensitivity()
    {
        var (row, changes) = Row();
        row.ScrollSensitivity = 12;
        row.UseScroll = true;
        Assert.Equal(12, row.ToMode().ScrollAmount);   // Version 16 BETA 2: the Speed itself
        row.ScrollSensitivity = 40;
        row.CommitScrollSensitivity();
        Assert.Equal(40, row.ToMode().ScrollAmount);
        row.UseScroll = false;
        Assert.Equal(0, row.ToMode().ScrollAmount);
        Assert.Equal(3, changes.Count);
    }
}
