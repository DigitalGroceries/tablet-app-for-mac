using System;
using System.Diagnostics;
using System.Threading;
using OpenTabletDriver.Desktop.Interop.Input;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>V28 (#85, mtr8080): the worker that lets a pen click go after at most 50 ms while macOS activates an app.</summary>
public class SingleSlotWorkerTests
{
    [Fact]
    public void A_quick_job_is_waited_for()
    {
        var worker = new SingleSlotWorker("test");
        var ran = false;
        Assert.Equal(SingleSlotWorker.Outcome.Finished, worker.Run(() => ran = true, 5000));
        Assert.True(ran);
        Assert.False(worker.IsBusy);
    }

    [Fact]
    public void A_slow_job_returns_after_the_wait()
    {
        var worker = new SingleSlotWorker("test");
        using var release = new ManualResetEventSlim(false);
        var clock = Stopwatch.StartNew();
        var outcome = worker.Run(() => release.Wait(10000), 50);
        clock.Stop();
        release.Set();
        Assert.Equal(SingleSlotWorker.Outcome.StillRunning, outcome);
        Assert.InRange(clock.ElapsedMilliseconds, 40, 2000);
    }

    [Fact]
    public void A_request_while_busy_is_dropped()
    {
        var worker = new SingleSlotWorker("test");
        using var release = new ManualResetEventSlim(false);
        var secondRan = false;
        worker.Run(() => release.Wait(10000), 0);
        Assert.True(worker.IsBusy);
        Assert.Equal(SingleSlotWorker.Outcome.Dropped, worker.Run(() => secondRan = true, 50));
        release.Set();
        Assert.True(SpinWait.SpinUntil(() => !worker.IsBusy, 5000));
        Assert.False(secondRan);
        Assert.Equal(SingleSlotWorker.Outcome.Finished, worker.Run(() => { }, 5000));
    }

    [Fact]
    public void An_exception_in_a_job_does_not_stop_the_worker()
    {
        Exception? reported = null;
        var worker = new SingleSlotWorker("test", ex => reported = ex);
        Assert.Equal(SingleSlotWorker.Outcome.Finished, worker.Run(() => throw new InvalidOperationException("boom"), 5000));
        Assert.Equal("boom", reported?.Message);
        var ran = false;
        Assert.Equal(SingleSlotWorker.Outcome.Finished, worker.Run(() => ran = true, 5000));
        Assert.True(ran);
    }
}
