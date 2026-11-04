using System;
using System.Diagnostics;
using System.Threading.Tasks;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Item 5 / #15: Reset permissions must not freeze Home, and a failed call must not read as success.</summary>
public class PermissionResetAsyncTests
{
    [Fact]
    public async Task AllCallsFinish_IsSuccess_EvenWithNonZeroExit()
    {
        int calls = 0;
        var ok = await PermissionReset.RunAsync((_, _) => { calls++; return Task.FromResult<(int, string)?>((1, "No such record")); });
        Assert.True(ok);
        Assert.Equal(4, calls);
    }

    [Fact]
    public async Task TimeoutOrStartFailure_IsReportedAsFailure()
    {
        int n = 0;
        var ok = await PermissionReset.RunAsync((_, _) => Task.FromResult<(int, string)?>(n++ == 1 ? null : (0, "")));
        Assert.False(ok);
    }

    [Fact]
    public async Task SlowTccutil_DoesNotBlockTheCaller()
    {
        var gate = new TaskCompletionSource();
        var sw = Stopwatch.StartNew();
        var run = PermissionReset.RunAsync(async (_, _) => { await gate.Task; return (0, ""); });
        Assert.True(sw.ElapsedMilliseconds < 1000);   // returned to the caller (the UI thread) at once
        Assert.False(run.IsCompleted);
        gate.SetResult();
        Assert.True(await run);
    }

    [Fact]
    public void TimeoutIsShorterThanBefore() => Assert.True(PermissionReset.CallTimeout < TimeSpan.FromSeconds(10));
}
