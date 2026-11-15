using Newtonsoft.Json.Linq;
using OpenTabletArtist.Services;
using OtdInterop;
using Xunit;

namespace OpenTabletArtist.Tests;

public class ProblemRecorderTests
{
    private sealed class Daemon : IDaemonDebugSession, IDaemonLogWriter
    {
        public event Action<JObject>? DeviceReport;
        public List<bool> Debug { get; } = new();
        public List<string> Log { get; } = new();
        public bool FailDebug { get; init; }
        public bool RefuseLog { get; init; }
        public int Listeners => DeviceReport?.GetInvocationList().Length ?? 0;
        public Task SetTabletDebugAsync(bool enabled)
        {
            Debug.Add(enabled);
            return FailDebug && enabled ? Task.FromException(new InvalidOperationException("pipe closed")) : Task.CompletedTask;
        }
        public Task<bool> WriteLogAsync(string group, string message)
        {
            Log.Add(group + ": " + message);
            return Task.FromResult(!RefuseLog);
        }
        public void Emit(JObject report) => DeviceReport?.Invoke(report);
    }

    private sealed class PlainDaemon : IDaemonDebugSession
    {
#pragma warning disable CS0067
        public event Action<JObject>? DeviceReport;
#pragma warning restore CS0067
        public Task SetTabletDebugAsync(bool enabled) => Task.CompletedTask;
    }

    private sealed class Clock
    {
        public DateTime Now = new(2026, 11, 15, 21, 40, 0, 0);
        public DateTime Get() => Now;
    }

    private static JObject Report(string path, string raw = "AQID", double x = 100, double pressure = 512) => new()
    {
        ["Path"] = path,
        ["Tablet"] = new JObject { ["Properties"] = new JObject { ["Name"] = "Wacom PTK-440" } },
        ["Data"] = new JObject { ["Raw"] = raw, ["Position"] = new JObject { ["X"] = x, ["Y"] = 50 }, ["Pressure"] = pressure },
    };

    [Fact]
    public async Task StartAndStop_TurnTheStreamOnAndOff_MarkTheDriverLog_AndLetGo()
    {
        var d = new Daemon();
        var r = new ProblemRecorder(d);
        await r.StartAsync();
        Assert.True(r.IsRecording);
        Assert.Equal(1, d.Listeners);
        var trace = await r.StopAsync();
        Assert.False(r.IsRecording);
        Assert.Equal(0, d.Listeners);
        Assert.Equal([true, false], d.Debug);
        Assert.Equal(2, d.Log.Count);
        Assert.Contains("recording started", d.Log[0]);
        Assert.Contains("recording stopped", d.Log[1]);
        Assert.StartsWith(ProblemRecorder.LogGroup + ":", d.Log[0]);
        Assert.Equal(trace, r.Trace);
    }

    [Fact]
    public async Task Reports_AreKeptWithTimeTypeTabletAndValues()
    {
        var clock = new Clock();
        var d = new Daemon();
        var r = new ProblemRecorder(d, clock.Get);
        await r.StartAsync();
        clock.Now = clock.Now.AddMilliseconds(1250);
        d.Emit(Report("OpenTabletDriver.Tablet.Wacom.IntuosV2.IntuosV2TabletReport"));
        clock.Now = clock.Now.AddMilliseconds(500);
        d.Emit(Report("OpenTabletDriver.Tablet.Wacom.IntuosV2.IntuosV2AuxReport"));
        d.Emit(Report("OpenTabletDriver.Tablet.Wacom.IntuosV2.IntuosV2AuxReport"));
        Assert.Equal(3, r.EventCount);
        clock.Now = clock.Now.AddSeconds(2);
        var trace = await r.StopAsync();

        Assert.Contains("+   1.250 s  IntuosV2TabletReport  [Wacom PTK-440]", trace);
        Assert.Contains("\"RawHex\":\"010203\"", trace);
        Assert.DoesNotContain("\"Raw\":", trace);
        Assert.Contains("\"Pressure\":512", trace);
        Assert.Contains("Tablet reports kept: 3", trace);
        Assert.Contains("By type: IntuosV2AuxReport 2, IntuosV2TabletReport 1", trace);
        Assert.Contains("(3.8 s)", trace);
        Assert.DoesNotContain("PROBLEM", trace);
    }

    [Fact]
    public async Task ReportsBeforeStartOrAfterStop_AreNotKept()
    {
        var d = new Daemon();
        var r = new ProblemRecorder(d);
        r.OnReport(Report("X.PenReport"));
        await r.StartAsync();
        await r.StopAsync();
        d.Emit(Report("X.PenReport"));
        r.OnReport(Report("X.PenReport"));
        Assert.Equal(0, r.EventCount);
    }

