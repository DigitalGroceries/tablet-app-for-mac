using System;
using System.Collections.Generic;
using System.Linq;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Dynamics;
using OpenTabletArtist.Services;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Build J: the EXPERIMENTAL smooth-rotate gesture's safety rules, the watchdog's decision, and the
/// profile wiring (pen gate filter).</summary>
public class BuildJTests
{
    private sealed class FakeIo : IGestureIo
    {
        public bool Watchdog = true, Gate = true, Pen;
        public (double, double)? CursorAt = (500, 300);
        public (double, double, double, double)? Window = (0, 0, 1000, 800);   // centre (500, 400)
        public readonly List<string> Events = new();
        public bool ThrowOnKeyUp;

        public (double X, double Y)? Cursor() => CursorAt;
        public (double X, double Y, double W, double H)? PhotoshopWindowUnderCursor() => Window;
        public bool WatchdogFresh() => Watchdog;
        public bool GateInstalled() => Gate;
        public bool PenBusy() => Pen;
        public void KeyR(bool down)
        {
            if (!down && ThrowOnKeyUp) throw new InvalidOperationException("boom");
            Events.Add(down ? "R down" : "R up");
        }
        public void MouseDown(double x, double y) => Events.Add($"down {x:0},{y:0}");
        public void MouseDrag(double x, double y) => Events.Add($"drag {x:0},{y:0}");
        public void MouseUp(double x, double y) => Events.Add($"up {x:0},{y:0}");
        public void Warp(double x, double y) => Events.Add($"warp {x:0},{y:0}");
        public void Heartbeat(bool active) => Events.Add(active ? "hb active" : "hb idle");
        public void Log(string message) => Events.Add("log " + message);
        public IEnumerable<string> Input => Events.Where(e => !e.StartsWith("log") && e != "hb active");
    }

    private static (DragRotateCore Core, FakeIo Io, Func<long, long> Advance) New()
    {
        long now = 1000;
        var io = new FakeIo();
        var core = new DragRotateCore(io, () => now);
        return (core, io, ms => now += ms);
    }

