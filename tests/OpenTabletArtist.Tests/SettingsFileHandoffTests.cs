using System;
using System.Collections.Concurrent;
using System.Threading;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Item 26 (#28): a .tabletsettings file given to a second app copy reaches the running one.</summary>
public class SettingsFileHandoffTests
{
    [Fact]
    public void SecondCopy_ForwardsSettingsFile_ToPrimary()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        var key = "Test-" + Guid.NewGuid().ToString("N");
        using var first = new SingleInstance(key);
        Assert.True(first.TryAcquire());
        var got = new BlockingCollection<string>();
        using var activated = new ManualResetEventSlim(false);
        first.ListenForActivation(() => activated.Set(), p => got.Add(p));

        using var second = new SingleInstance(key);
        Assert.False(second.TryAcquire(new[] { "/Users/a/My backup.tabletsettings" }));

        Assert.True(activated.Wait(TimeSpan.FromSeconds(30)));
        Assert.True(got.TryTake(out var path, TimeSpan.FromSeconds(30)));
        Assert.Equal("/Users/a/My backup.tabletsettings", path);
    }

    [Fact]
    public void PathlessNudge_StillJustSurfaces()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        var key = "Test-" + Guid.NewGuid().ToString("N");
        using var first = new SingleInstance(key);
        Assert.True(first.TryAcquire());
        var got = new BlockingCollection<string>();
        using var activated = new ManualResetEventSlim(false);
        first.ListenForActivation(() => activated.Set(), p => got.Add(p));
        using var second = new SingleInstance(key);
        Assert.False(second.TryAcquire());
        Assert.True(activated.Wait(TimeSpan.FromSeconds(30)));
        Assert.False(got.TryTake(out _, TimeSpan.FromMilliseconds(1500)));
    }

    [Fact]
    public void Paths_RoundTrip_UnicodeAndSpaces()
    {
        var paths = new[] { "/tmp/Ünïcode file.tabletsettings", "/tmp/b.tabletsettings" };
        var bytes = SingleInstance.EncodePaths(paths);
        Assert.Equal(paths, SingleInstance.DecodePaths(bytes, bytes.Length));
    }

    [Fact]
    public void Args_PickOnlyExistingSettingsFiles()
    {
        var r = SettingsFileArgs.From(new[] { "--background", "/x/a.tabletsettings", "/x/gone.tabletsettings", "/x/b.txt" },
            p => p != "/x/gone.tabletsettings");
        Assert.Equal(new[] { "/x/a.tabletsettings" }, r);
    }
}