    [Fact]
    public async Task TheRecording_IsBounded_AndSaysHowManyWereNotKept()
    {
        var d = new Daemon();
        var r = new ProblemRecorder(d);
        await r.StartAsync();
        for (var i = 0; i < ProblemRecorder.MaxEvents + 7; i++) d.Emit(Report("X.PenReport"));
        var trace = await r.StopAsync();
        Assert.Equal(ProblemRecorder.MaxEvents, r.EventCount);
        Assert.Contains($"Tablet reports not kept: 7 (over the {ProblemRecorder.MaxEvents} report limit)", trace);
    }

    [Fact]
    public async Task ReportsAfterTheTimeLimit_AreCountedNotKept()
    {
        var clock = new Clock();
        var d = new Daemon();
        var r = new ProblemRecorder(d, clock.Get);
        await r.StartAsync();
        d.Emit(Report("X.PenReport"));
        clock.Now = clock.Now + ProblemRecorder.MaxLength + TimeSpan.FromSeconds(1);
        d.Emit(Report("X.PenReport"));
        var trace = await r.StopAsync();
        Assert.Equal(1, r.EventCount);
        Assert.Contains("Tablet reports not kept: 1 (after the 2 minute limit)", trace);
    }

    [Fact]
    public async Task NothingFromTheTablet_IsSaidPlainly()
    {
        var r = new ProblemRecorder(new Daemon());
        await r.StartAsync();
        var trace = await r.StopAsync();
        Assert.Contains("Tablet reports kept: 0", trace);
        Assert.Contains("The tablet sent nothing while recording", trace);
    }

    [Fact]
    public async Task NoDriver_IsAProblemInTheRecording_NotAnError()
    {
        var r = new ProblemRecorder(null);
        await r.StartAsync();
        var trace = await r.StopAsync();
        Assert.Contains("PROBLEM: The driver is not connected", trace);
        Assert.Contains("No tablet reports were recorded.", trace);
    }

    [Fact]
    public async Task AStreamThatWontStart_IsReported_AndNotTurnedOffAgain()
    {
        var d = new Daemon { FailDebug = true };
        var r = new ProblemRecorder(d);
        await r.StartAsync();
        var trace = await r.StopAsync();
        Assert.Contains("PROBLEM: Couldn't turn on the driver's tablet report stream: pipe closed", trace);
        Assert.Equal([true], d.Debug);
        Assert.Equal(0, d.Listeners);
    }

    [Fact]
    public async Task DriverLogMarks_ThatFail_AreReported()
    {
        var refused = new ProblemRecorder(new Daemon { RefuseLog = true });
        await refused.StartAsync();
        var t1 = await refused.StopAsync();
        Assert.Contains("PROBLEM: Couldn't write the started mark into the driver log.", t1);
        Assert.Contains("PROBLEM: Couldn't write the stopped mark into the driver log.", t1);

        var plain = new ProblemRecorder(new PlainDaemon());
        await plain.StartAsync();
        var t2 = await plain.StopAsync();
        Assert.Contains("PROBLEM: The driver log has no started mark", t2);
    }

    [Fact]
    public async Task ASecondStart_KeepsOneSubscription_AndStopWhenStopped_IsHarmless()
    {
        var d = new Daemon();
        var r = new ProblemRecorder(d);
        Assert.Equal("", await r.StopAsync());
        await r.StartAsync();
        await r.StartAsync();
        Assert.Equal(1, d.Listeners);
        Assert.Equal([true], d.Debug);
        await r.StopAsync();
        await r.StopAsync();
        Assert.Equal([true, false], d.Debug);
    }

    [Fact]
    public async Task ANewRecording_StartsClean()
    {
        var d = new Daemon();
        var r = new ProblemRecorder(d);
        await r.StartAsync();
        d.Emit(Report("X.PenReport"));
        await r.StopAsync();
        await r.StartAsync();
        Assert.Null(r.Trace);
        Assert.Equal(0, r.EventCount);
        await r.StopAsync();
    }

