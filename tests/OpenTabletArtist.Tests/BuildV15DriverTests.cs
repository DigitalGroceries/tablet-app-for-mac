using System;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Version 15 (Zak, 27 Oct 2026): a leftover driver from an older version is stopped at start; an
/// OpenTabletDriver the artist installed is never touched.</summary>
public class BuildV15DriverTests
{
    const string Mine = "/Applications/Tablet App for Mac.app/Contents/MacOS/Daemon/arm64/OpenTabletDriver.Daemon";
    static readonly DateTime Written = new(2026, 10, 27, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void OldAppMovedAsideByTheUpdater_IsStale()
        => Assert.True(StaleDriverGuard.IsStale("/private/var/folders/x/T/tablet-app-update-15/old.app/Contents/MacOS/Daemon/arm64/OpenTabletDriver.Daemon",
            Written.AddHours(-3), Mine, Written));

    [Fact]
    public void SamePathStartedBeforeTheFileWasReplaced_IsStale()
        => Assert.True(StaleDriverGuard.IsStale(Mine, Written.AddMinutes(-10), Mine, Written));

    [Fact]
    public void SamePathStartedAfterInstall_IsKept()
        => Assert.False(StaleDriverGuard.IsStale(Mine, Written.AddMinutes(5), Mine, Written));

    [Theory]
    [InlineData("/Applications/OpenTabletDriver.app/Contents/MacOS/OpenTabletDriver.Daemon")]
    [InlineData("/usr/local/bin/OpenTabletDriver.Daemon")]
    [InlineData(null)]
    public void AnOpenTabletDriverTheArtistInstalled_IsNeverTouched(string? path)
        => Assert.False(StaleDriverGuard.IsStale(path, Written.AddHours(-3), Mine, Written));
}
