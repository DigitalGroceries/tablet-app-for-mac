using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using OpenTabletArtist.Dynamics;
using OpenTabletDriver.Plugin.Platform.Keyboard;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Build R: ring keys fall back when the Photoshop-only gesture/zoom is refused; timed key hold.</summary>
[Collection("RingModeStatics")]
public class BuildRRingTests : IDisposable
{
    private sealed class FakeKb : IVirtualKeyboard
    {
        public readonly List<string> Events = new();
        public void Press(string key) { lock (Events) Events.Add("+" + key); }
        public void Release(string key) { lock (Events) Events.Add("-" + key); }
        public void Press(IEnumerable<string> keys) { foreach (var k in keys) Press(k); }
        public void Release(IEnumerable<string> keys) { foreach (var k in keys) Release(k); }
        public IEnumerable<string> SupportedKeys => new[] { "LeftBracket", "RightBracket", "Shift", "Alt", "F14", "Application", "Minus" };
        public List<string> Snap() { lock (Events) return Events.ToList(); }
    }

    private readonly Func<GestureKind, double, bool> _g = RingModeTurnBinding.Gesture;
    private readonly Func<FrontApp> _f = RingModeTurnBinding.FrontApp;
    private readonly Action<int> _c = RingModeTurnBinding.CommandScroll;
    private readonly Action<int> _s = RingModeTurnBinding.Scroll;
    private readonly string _path = RingModeState.StateFilePath;
    public BuildRRingTests()
    {
        RingModeState.StateFilePath = Path.Combine(Path.GetTempPath(), "ota-buildR-ring-" + Guid.NewGuid().ToString("N"), "ring-mode");
        RingModeState.SetCount(4); RingModeState.Reset();
    }
    public void Dispose()
    {
        RingModeTurnBinding.Gesture = _g; RingModeTurnBinding.FrontApp = _f; RingModeTurnBinding.Scroll = _s; RingModeTurnBinding.CommandScroll = _c;
        RingModeState.Reset(); RingModeState.StateFilePath = _path;
    }

    private static void ToMode(int m) { RingModeState.Reset(); for (int i = 0; i < m; i++) RingModeState.Next(); }

    private static (RingModeTurnBinding B, FakeKb Kb) UserRing()
    {
        var kb = new FakeKb();
        var b = new RingModeTurnBinding
        {
            Keyboard = kb, KeyHoldMs = 0,
            Mode1Keys = "Alt+F14", Mode2Keys = "Application+Minus", Mode3Keys = "LeftBracket", Mode4Keys = "Shift+RightBracket",
            Mode1Rotate = -10, Mode2Scroll = -5, Mode3Size = -10, Mode4Hardness = -10,
        };
        return (b, kb);
    }

    [Theory]
    [InlineData(0, "+Alt,+F14,-F14,-Alt")]
    [InlineData(2, "+LeftBracket,-LeftBracket")]
    [InlineData(3, "+Shift,+RightBracket,-RightBracket,-Shift")]
    public void RefusedGesture_SendsTheModesKeys(int mode, string expected)
    {
        int calls = 0;
        RingModeTurnBinding.Gesture = (_, _) => { calls++; return false; };
        var (b, kb) = UserRing(); ToMode(mode);
        b.Press(null!, null!); b.Release(null!, null!);
        Assert.Equal(1, calls);
        Assert.Equal(expected, string.Join(",", kb.Snap()));
    }

    [Theory] [InlineData(0)] [InlineData(2)] [InlineData(3)]
    public void AcceptedGesture_SendsNoKeys(int mode)
    {
        RingModeTurnBinding.Gesture = (_, _) => true;
        var (b, kb) = UserRing(); ToMode(mode);
        b.Press(null!, null!);
        Assert.Empty(kb.Snap());
    }

    [Fact]
    public void Zoom_UsesOptionScroll_OnlyWhenPhotoshopIsInFront()
    {
        int scrolled = 0;
        RingModeTurnBinding.Scroll = _ => scrolled++;
        var (b, kb) = UserRing(); ToMode(1);
        RingModeTurnBinding.FrontApp = () => FrontApp.Photoshop;
        b.Press(null!, null!);
        Assert.Equal(1, scrolled); Assert.Empty(kb.Snap());
        RingModeTurnBinding.FrontApp = () => FrontApp.Other;
        b.Press(null!, null!);
        Assert.Equal(1, scrolled);
        Assert.Equal("+Application,+Minus,-Minus,-Application", string.Join(",", kb.Snap()));   // Cmd up last
    }

    // ---- Version 16 BETA 2: Affinity, and one zoom line per step ----

    [Fact]
    public void Zoom_WorksInAffinityToo_OneLinePerStep_WithTheDirectionsSign()
    {
        var lines = new List<int>();
        RingModeTurnBinding.Scroll = lines.Add;
        RingModeTurnBinding.FrontApp = () => FrontApp.Affinity;
        var (b, kb) = UserRing(); ToMode(1);
        b.RingStep = 5; b.Mode2Step = 30;   // Speed ≈ 50: one wheel line per 30° of ring
        for (int i = 0; i < 12; i++) b.Press(null!, null!);   // 60° of ring
        Assert.Equal(new[] { -10, -10 }, lines);   // Mode2Scroll is -5 here: only its sign counts, one line each
        Assert.Empty(kb.Snap());
    }

