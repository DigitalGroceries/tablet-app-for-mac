using System;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

public class UpdaterTests
{
    [Theory]
    [InlineData("v7", 7)] [InlineData("V12", 12)] [InlineData("8", 8)] [InlineData("beta", 0)] [InlineData(null, 0)]
    public void ParsesTags(string? tag, int n) => Assert.Equal(n, Updater.ParseVersion(tag));

    [Fact]
    public void ChecksWeekly()
    {
        var t = new DateTime(2026, 10, 1);
        Assert.False(Updater.IsDue(t, t.AddDays(6)));
        Assert.True(Updater.IsDue(t, t.AddDays(7)));
    }

    [Fact]
    public void ReadsLatestRelease()
    {
        var json = "{\"tag_name\":\"v8\",\"draft\":false,\"assets\":[{\"name\":\"" + Updater.ZipName + "\",\"browser_download_url\":\"https://x/z\"},{\"name\":\"SHA256SUMS\",\"browser_download_url\":\"https://x/s\"}]}";
        var r = Updater.Parse(json)!;
        Assert.Equal(8, r.Version); Assert.Equal("https://x/z", r.ZipUrl); Assert.Equal("https://x/s", r.SumsUrl);
        Assert.Null(Updater.Parse("""{"tag_name":"v8","assets":[]}"""));
    }

    [Fact]
    public void FindsHashInSums()
        => Assert.Equal("abc123", Updater.HashFor("ABC123  Tablet-App-for-Mac.zip\nfff  other.dmg\n", "Tablet-App-for-Mac.zip"));
}
