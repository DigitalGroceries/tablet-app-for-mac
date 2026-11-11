using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Dynamics;
using OpenTabletArtist.ViewModels;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletDriver.Plugin.Platform.Keyboard;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Version 26 (#35, Maxime): the Touch Ring "Scroll" function, a plain mouse wheel in every app
/// (Wacom's: clockwise scrolls down, anticlockwise scrolls up).</summary>
[Collection("RingModeStatics")]
public class RingScrollTests : IDisposable
{
    private sealed class FakeKb : IVirtualKeyboard
    {
        public readonly List<string> Events = new();
        public void Press(string key) => Events.Add("+" + key);
        public void Release(string key) => Events.Add("-" + key);
        public void Press(IEnumerable<string> keys) { foreach (var k in keys) Press(k); }
        public void Release(IEnumerable<string> keys) { foreach (var k in keys) Release(k); }
        public IEnumerable<string> SupportedKeys => new[] { "LeftBracket", "RightBracket", "Application", "Minus" };
    }

    private readonly Action<int> _wheel = RingModeTurnBinding.WheelScroll;
    private readonly Action<int> _scroll = RingModeTurnBinding.Scroll;
    private readonly Func<FrontApp> _front = RingModeTurnBinding.FrontApp;
    private readonly string _path = RingModeState.StateFilePath;

    public RingScrollTests()
    {
        RingModeState.StateFilePath = Path.Combine(Path.GetTempPath(), "ota-ring-scroll-" + Guid.NewGuid().ToString("N"), "ring-mode");
        RingModeState.SetCount(4); RingModeState.Reset();
    }

    public void Dispose()
    {
        RingModeTurnBinding.WheelScroll = _wheel; RingModeTurnBinding.Scroll = _scroll; RingModeTurnBinding.FrontApp = _front;
        RingModeState.Reset(); RingModeState.StateFilePath = _path;
    }

    private static RingMode ScrollMode(int speed = RingMode.DefaultWheelSpeed) =>
        new("Scroll", AuxCombo.Unbound, AuxCombo.Unbound, WheelScroll: speed);

    private static WheelBindingSettings Wheel() => new()
    {
        ClockwiseActivationThreshold = 5, CounterClockwiseActivationThreshold = 5,
        WheelButtons = new PluginSettingStoreCollection(new[] { (PluginSettingStore)null! }),
    };

    private static string? Setting(PluginSettingStore? store, string property) =>
        store?.Settings.FirstOrDefault(s => s.Property == property)?.Value is { } v
            ? Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) : null;

    // ---- Domain: written into the ring plugin, and read back ----

    [Fact]
    public void ScrollMode_IsMapped_WithoutAnyKeys()
    {
        Assert.True(ScrollMode().IsMapped);
        Assert.Single(RingModeBinding.ActiveModes(new[] { ScrollMode() }));
    }

    [Fact]
    public void Apply_ClockwiseScrollsDown_AnticlockwiseUp_OneNotchPerStep()
    {
        var w = Wheel();
        Assert.True(RingModeBinding.Apply(w, new[] { ScrollMode() }));
        Assert.Equal("-1", Setting(w.ClockwiseRotation, "Mode1Wheel"));          // down
        Assert.Equal("1", Setting(w.CounterClockwiseRotation, "Mode1Wheel"));    // up
        Assert.Equal("0", Setting(w.ClockwiseRotation, "Mode1Scroll"));          // not the Option+scroll zoom
        Assert.Equal(RingMode.DegreesOfSpeed(RingMode.DefaultWheelSpeed).ToString(System.Globalization.CultureInfo.InvariantCulture),
            Setting(w.ClockwiseRotation, "Mode1Step"));
        Assert.Equal("0", Setting(w.ClockwiseRotation, "Mode2Wheel"));
    }

    [Theory]
    [InlineData(70)]
    [InlineData(100)]
    [InlineData(1)]
    [InlineData(-70)]   // a negative Speed reverses the direction
    public void ReadBack_KeepsTheScrollSpeedAndDirection(int speed)
    {
        var w = Wheel();
        RingModeBinding.Apply(w, new[] { ScrollMode(speed), new RingMode("Zoom", AuxCombo.Unbound, AuxCombo.Unbound, ScrollAmount: 50) });
        var read = RingModeBinding.Read(w);
        Assert.Equal(2, read.Count);
        Assert.Equal(speed, read[0].WheelScroll);
        Assert.Equal(0, read[0].ScrollAmount);
        Assert.Equal(0, read[1].WheelScroll);          // the zoom mode stays a zoom
        Assert.Equal(50, read[1].ScrollAmount);
    }

    [Fact]
    public void SavedList_KeepsScroll_AndOnlyOneActionPerMode()
    {
        var json = RingModeBinding.Serialize(new[] { ScrollMode(80) });
        Assert.Contains("\"WheelSpeed\":80", json);
        var back = RingModeBinding.Deserialize(json)!;
        Assert.Equal(80, back[0].WheelScroll);
        Assert.Equal(0, back[0].ScrollAmount);

        // A list that somehow carries both a zoom and a scroll keeps the scroll only.
        var both = RingModeBinding.Deserialize("[{\"Name\":\"X\",\"ScrollSpeed\":50,\"WheelSpeed\":60}]")!;
        Assert.Equal(60, both[0].WheelScroll);
        Assert.Equal(0, both[0].ScrollAmount);
    }

    [Fact]
    public void SameKeys_SeesAChangedScrollSpeed()
    {
        Assert.True(RingModeBinding.SameKeys(new[] { ScrollMode(70) }, new[] { ScrollMode(70) }));
        Assert.False(RingModeBinding.SameKeys(new[] { ScrollMode(70) }, new[] { ScrollMode(40) }));
    }

    // ---- Editor: the Function list ----

    private static RingModeRow Row(RingMode mode) => new(1, mode, true, null, () => System.Threading.Tasks.Task.CompletedTask);

    [Fact]
    public void FunctionList_HasScroll_LastSoOldIndicesKeepTheirMeaning()
    {
        Assert.Equal("Scroll", RingModeRow.ActionNames[^1]);
        Assert.Equal((int)RingAction.Scroll, RingModeRow.ActionNames.Count - 1);
        Assert.Equal("Smooth zoom", RingModeRow.ActionNames[(int)RingAction.SmoothZoom]);
    }

    [Fact]
    public void PickingScroll_WritesAScrollMode_AtTheDefaultSpeed_AndHidesTheKeys()
    {
        var row = Row(RingModeBinding.Defaults[1]);   // Zoom (keys)
        row.ActionIndex = (int)RingAction.Scroll;
        var m = row.ToMode();
        Assert.Equal(RingMode.DefaultWheelSpeed, m.WheelScroll);
        Assert.Equal(0, m.ScrollAmount);
        Assert.False(row.UsesKeys);
        Assert.True(row.UseWheel);
        Assert.Equal(RingMode.DefaultWheelSpeed, row.Speed);
        Assert.Contains("mouse wheel", row.ModeNote);
        Assert.False(row.HasModeSetup);
        Assert.Equal("Scroll", row.ActionShort);

        row.CommitSpeed(90);
        Assert.Equal(90, row.ToMode().WheelScroll);

        row.ActionIndex = (int)RingAction.Keys;
        Assert.Equal(0, row.ToMode().WheelScroll);
    }

    [Fact]
    public void AScrollMode_OpensWithScrollPicked()
    {
        var row = Row(ScrollMode(40));
        Assert.Equal(RingAction.Scroll, row.Action);
        Assert.Equal(40, row.Speed);
    }

    // ---- Plugin: a plain wheel in every app ----

    [Theory]
    [InlineData(FrontApp.Other)]
    [InlineData(FrontApp.Photoshop)]
    [InlineData(FrontApp.Affinity)]
    public void Plugin_ScrollsTheWheel_InEveryApp_OneNotchPerModeStep_NoKeys(FrontApp front)
    {
        var lines = new List<int>();
        int zooms = 0;
        RingModeTurnBinding.WheelScroll = lines.Add;
        RingModeTurnBinding.Scroll = _ => zooms++;
        RingModeTurnBinding.FrontApp = () => front;
        var kb = new FakeKb();
        var b = new RingModeTurnBinding { Keyboard = kb, KeyHoldMs = 0, Mode1Keys = "RightBracket", Mode1Wheel = -1, RingStep = 5, Mode1Step = 15 };
        for (int i = 0; i < 9; i++) b.Press(null!, null!);   // 45° of ring
        Assert.Equal(new[] { -1, -1, -1 }, lines);
        Assert.Equal(0, zooms);
        Assert.Empty(kb.Events);
    }
}
