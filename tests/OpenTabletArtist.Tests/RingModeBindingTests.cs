using System.Collections.Generic;
using System.Linq;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using Xunit;

namespace OpenTabletArtist.Tests;

public class RingModeBindingTests
{
    private static WheelBindingSettings Wheel(int buttons = 1)
    {
        var w = new WheelBindingSettings();
        for (int i = 0; i < buttons; i++) w.WheelButtons.Add(null!);
        return w;
    }

    private static AuxCombo K(string key, bool cmd = false, bool alt = false) => new(false, false, alt, key, cmd);

    private static string? Setting(PluginSettingStore? store, string property) =>
        store?.Settings.FirstOrDefault(s => s.Property == property)?.Value?.ToString();

    [Fact]
    public void Apply_WritesOnlyModesThatAreOnAndMapped_InOrder()
    {
        var w = Wheel();
        var modes = new List<RingMode>
        {
            new("Brush size", K("RightBracket"), K("LeftBracket")),
            new("Off one", K("A"), K("B"), Enabled: false),
            new("Empty", AuxCombo.Unbound, AuxCombo.Unbound),
            new("Zoom", K("Equal", cmd: true), K("Minus", cmd: true)),
        };

        Assert.True(RingModeBinding.Apply(w, modes));

        // physical clockwise ↔ OTD CounterClockwiseRotation (the app's ring inversion)
        Assert.Equal(RingModeBinding.TurnTypeName, w.CounterClockwiseRotation!.Path);
        Assert.Equal("RightBracket", Setting(w.CounterClockwiseRotation, "Mode1Keys"));
        Assert.Equal("Application+Equal", Setting(w.CounterClockwiseRotation, "Mode2Keys"));
        Assert.Equal("", Setting(w.CounterClockwiseRotation, "Mode3Keys"));
        Assert.Equal("LeftBracket", Setting(w.ClockwiseRotation, "Mode1Keys"));
        Assert.Equal("Application+Minus", Setting(w.ClockwiseRotation, "Mode2Keys"));

        Assert.Equal(RingModeBinding.NextTypeName, w.WheelButtons[0].Path);
        Assert.Equal("2", Setting(w.WheelButtons[0], RingModeBinding.ModeCountProperty));
        Assert.Equal("Brush size|Zoom", Setting(w.WheelButtons[0], RingModeBinding.ModeNamesProperty));
        Assert.True(RingModeBinding.IsActive(w));
    }

    [Fact]
    public void Apply_RefusesWithoutARingButton_OrWithNothingMapped()
    {
        var noButton = Wheel(buttons: 0);
        Assert.False(RingModeBinding.Apply(noButton, RingModeBinding.Defaults));
        Assert.Null(noButton.ClockwiseRotation);

        var w = Wheel();
        Assert.False(RingModeBinding.Apply(w, new[] { new RingMode("x", AuxCombo.Unbound, AuxCombo.Unbound) }));
        Assert.False(RingModeBinding.IsActive(w));
    }

    [Fact]
    public void Read_RoundTripsWhatApplyWrote()
    {
        var w = Wheel();
        RingModeBinding.Apply(w, RingModeBinding.Defaults);
        var back = RingModeBinding.Read(w);

        Assert.Equal(RingModeBinding.Defaults.Select(m => m.Name), back.Select(m => m.Name));
        Assert.Equal(RingModeBinding.Defaults.Select(m => m.Clockwise), back.Select(m => m.Clockwise));
        Assert.Equal(RingModeBinding.Defaults.Select(m => m.CounterClockwise), back.Select(m => m.CounterClockwise));
    }

    [Fact(Skip = "Superseded by build K: the old ring keys are no longer seeded as a 'Ring' mode; see BuildKTests")]
    public void Load_KeepsTheRingsOwnKeysInTheLastMode_WhenTheyArentADefaultMode()
    {
        var w = Wheel();
        var modes = RingModeBinding.Load(w, saved: null, K("A"), K("B"));

        Assert.Equal(RingModeBinding.MaxModes, modes.Count);
        Assert.Equal("Rotate", modes[0].Name);   // build G: Rotate stays first
        Assert.Equal("Ring", modes[3].Name);
        Assert.Equal(K("A"), modes[3].Clockwise);
        Assert.Equal(K("B"), modes[3].CounterClockwise);
    }