    [Theory]
    [InlineData("watchdog")]
    [InlineData("gate")]
    [InlineData("pen")]
    [InlineData("photoshop")]
    [InlineData("cursor")]
    public void Refuses_AndPressesNothing_WhenAnySafetyConditionFails(string failing)
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
        Assert.False(core.Turn(10));
        Assert.False(core.Active);
        Assert.DoesNotContain(io.Events, e => e.StartsWith("R") || e.StartsWith("down") || e.StartsWith("hb"));
        Assert.Contains(io.Events, e => e.StartsWith("log Smooth rotate not started"));
    }

    [Fact]
    public void Start_MarksTheHeartbeatActive_BeforePressingAnything_ThenDragsAlongAnArc()
    {
        var (core, io, _) = New();
        // Cursor (500,300) is 100 above the centre (500,400): start there, angle -90°.
        Assert.True(core.Turn(90));
        var first = io.Events.Where(e => !e.StartsWith("log")).Take(3).ToList();
        Assert.Equal(new[] { "hb active", "R down", "down 500,300" }, first);
        // +90° clockwise (screen y down) from straight up → to the right of centre.
        Assert.Equal("drag 600,400", io.Events.Last(e => e.StartsWith("drag")));
    }

    [Fact]
    public void CursorNearTheCentre_StartsOnAUsableRadius_AndPutsTheCursorBackAfterwards()
    {
        var (core, io, advance) = New();
        io.CursorAt = (505, 400);
        core.Turn(0);
        Assert.Contains("warp 560,400", io.Events);   // moved out to the 60 pt minimum radius
        advance(400);
        core.Tick();
        Assert.Equal("warp 505,400", io.Events.Last(e => e.StartsWith("warp")));
    }

    [Fact]
    public void ReleasesEverything_After300msWithoutRingMovement()
    {
        var (core, io, advance) = New();
        core.Turn(5);
        advance(250); core.Tick();
        Assert.True(core.Active);
        advance(60); core.Tick();
        Assert.False(core.Active);
        var tail = io.Events.Where(e => !e.StartsWith("log")).TakeLast(4).ToList();
        Assert.Equal("up", tail[0].Split(' ')[0]);
        Assert.Equal("R up", tail[1]);
        Assert.StartsWith("warp", tail[2]);
        Assert.Equal("hb idle", tail[3]);
    }

    [Fact]
    public void HardCap_ReleasesAfter3s_EvenWhileTheRingKeepsTurning()
    {
        var (core, io, advance) = New();
        core.Turn(5);
        for (int t = 0; t < 2900; t += 100) { advance(100); core.Turn(5); core.Tick(); }
        Assert.True(core.Active);
        advance(100); core.Tick();
        Assert.False(core.Active);
        Assert.Contains(io.Events, e => e.Contains("3 s limit"));
    }

    [Fact]
    public void WatchdogGoingStale_MidGesture_Releases()
    {
        var (core, io, advance) = New();
        core.Turn(5);
        io.Watchdog = false;
        advance(50); core.Tick();
        Assert.False(core.Active);
        Assert.Contains("R up", io.Events);
        Assert.Contains(io.Events, e => e.StartsWith("up "));
    }

    [Fact]
    public void AFailingReleaseStep_DoesNotStopTheOthers_AndReleaseIsIdempotent()
    {
        var (core, io, _) = New();
        core.Turn(5);
        io.ThrowOnKeyUp = true;
        core.Release("test");
        Assert.Contains(io.Events, e => e.StartsWith("up "));
        Assert.Contains("hb idle", io.Events);
        int count = io.Events.Count;
        core.Release("again");
        Assert.Equal(count, io.Events.Count);
    }

    [Fact]
    public void OnlyOneGestureAtATime_TurnsContinueTheSameDrag()
    {
        var (core, io, advance) = New();
        core.Turn(5); advance(50); core.Turn(5); advance(50); core.Turn(5);
        Assert.Single(io.Events, "R down");
        Assert.Single(io.Events, e => e.StartsWith("down "));
    }

    // ---- watchdog decision ----

    [Theory]
    [InlineData(null, false)]
    [InlineData("idle 42 1000", false)]
    [InlineData("active 42 1500", false)]   // alive, 500 ms old
    [InlineData("active 42 900", true)]     // alive, 1100 ms old → stale
    [InlineData("active 7 1900", true)]     // process gone
    [InlineData("active x y", true)]        // unreadable while active
    public void Watchdog_RecoversOnlyAnActiveGestureThatIsDeadOrStale(string? hb, bool recover)
    {
        var (r, _, reason) = RingGestureWatchdog.Decide(hb, 2000, pid => pid == 42);
        Assert.Equal(recover, r);
        if (recover) Assert.False(string.IsNullOrEmpty(reason));
    }

    // ---- profile wiring ----

    private static Profile ProfileWithRing(RingMode mode)
    {
        var p = new Profile { Tablet = "PTK-440", OutputMode = new OpenTabletDriver.Desktop.Reflection.PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)) };
        var w = new WheelBindingSettings();
        w.WheelButtons.Add(null!);
        p.BindingSettings.WheelBindings.Add(w);
        Assert.True(RingModeBinding.Apply(w, new[] { mode }));
        return p;
    }

    [Fact]
    public void SmoothRotate_IsWrittenSignedPerDirection_AndReadsBack()
    {
        var p = ProfileWithRing(new RingMode("Rotate", AuxCombo.Unbound, AuxCombo.Unbound, RotateSensitivity: 15));
        var w = p.BindingSettings.WheelBindings[0];
        string? Get(OpenTabletDriver.Desktop.Reflection.PluginSettingStore s) =>
            s.Settings.First(x => x.Property == "Mode1Rotate").Value?.ToString();
        // build K: speed 15 → gain 3. Version 16 BETA 2: physical clockwise is OTD's ClockwiseRotation.
        Assert.Equal(3f, Convert.ToSingle(Get(w.ClockwiseRotation!), System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(-3f, Convert.ToSingle(Get(w.CounterClockwiseRotation!), System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(15, RingModeBinding.Read(w)[0].RotateSensitivity);
        var back = RingModeBinding.Deserialize(RingModeBinding.Serialize(RingModeBinding.Read(w)))!;
        Assert.Equal(15, back[0].RotateSensitivity);
    }

    [Fact]
    public void Gate_IsAddedOnlyForSmoothRotate_AndRemovedWithoutTouchingOtherFilters()
    {
        var p = ProfileWithRing(new RingMode("Rotate", AuxCombo.Unbound, AuxCombo.Unbound, RotateSensitivity: 100));
        var other = AuxKeyBinding.NewStore("Some.Other.Filter");
        p.Filters.Add(other);
        RingModeBinding.SyncGate(p);
        RingModeBinding.SyncGate(p);   // idempotent
        var gate = Assert.Single(p.Filters, f => f.Path == RingModeBinding.GateTypeName);
        Assert.True(gate.Enable);

        RingModeBinding.Apply(p.BindingSettings.WheelBindings[0],
            new[] { new RingMode("Zoom", AuxCombo.Unbound, AuxCombo.Unbound, ScrollAmount: 20) });
        RingModeBinding.SyncGate(p);
        Assert.DoesNotContain(p.Filters, f => f.Path == RingModeBinding.GateTypeName);
        Assert.Contains(other, p.Filters);
    }

    [Fact]
    public void Gate_IsAnApprovedFilter_SoOtasFilterRuleLeavesItOn()
    {
        var p = ProfileWithRing(new RingMode("Rotate", AuxCombo.Unbound, AuxCombo.Unbound, RotateSensitivity: 100));
        RingModeBinding.SyncGate(p);
        var settings = new Settings { Profiles = new ProfileCollection { p } };
        ProfileFilterMaintenance.DisableUnapprovedFilters(settings);
        ProfileFilterMaintenance.CleanLegacyFilters(settings);
        Assert.True(Assert.Single(p.Filters, f => f.Path == RingModeBinding.GateTypeName).Enable);
    }

    [Fact]
    public void RotateAndScroll_AreExclusive_InTheModeRow()
    {
        int changes = 0;
        var row = new ViewModels.RingModeRow(1, new RingMode("R", AuxCombo.Unbound, AuxCombo.Unbound, ScrollAmount: 20),
            true, null, () => { changes++; return System.Threading.Tasks.Task.CompletedTask; });
        Assert.True(row.UseScroll);
        row.UseRotate = true;
        Assert.False(row.UseScroll);
        var m = row.ToMode();
        Assert.Equal(RingMode.DefaultRotateSensitivity, m.RotateSensitivity);
        Assert.Equal(0, m.ScrollAmount);
        row.UseRotate = false;
        Assert.Equal(0, row.ToMode().RotateSensitivity);
        Assert.False(row.ToMode().IsMapped);   // nothing left: no keys, no scroll, no rotate
    }

    [Fact]
    public void SmoothRotate_IsOffByDefault()
    {
        Assert.All(RingModeBinding.Defaults, m => Assert.Equal(0, m.RotateSensitivity));
        Assert.Equal(0, new RingMode("x", AuxCombo.Unbound, AuxCombo.Unbound).RotateSensitivity);
    }
}