    [Fact]
    public void FormatReport_RemovesSerials_CutsLongReports_AndCopesWithOddData()
    {
        var serial = new JObject { ["Path"] = "X.R", ["Data"] = new JObject { ["Serial"] = "8HA00Q1234567" } };
        var line = ProblemRecorder.FormatReport(TimeSpan.Zero, "R", serial);
        Assert.DoesNotContain("8HA00Q1234567", line);

        var big = new JObject { ["Data"] = new JObject { ["Text"] = new string('a', 2000) } };
        var cut = ProblemRecorder.FormatReport(TimeSpan.Zero, "R", big);
        Assert.EndsWith("…", cut);
        Assert.True(cut.Length < ProblemRecorder.MaxDataChars + 80);

        var bad = ProblemRecorder.FormatReport(TimeSpan.Zero, "R", new JObject { ["Data"] = new JObject { ["Raw"] = "%%%" } });
        Assert.Contains("(unreadable)", bad);
        Assert.Contains("[?]", bad);
        Assert.Contains("(no data)", ProblemRecorder.FormatReport(TimeSpan.Zero, "R", new JObject()));
    }

    [Theory]
    [InlineData("OpenTabletDriver.Tablet.Wacom.Intuos4.Intuos4TabletReport", "Intuos4TabletReport")]
    [InlineData("Plain", "Plain")]
    [InlineData("", "(no type)")]
    public void Kind_IsTheLastPartOfThePath(string path, string kind) =>
        Assert.Equal(kind, ProblemRecorder.Kind(new JObject { ["Path"] = path }));

    [Fact]
    public async Task Changed_FiresOnStartFirstReportAndStop()
    {
        var d = new Daemon();
        var r = new ProblemRecorder(d);
        var n = 0;
        r.Changed += () => n++;
        await r.StartAsync();
        d.Emit(Report("X.PenReport"));
        d.Emit(Report("X.PenReport"));
        await r.StopAsync();
        Assert.Equal(3, n);
    }

    [Fact]
    public void FormatReport_LeavesOutTheRawBytes_OfAReportThatHoldsASerial()
    {
        var raw = Convert.ToBase64String(new byte[] { 0xC2, 0x80, 0x12, 0x34, 0x56, 0x78 });
        var tool = new JObject { ["Data"] = new JObject { ["Raw"] = raw, ["Serial"] = 305419896, ["RawToolID"] = 2050 } };
        var line = ProblemRecorder.FormatReport(TimeSpan.Zero, "IntuosV2ToolReport", tool);
        Assert.DoesNotContain("12345678", line);
        Assert.DoesNotContain("305419896", line);
        Assert.Contains("left out", line);
    }

    [Fact]
    public async Task AReportAfterTheTimeLimit_StopsTheRecordingByItself()
    {
        var t = new DateTime(2026, 11, 15, 12, 0, 0);
        var daemon = new LimitDaemon();
        var rec = new ProblemRecorder(daemon, () => t);
        await rec.StartAsync();
        daemon.Emit();
        t += ProblemRecorder.MaxLength + TimeSpan.FromSeconds(1);
        daemon.Emit();
        for (var i = 0; i < 50 && rec.IsRecording; i++) await Task.Delay(20);
        Assert.False(rec.IsRecording);
        Assert.True(rec.StoppedAtLimit);
        Assert.Equal(1, rec.EventCount);
        Assert.Equal(new[] { true, false }, daemon.Debug);
        Assert.Contains("after the 2 minute limit", rec.Trace);
        await rec.StartAsync();
        Assert.False(rec.StoppedAtLimit);
        await rec.StopAsync();
    }

    private sealed class LimitDaemon : OtdInterop.IDaemonDebugSession
    {
        public event Action<JObject>? DeviceReport;
        public List<bool> Debug { get; } = new();
        public Task SetTabletDebugAsync(bool enabled) { Debug.Add(enabled); return Task.CompletedTask; }
        public void Emit() => DeviceReport?.Invoke(new JObject { ["Path"] = "X.PenReport", ["Data"] = new JObject { ["Pressure"] = 1 } });
    }

    [Fact]
    public async Task ASecondStop_WhileTheFirstIsStillRunning_WaitsForTheRecording()
    {
        var daemon = new SlowStopDaemon();
        var rec = new ProblemRecorder(daemon);
        await rec.StartAsync();
        var first = rec.StopAsync();
        var second = rec.StopAsync();
        Assert.False(second.IsCompleted);
        daemon.Release.SetResult();
        Assert.Equal(await first, await second);
        Assert.NotNull(rec.Trace);
    }

    private sealed class SlowStopDaemon : OtdInterop.IDaemonDebugSession
    {
        public event Action<JObject>? DeviceReport { add { } remove { } }
        public TaskCompletionSource Release { get; } = new();
        public Task SetTabletDebugAsync(bool enabled) => enabled ? Task.CompletedTask : Release.Task;
    }
}
