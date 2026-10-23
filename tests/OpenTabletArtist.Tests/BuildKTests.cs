using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Dynamics;
using OpenTabletArtist.ViewModels;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Build K: smooth brush size/hardness gesture, exclusive per-mode actions, Mode 4 default, sensitivity
/// rescale, smooth zoom line accumulation.</summary>
public class BuildKTests
{
    private sealed class FakeIo : IGestureIo
    {
        public bool Watchdog = true, Gate = true, Pen, ThrowOnModsUp;
        public (double, double)? CursorAt = (500, 300);
        public (double, double, double, double)? Window = (0, 0, 1000, 800);
        public readonly List<string> Events = new();
        public (double X, double Y)? Cursor() => CursorAt;
        public (double X, double Y, double W, double H)? PhotoshopWindowUnderCursor() => Window;
        public bool WatchdogFresh() => Watchdog;
        public bool GateInstalled() => Gate;
        public bool PenBusy() => Pen;
        public void KeyR(bool down) => Events.Add(down ? "R down" : "R up");
        public void KeyControlOption(bool down)
        {
            if (!down && ThrowOnModsUp) throw new InvalidOperationException("boom");
            Events.Add(down ? "CO down" : "CO up");
        }
        public void MouseDown(double x, double y) => Events.Add($"down {x:0},{y:0}");
        public void MouseDrag(double x, double y) => Events.Add($"drag {x:0},{y:0}");
        public void MouseUp(double x, double y) => Events.Add($"up {x:0},{y:0}");
        public void Warp(double x, double y) => Events.Add($"warp {x:0},{y:0}");
        public void Heartbeat(bool active) => Events.Add(active ? "hb active" : "hb idle");
        public void Log(string message) => Events.Add("log " + message);
        public List<string> Input => Events.Where(e => !e.StartsWith("log") && e != "hb active").ToList();
    }

    private static (DragRotateCore Core, FakeIo Io, Func<long, long> Advance) New()
    {
        long now = 1000;
        var io = new FakeIo();
        return (new DragRotateCore(io, () => now), io, ms => now += ms);
    }

    [Theory]
    [InlineData("watchdog")] [InlineData("gate")] [InlineData("pen")] [InlineData("photoshop")] [InlineData("cursor")]
    public void Brush_Refuses_AndPressesNothing_WhenAnySafetyConditionFails(string failing)
    {
        var (core, io, _) = New();
        switch (failing)
        {
            case "watchdog": io.Watchdog = false; break;
            case "gate": io.Gate = false; break;
            case "pen": io.Pen = true; break;
            case "photoshop": io.Window = null; break;
            case "cursor": io.CursorAt = null; break;
        }
        Assert.False(core.Turn(GestureKind.BrushSize, 5));
        Assert.DoesNotContain(io.Events, e => e.StartsWith("CO") || e.StartsWith("down") || e.StartsWith("hb"));
        Assert.Contains(io.Events, e => e.StartsWith("log Smooth brush size not started"));
    }

    [Fact]
    public void Size_HeartbeatFirst_ThenControlOption_ThenDragsRight_AndReleasesAll()
    {
        var (core, io, advance) = New();
        Assert.True(core.Turn(GestureKind.BrushSize, 5));
        core.Turn(GestureKind.BrushSize, 5);
        core.Turn(GestureKind.BrushSize, -3);
        Assert.Equal(new[] { "hb active", "CO down", "down 500,300" }, io.Events.Where(e => !e.StartsWith("log")).Take(3));
        Assert.Equal(new[] { "drag 505,300", "drag 510,300", "drag 507,300" }, io.Events.Where(e => e.StartsWith("drag")));
        advance(310); core.Tick();
        Assert.False(core.Active);
        Assert.Equal(new[] { "up 507,300", "CO up", "warp 500,300", "hb idle" }, io.Input.TakeLast(4));
        Assert.DoesNotContain(io.Events, e => e.StartsWith("R"));
    }

    [Fact]
    public void Hardness_Positive_DragsUp()
    {
        var (core, io, _) = New();
        core.Turn(GestureKind.BrushHardness, 10);
        Assert.Equal("drag 500,290", io.Events.Last(e => e.StartsWith("drag")));
    }

    [Fact]
    public void HardCap_AndStaleWatchdog_ReleaseBrushGesture()
    {
        var (core, io, advance) = New();
        core.Turn(GestureKind.BrushSize, 5);
        for (int t = 0; t < 3000; t += 100) { advance(100); core.Turn(GestureKind.BrushSize, 5); core.Tick(); }
        Assert.False(core.Active);
        Assert.Contains("CO up", io.Events);

        var (c2, io2, a2) = New();
        c2.Turn(GestureKind.BrushHardness, 5);
        io2.Watchdog = false; a2(50); c2.Tick();
        Assert.False(c2.Active);
        Assert.Contains("CO up", io2.Events);
    }

