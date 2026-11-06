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
        var key = "T" + Guid.NewGuid().ToString("N")[..8];   // short: macOS caps socket paths at 104 bytes
        using var first = new SingleInstance(key);
        Assert.True(first.TryAcquire());
        var got = new BlockingCollection<string>();
        using var activated = new ManualResetEventSlim(false);
        first.ListenForActivation(() => activated.Set(), p => got.Add(p));

        // The primary passes on only real files (review of #58), so the test uses one.
        var file = System.IO.Path.Combine(System.IO.Directory.CreateTempSubdirectory().FullName, "My backup.tabletsettings");
        System.IO.File.WriteAllText(file, "{}");
        using var second = new SingleInstance(key);
        Assert.False(second.TryAcquire(new[] { file }));

        Assert.True(activated.Wait(TimeSpan.FromSeconds(30)));
        Assert.True(got.TryTake(out var path, TimeSpan.FromSeconds(30)));
        Assert.Equal(file, path);
    }

    [Fact]
    public void SecondCopy_ForwardsSeveralFiles_WithSpaces_InOrder()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        var key = "T" + Guid.NewGuid().ToString("N")[..8];   // short: macOS caps socket paths at 104 bytes
        using var first = new SingleInstance(key);
        Assert.True(first.TryAcquire());
        var got = new BlockingCollection<string>();
        first.ListenForActivation(() => { }, p => got.Add(p));
        var dir = System.IO.Directory.CreateTempSubdirectory().FullName;
        var files = new[] { System.IO.Path.Combine(dir, "My backup.tabletsettings"), System.IO.Path.Combine(dir, "Ünï two.tabletsettings") };
        foreach (var f in files) System.IO.File.WriteAllText(f, "{}");
        using var second = new SingleInstance(key);
        Assert.False(second.TryAcquire(files));
        foreach (var f in files)
        {
            Assert.True(got.TryTake(out var path, TimeSpan.FromSeconds(30)));
            Assert.Equal(f, path);
        }
    }

    [Fact]
    public void PathlessNudge_StillJustSurfaces()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        var key = "T" + Guid.NewGuid().ToString("N")[..8];   // short: macOS caps socket paths at 104 bytes
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

/// <summary>Review of #58: the primary passes on only full paths to existing .tabletsettings files.</summary>
public class SettingsFileHandoffHardeningTests
{
    [Xunit.Fact]
    public void Only_full_paths_to_existing_settings_files_are_accepted()
    {
        var dir = System.IO.Directory.CreateTempSubdirectory().FullName;
        var good = System.IO.Path.Combine(dir, "a." + OpenTabletArtist.Services.SettingsBundle.Extension);
        System.IO.File.WriteAllText(good, "{}");
        var accepted = OpenTabletArtist.Services.SingleInstance.AcceptedPaths(new[]
        {
            good,
            System.IO.Path.Combine(dir, "missing." + OpenTabletArtist.Services.SettingsBundle.Extension),
            System.IO.Path.Combine(dir, "evil.sh"),
            "relative." + OpenTabletArtist.Services.SettingsBundle.Extension,
        });
        Xunit.Assert.Equal(new[] { good }, accepted);
    }
}