    // the user's ring today: clockwise ], counter-clockwise [ — already the Brush size mode, so the defaults stand.
    [Fact]
    public void Load_UsesTheDefaultsUnchanged_WhenTheRingAlreadyDoesBrushSize()
    {
        var modes = RingModeBinding.Load(Wheel(), saved: null, K("RightBracket"), K("LeftBracket"));
        Assert.Equal(RingModeBinding.Defaults, modes);
        Assert.Equal(new[] { "Rotate", "Zoom", "Brush size", "Brush hardness" }, modes.Select(m => m.Name));
        Assert.Equal(new AuxCombo(false, false, true, "F13"), modes[0].Clockwise);
        Assert.Equal(new AuxCombo(false, false, true, "F14"), modes[0].CounterClockwise);
    }

    [Fact]
    public void Load_KeepsTheSavedListIncludingModesThatAreOff_WhenItMatchesTheProfile()
    {
        var saved = new List<RingMode>
        {
            new("Brush", K("RightBracket"), K("LeftBracket")),
            new("Spare", K("A"), K("B"), Enabled: false),
            new("Zoom", K("Equal", cmd: true), K("Minus", cmd: true)),
        };
        var w = Wheel();
        RingModeBinding.Apply(w, saved);

        var loaded = RingModeBinding.Load(w, RingModeBinding.Serialize(saved), null, null);

        Assert.Equal(4, loaded.Count);
        Assert.Equal("Spare", loaded[1].Name);
        Assert.False(loaded[1].Enabled);
        Assert.Equal(K("A"), loaded[1].Clockwise);
    }

    [Fact]
    public void Load_TrustsTheProfile_WhenTheSavedListNoLongerMatches()
    {
        var w = Wheel();
        RingModeBinding.Apply(w, new[] { new RingMode("Brush", K("RightBracket"), K("LeftBracket")) });
        var stale = RingModeBinding.Serialize(new[] { new RingMode("Old", K("Z"), K("X")) });

        var loaded = RingModeBinding.Load(w, stale, null, null);

        Assert.Equal("Brush", loaded[0].Name);
        Assert.Equal(K("RightBracket"), loaded[0].Clockwise);
    }

    [Fact]
    public void Serialize_RoundTrips_AndBadJsonIsIgnored()
    {
        var json = RingModeBinding.Serialize(RingModeBinding.Defaults);
        Assert.Equal(RingModeBinding.Defaults, RingModeBinding.Deserialize(json));
        Assert.Null(RingModeBinding.Deserialize("{not json"));
        Assert.Null(RingModeBinding.Deserialize(""));
    }

    [Fact]
    public void ParseKeys_And_FormatKeys_AreInverse()
    {
        var combo = new AuxCombo(false, true, false, "Z", Cmd: true);
        var keys = AuxKeyBinding.FormatKeys(combo);
        Assert.Equal("Application+Shift+Z", keys);
        Assert.Equal(combo, AuxKeyBinding.ParseKeys(keys));
        Assert.Equal(AuxCombo.Unbound, AuxKeyBinding.ParseKeys(""));
        Assert.Null(AuxKeyBinding.ParseKeys("A+B"));
    }

    [Theory]
    [InlineData("2 3\nZoom\n", 2, 3, "Zoom")]
    [InlineData("1 1\n", 1, 1, "Mode 1")]
    public void Hud_ParsesTheStateFile(string content, int mode, int count, string name)
    {
        Assert.True(RingModeHud.TryParse(content, out var m, out var c, out var n));
        Assert.Equal((mode, count, name), (m, c, n));
    }

    [Theory]
    [InlineData("")]
    [InlineData("x y")]
    [InlineData("4 3\nToo far")]
    [InlineData("0 3")]
    public void Hud_RejectsBadStateFiles(string content) =>
        Assert.False(RingModeHud.TryParse(content, out _, out _, out _));

    [Fact]
    public void Hud_DotsMarkTheCurrentMode() =>
        Assert.Equal("○  ●  ○", RingModeHud.Dots(2, 3));
}