    [Fact]
    public void ReleaseSteps_AllRun_EvenIfModifierReleaseThrows()
    {
        var (core, io, _) = New();
        core.Turn(GestureKind.BrushSize, 5);
        io.ThrowOnModsUp = true;
        core.Release("test");
        Assert.Equal(new[] { "warp 500,300", "hb idle" }, io.Input.TakeLast(2));
        Assert.False(core.Active);
    }

    [Fact]
    public void SwitchingKind_ReleasesTheOldGestureFirst()
    {
        var (core, io, _) = New();
        core.Turn(GestureKind.Rotate, 5);
        core.Turn(GestureKind.BrushSize, 5);
        int rUp = io.Events.IndexOf("R up"), coDown = io.Events.IndexOf("CO down");
        Assert.True(rUp >= 0 && coDown > rUp);
    }

    // ---- profile & settings ----

    private static WheelBindingSettings Wheel() { var w = new WheelBindingSettings(); w.WheelButtons.Add(null!); return w; }

    [Fact]
    public void SizeAndHardness_WrittenPerMode_Signed_AndReadBack_NotShared()
    {
        var w = Wheel();
        var modes = new[]
        {
            new RingMode("Rotate", AuxCombo.Unbound, AuxCombo.Unbound, RotateSensitivity: 10),
            new RingMode("Zoom", AuxCombo.Unbound, AuxCombo.Unbound, ScrollAmount: 20),
            new RingMode("Size", AuxCombo.Unbound, AuxCombo.Unbound, SizeSensitivity: 12),
            new RingMode("Hard", AuxCombo.Unbound, AuxCombo.Unbound, HardnessSensitivity: 6),
        };
        Assert.True(RingModeBinding.Apply(w, modes));
        var back = RingModeBinding.Read(w);
        Assert.Equal(new[] { 10, 0, 0, 0 }, back.Select(m => m.RotateSensitivity));
        Assert.Equal(new[] { 0, 20, 0, 0 }, back.Select(m => m.ScrollAmount));
        Assert.Equal(new[] { 0, 0, 12, 0 }, back.Select(m => m.SizeSensitivity));
        Assert.Equal(new[] { 0, 0, 0, 6 }, back.Select(m => m.HardnessSensitivity));
        Assert.Equal(-2.4f, Convert.ToSingle(w.ClockwiseRotation!.Settings.First(s => s.Property == "Mode3Size").Value, System.Globalization.CultureInfo.InvariantCulture), 3);
        var json = RingModeBinding.Deserialize(RingModeBinding.Serialize(modes))!;
        Assert.Equal(new[] { 10, 0, 0, 0 }, json.Select(m => m.RotateSensitivity));
        Assert.Equal(new[] { 0, 0, 0, 6 }, json.Select(m => m.HardnessSensitivity));
        var p = new Profile { Tablet = "x", OutputMode = new OpenTabletDriver.Desktop.Reflection.PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)) }; p.BindingSettings.WheelBindings.Add(w);
        Assert.True(RingModeBinding.UsesSmoothRotate(p));
    }

    [Fact]
    public void GateIsAdded_ForBrushOnlyModes()
    {
        var w = Wheel();
        RingModeBinding.Apply(w, new[] { new RingMode("S", AuxCombo.Unbound, AuxCombo.Unbound, SizeSensitivity: 10) });
        var p = new Profile { Tablet = "x", OutputMode = new OpenTabletDriver.Desktop.Reflection.PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)) }; p.BindingSettings.WheelBindings.Add(w);
        RingModeBinding.SyncGate(p);
        Assert.Contains(p.Filters, f => f.Path == RingModeBinding.GateTypeName);
    }

    private static RingModeRow Row(RingMode m, List<int> changes) =>
        new(1, m, true, null, () => { changes.Add(1); return Task.CompletedTask; });

    [Fact]
    public void Row_Actions_AreExclusive()
    {
        var changes = new List<int>();
        var row = Row(RingModeBinding.Defaults[0], changes);
        Assert.True(row.UseKeys);
        row.UseRotate = true;
        row.UseSize = true;
        Assert.False(row.UseRotate);
        Assert.True(row.UseSize);
        var m = row.ToMode();
        Assert.Equal(0, m.RotateSensitivity);
        Assert.Equal(RingMode.DefaultBrushSensitivity, m.SizeSensitivity);
        row.UseHardness = true;
        Assert.Equal(new[] { 0, 0, 50 }, new[] { row.ToMode().SizeSensitivity, row.ToMode().ScrollAmount, row.ToMode().HardnessSensitivity });
        row.UseKeys = true;
        Assert.True(row.UsesKeys);
        Assert.False(row.ToMode().UsesDragGesture);
    }

    [Fact]
    public void Editor_Rows_DoNotShareState()
    {
        var ed = new RingModesEditor(0, RingModeBinding.Defaults, true, true, true, null, null, _ => Task.CompletedTask);
        ed.Rows[0].UseRotate = true;
        Assert.Equal(new[] { true, false, false, false }, ed.Rows.Select(r => r.UseRotate));
        Assert.Equal(new[] { 50, 0, 0, 0 }, ed.Modes.Select(m => m.RotateSensitivity));
    }

    [Fact]
    public void Defaults_Mode4_IsBrushHardness_AndOldRingKeysAreNotSeeded()
    {
        var w = Wheel();
        var list = RingModeBinding.Load(w, null, AuxKeyBinding.ParseKeys("LeftBracket"), AuxKeyBinding.ParseKeys("RightBracket"));
        Assert.Equal("Brush hardness", list[3].Name);
        Assert.Equal("Shift+RightBracket", AuxKeyBinding.FormatKeys(list[3].Clockwise));
        Assert.Equal("Shift+LeftBracket", AuxKeyBinding.FormatKeys(list[3].CounterClockwise));
    }

    [Theory]
    [InlineData("Ring", "Brush hardness")]
    [InlineData("Hardness", "Hardness")]
    public void SavedSeededRing_MigratesToHardnessKeys(string name, string expected)
    {
        var saved = RingModeBinding.Serialize(new[]
        {
            new RingMode(name, AuxKeyBinding.ParseKeys("LeftBracket")!, AuxKeyBinding.ParseKeys("RightBracket")!),
        });
        var m = RingModeBinding.Deserialize(saved)![0];
        Assert.Equal(expected, m.Name);
        Assert.Equal("Shift+RightBracket", AuxKeyBinding.FormatKeys(m.Clockwise));
        // A real "Brush size" mode is never touched.
        var size = RingModeBinding.Deserialize(RingModeBinding.Serialize(new[] { RingModeBinding.Defaults[2] }))![0];
        Assert.Equal("RightBracket", AuxKeyBinding.FormatKeys(size.Clockwise));
    }

    [Fact]
    public void JMinimum_IsTheNewDefaultSpeed_InTheMiddle_AndKeepsTheSameFeel()
    {
        Assert.Equal(50, RingMode.DefaultSpeed);
        Assert.Equal(10f, RingMode.GainOf(RingMode.DefaultSpeed));   // J gain 10 = canvas 10 % of ring travel
        var j = RingModeBinding.Deserialize("[{\"Name\":\"Rotate\",\"Rotate\":10}]")![0];
        Assert.Equal(50, j.RotateSensitivity);
        // A J profile (int gain 10) reads back as speed 50.
        var w = Wheel();
        RingModeBinding.Apply(w, new[] { new RingMode("R", AuxCombo.Unbound, AuxCombo.Unbound, RotateSensitivity: 50) });
        w.CounterClockwiseRotation!.Settings.First(x => x.Property == "Mode1Rotate").Value = 10;
        Assert.Equal(50, RingModeBinding.Read(w)[0].RotateSensitivity);
    }

    [Fact]
    public void Speed_HigherIsAlwaysFaster_ForKeysAndZoom()
    {
        float prev = float.MaxValue;
        for (int sp = 1; sp <= 100; sp++) { var d = RingMode.DegreesOfSpeed(sp); Assert.True(d <= prev); prev = d; }
        Assert.Equal(180f, RingMode.DegreesOfSpeed(1));
        Assert.Equal(5f, RingMode.DegreesOfSpeed(100));
        Assert.Equal(100, RingMode.SpeedOfDegrees(0));
        foreach (var deg in new[] { 8f, 10f, 30f, 60f }) Assert.InRange(RingMode.DegreesOfSpeed(RingMode.SpeedOfDegrees(deg)), deg * 0.94f, deg * 1.06f);
        Assert.True(RingMode.ScrollOfSpeed(80) > RingMode.ScrollOfSpeed(20));
        Assert.Equal(20, RingMode.ScrollOfSpeed(50));
        Assert.True(RingMode.SpeedOfDegrees(RingModeBinding.Defaults[3].StepDegrees) < 40);   // hardness keys: slow
    }

    [Fact]
    public void Row_Speed_CommitsToTheCurrentAction()
    {
        var changes = new List<int>();
        var row = Row(RingModeBinding.Defaults[2], changes);
        row.CommitSpeed(100);
        Assert.Equal(5f, row.ToMode().StepDegrees);
        row.CommitSpeed(1);
        Assert.Equal(180f, row.ToMode().StepDegrees);
        row.UseRotate = true;
        Assert.Equal(50, row.Speed);
        row.SpeedText = "70";
        Assert.Equal(70, row.ToMode().RotateSensitivity);
        row.UseScroll = true;
        row.CommitSpeed(50);
        Assert.Equal(20, row.ToMode().ScrollAmount);
    }

    [Fact]
    public void ZoomLines_AccumulateRemainders()
    {
        int rem = 0;
        Assert.Equal(0, MacScroll.TakeLines(4, ref rem));
        Assert.Equal(0, MacScroll.TakeLines(4, ref rem));
        Assert.Equal(1, MacScroll.TakeLines(4, ref rem));
        Assert.Equal(2, rem);
        Assert.Equal(-1, MacScroll.TakeLines(-12, ref rem));
    }
}