    [Fact]
    public void Rotate_InAffinity_SendsCommandScroll_PerStep_NotTheKeys()
    {
        var lines = new List<int>();
        RingModeTurnBinding.Gesture = (_, _) => false;   // not Photoshop: the drag gesture is refused
        RingModeTurnBinding.CommandScroll = lines.Add;
        RingModeTurnBinding.FrontApp = () => FrontApp.Affinity;
        var (b, kb) = UserRing(); ToMode(0);
        b.RingStep = 5; b.Mode1Step = 10;
        for (int i = 0; i < 4; i++) b.Press(null!, null!);
        Assert.Equal(new[] { -10, -10 }, lines);   // Mode1Rotate is -10: anticlockwise store
        Assert.Empty(kb.Snap());
    }

    [Fact]
    public void Rotate_InAnotherApp_StillSendsTheModesKeys_AtTheStep()
    {
        int lines = 0;
        RingModeTurnBinding.Gesture = (_, _) => false;
        RingModeTurnBinding.CommandScroll = _ => lines++;
        RingModeTurnBinding.FrontApp = () => FrontApp.Other;
        var (b, kb) = UserRing(); ToMode(0);
        b.RingStep = 5; b.Mode1Step = 10;
        for (int i = 0; i < 4; i++) b.Press(null!, null!);
        Assert.Equal(0, lines);
        Assert.Equal(2, kb.Snap().Count(e => e == "+F14"));   // a key every 10°, not every ring step
    }

    [Theory]
    [InlineData("com.adobe.Photoshop", "Adobe Photoshop 2026", FrontApp.Photoshop)]
    [InlineData("com.seriflabs.affinityphoto2", "Affinity Photo 2", FrontApp.Affinity)]
    [InlineData("com.example.unknown", "Affinity", FrontApp.Affinity)]
    [InlineData(null, "Affinity", FrontApp.Affinity)]
    [InlineData("com.photocraft.app", "PhotoCraft", FrontApp.Other)]
    [InlineData(null, null, FrontApp.Other)]
    public void FrontApp_IsRecognised_ByBundleIdOrWindowOwner(string? bundle, string? owner, FrontApp expected)
    {
        Assert.Equal(expected, MacGestureIo.Classify(bundle, owner));
    }

    [Fact]
    public void KeyPulse_HoldsThenReleasesOnATimer_WithoutBlocking()
    {
        var kb = new FakeKb(); var p = new KeyPulse { HoldMs = 18 };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        p.Press(kb, new[] { "Application", "Minus" });
        Assert.True(sw.ElapsedMilliseconds < 15);   // returns at once
        Assert.True(p.Holding);
        Assert.Equal("+Application,+Minus", string.Join(",", kb.Snap()));
        SpinWait.SpinUntil(() => !p.Holding, 2000);
        Assert.False(p.Holding);
        Assert.Equal("+Application,+Minus,-Minus,-Application", string.Join(",", kb.Snap()));
    }

    [Fact]
    public void KeyPulse_NewNotch_ReleasesTheHeldKeyFirst_AndOldTimerDoesNothing()
    {
        var kb = new FakeKb(); var p = new KeyPulse { HoldMs = 60 };
        p.Press(kb, new[] { "LeftBracket" });
        p.Press(kb, new[] { "RightBracket" });
        Assert.Equal("+LeftBracket,-LeftBracket,+RightBracket", string.Join(",", kb.Snap()));
        SpinWait.SpinUntil(() => !p.Holding, 2000);
        Assert.Equal("+LeftBracket,-LeftBracket,+RightBracket,-RightBracket", string.Join(",", kb.Snap()));
    }

    [Fact]
    public void KeyPulse_ManyThreads_EveryDownHasOneUp_InOrder()
    {
        var kb = new FakeKb(); var p = new KeyPulse { HoldMs = 2 };
        var ts = Enumerable.Range(0, 8).Select(_ => new Thread(() => { for (int i = 0; i < 50; i++) p.Press(kb, new[] { "Shift", "RightBracket" }); })).ToList();
        ts.ForEach(t => t.Start()); ts.ForEach(t => t.Join());
        p.ReleaseNow();
        var ev = kb.Snap();
        Assert.Equal(800, ev.Count(e => e.StartsWith("+")));
        Assert.Equal(800, ev.Count(e => e.StartsWith("-")));
        for (int i = 0; i < ev.Count; i += 4)
            Assert.Equal("+Shift,+RightBracket,-RightBracket,-Shift", string.Join(",", ev.Skip(i).Take(4)));
    }
}

public class BuildRRpcTraceTests
{
    [Fact]
    public void QuietRpcTrace_KeepsJsonRpcOutOfTheSystemLog_AndForwardsErrors()
    {
        using var ms = new System.IO.MemoryStream();
        var rpc = new StreamJsonRpc.JsonRpc(ms);
        var got = new List<string>();
        OtdInterop.QuietRpcTrace.Apply(rpc, got.Add);
        Assert.DoesNotContain(rpc.TraceSource.Listeners.Cast<System.Diagnostics.TraceListener>(), l => l is System.Diagnostics.DefaultTraceListener);
        rpc.TraceSource.TraceEvent(System.Diagnostics.TraceEventType.Error, 1, "connection lost");
        rpc.TraceSource.TraceEvent(System.Diagnostics.TraceEventType.Information, 2, "chatter");
        Assert.Single(got);
        Assert.Contains("connection lost", got[0]);
    }
}
