using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

public class SystemQuitCoordinatorTests
{
    private static void Wait(Func<Task> work, TimeSpan limit) => Task.Run(work).Wait(limit);

    [Fact]
    public void NeverCancels_AllowsCloseFirst_SavesClosesAndStopsOwnDaemon()
    {
        var log = new List<string>();
        var q = new SystemQuitCoordinator(
            () => log.Add("allow"),
            () => { log.Add("save"); return Task.FromResult(true); },
            _ => { log.Add("close"); return Task.FromResult(true); },
            () => () => { log.Add("stop"); return Task.CompletedTask; },
            Wait, _ => { });
        Assert.False(q.OnShutdownRequested());
        Assert.Equal(new[] { "allow", "save", "close", "stop" }, log);
    }

    [Fact]
    public void HangingSave_StillExitsWithinBudget_AndNeverCancels()
    {
        var warnings = new List<string>();
        var stopped = false;
        var q = new SystemQuitCoordinator(() => { },
            () => new TaskCompletionSource<bool>().Task,
            _ => Task.FromResult(true),
            () => () => { stopped = true; return Task.CompletedTask; },
            Wait, warnings.Add);
        var sw = Stopwatch.StartNew();
        Assert.False(q.OnShutdownRequested());
        Assert.True(sw.Elapsed < SystemQuitCoordinator.TotalBudget);
        Assert.True(stopped);
        Assert.Contains(warnings, w => w.Contains("snapshot did not finish"));
    }

    [Fact]
    public void ThrowingSteps_NeverCancel_AndSecondRequestIsNoOp()
    {
        var allows = 0;
        var q = new SystemQuitCoordinator(() => allows++,
            () => throw new InvalidOperationException("boom"),
            _ => throw new InvalidOperationException("boom"),
            () => throw new InvalidOperationException("boom"),
            Wait, _ => { });
        Assert.False(q.OnShutdownRequested());
        Assert.False(q.OnShutdownRequested());
        Assert.Equal(1, allows);
        Assert.True(q.HasRun);
    }

    [Fact]
    public void ForeignDaemon_NoStopStep_StillQuits()
    {
        var q = new SystemQuitCoordinator(() => { }, () => Task.FromResult(true),
            _ => Task.FromResult(true), () => null, Wait, _ => { });
        Assert.False(q.OnShutdownRequested());
    }
}
