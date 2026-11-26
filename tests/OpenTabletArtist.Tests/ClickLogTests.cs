using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OpenTabletArtist.Services;
using OtdInterop;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>TASKS #85 (Digital Groceries 17 Nov 2026): Record the problem also records the mouse and the pen's clicks, so a
/// mouse click and a pen click on the same windows can be compared.</summary>
public class ClickLogTests
{
    private const long Daemon = 4321;

    private static MouseSample Ev(double ms, int type, long pid = 0, long state = 1, long window = 0, double x = 100) =>
        new(TimeSpan.FromMilliseconds(ms), type, x, 200, Button: 0, ClickState: 1, Subtype: 0, EventNumber: 7,
            Pressure: type == 1 ? 1 : 0, SourcePid: pid, SourceStateId: state, SourceUserData: 0, TargetPid: 0,
            WindowUnder: window, WindowThatCanHandle: window, DeltaX: 0, DeltaY: 0, Flags: 0x100, Timestamp: 1);

    [Fact]
    public void Clicks_AreAlwaysKept_MovesAreThinned_AndTheLastMoveBeforeAClickIsKept()
    {
        var log = new ClickLog();
        for (var ms = 0; ms < 100; ms += 5) log.Add(Ev(ms, 5, x: ms));   // 20 moves in 100 ms
        log.Add(Ev(101, 1));
        log.Add(Ev(180, 2));
        var text = log.Format();
        var moves = text.Split('\n').Count(l => l.Contains("MouseMoved"));
        Assert.Equal(3, moves); // the first, the one 50 ms later and the last one before the click
        Assert.Contains("at (95.0, 200.0)", text); // the last move before the down
        Assert.Contains("LeftMouseDown", text);
        Assert.Contains("LeftMouseUp", text);
        Assert.Contains("released after 79 ms", text);
        Assert.Contains("thinned out", text);
    }

    [Fact]
    public void TheSource_SaysWhetherTheMouseOrAProgramSentTheClick()
    {
        var log = new ClickLog();
        log.NameProcess(Daemon, "OpenTabletDriver.Daemon");
        log.Add(Ev(10, 1));
        log.Add(Ev(20, 2));
        log.Add(Ev(1000, 1, pid: Daemon, state: -1));
        log.Add(Ev(1050, 2, pid: Daemon, state: -1));
        var text = log.Format();
        Assert.Contains("Clicks: 2", text);
        Assert.Contains("#1", text);
        Assert.Contains("from the mouse or trackpad (HID, pid 0)", text);
        Assert.Contains("posted by OpenTabletDriver.Daemon (pid 4321), state private", text);
    }

    [Fact]
    public void EachClick_ShowsHowLongTheFrontAppAndWindowTookToChange()
    {
        var log = new ClickLog();
        log.NameWindow(77, 500, "Safari", 0);
        log.Front(TimeSpan.Zero, ClickLog.FrontKind.App, 100, "Finder");
        log.Front(TimeSpan.Zero, ClickLog.FrontKind.Window, 100, "Finder");
        log.Add(Ev(1000, 1, pid: Daemon, state: -1, window: 77));
        log.Front(TimeSpan.FromMilliseconds(1020), ClickLog.FrontKind.Window, 500, "Safari");
        log.Front(TimeSpan.FromMilliseconds(2012), ClickLog.FrontKind.App, 500, "Safari");
        var text = log.Format();
        Assert.Contains("window under the pointer: 77 (Safari, pid 500, layer 0)", text);
        Assert.Contains("front app: Finder -> Safari after 1012 ms", text);
        Assert.Contains("front window: Finder -> Safari after 20 ms", text);
    }

    [Fact]
    public void AClickOnTheAppAlreadyInFront_SaysSo_AndOnlyChangesAreKept()
    {
        var log = new ClickLog();
        log.NameWindow(5, 100, "Finder", 0);
        log.Front(TimeSpan.Zero, ClickLog.FrontKind.App, 100, "Finder");
        log.Front(TimeSpan.FromMilliseconds(500), ClickLog.FrontKind.App, 100, "Finder");
        log.Add(Ev(600, 1, window: 5));
        var text = log.Format();
        Assert.Contains("front app: stayed Finder (already in front)", text);
        Assert.Single(text.Split('\n'), l => l.Contains("front app: Finder (pid 100)"));
    }

    [Fact]
    public void WindowsAndProcessesStillToBeNamed_AreListedOnce()
    {
        var log = new ClickLog();
        log.Add(Ev(10, 1, pid: Daemon, state: -1, window: 9));
        log.Add(Ev(20, 2, pid: Daemon, state: -1, window: 9));
        Assert.Equal(new long[] { 9 }, log.UnnamedWindows());
        Assert.Equal(new long[] { Daemon }, log.UnnamedProcesses());
        log.NameWindow(9, 1, "Notes", 0);
        log.NameProcess(Daemon, "OpenTabletDriver.Daemon");
        Assert.Empty(log.UnnamedWindows());
        Assert.Empty(log.UnnamedProcesses());
        Assert.Equal(TimeSpan.FromMilliseconds(20), log.LastClickAt);
    }

    private sealed class FakeWatcher : IClickWatcher
    {
        public bool Throw { get; init; }
        public int Starts, Stops;
        public Func<TimeSpan>? Now;
        public void Start(Func<TimeSpan> now)
        {
            Starts++;
            Now = now;
            if (Throw) throw new InvalidOperationException("tap refused");
        }
        public string? Stop() { Stops++; return "Mouse and click events (what macOS saw)\nClicks: 0\n"; }
    }

    private sealed class PlainDaemon : IDaemonDebugSession
    {
#pragma warning disable CS0067
        public event Action<JObject>? DeviceReport;
#pragma warning restore CS0067
        public Task SetTabletDebugAsync(bool enabled) => Task.CompletedTask;
    }

    [Fact]
    public async Task TheRecording_StartsAndStopsTheWatcher_AndAddsItsSection()
    {
        var w = new FakeWatcher();
        var r = new ProblemRecorder(new PlainDaemon(), clicks: w);
        await r.StartAsync();
        Assert.Equal(1, w.Starts);
        Assert.True(w.Now!() >= TimeSpan.Zero);
        var trace = await r.StopAsync();
        Assert.Equal(1, w.Stops);
        Assert.Contains("Mouse and click events (what macOS saw)", trace);
    }

    [Fact]
    public async Task AWatcherThatFails_IsReported_AndTheRecordingStillWorks()
    {
        var r = new ProblemRecorder(new PlainDaemon(), clicks: new FakeWatcher { Throw = true });
        await r.StartAsync();
        var trace = await r.StopAsync();
        Assert.Contains("PROBLEM: Couldn't watch the mouse and click events: tap refused", trace);
        Assert.Contains("Record the problem", trace);
    }
}
