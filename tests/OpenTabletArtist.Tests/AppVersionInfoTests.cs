using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.Tests;

public class AppVersionInfoTests
{
    [Theory]
    [InlineData("1", "Version 1")]
    [InlineData("0.6.0", "Version 0.6.0")]
    [InlineData("v0.6.0", "Version 0.6.0")]
    [InlineData("0.6.0+abc1234", "Version 0.6.0")]      // strip +build metadata
    [InlineData("1.2.3-beta+deadbeef", "Version 1.2.3-beta")]
    [InlineData("  1.2.3  ", "Version 1.2.3")]
    public void Format_NormalizesToVPrefixedNoMetadata(string input, string expected)
        => Assert.Equal(expected, AppVersionInfo.Format(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("+onlymetadata")]
    public void Format_MissingOrEmpty_ReturnsPlaceholder(string? input)
        => Assert.Equal("Version ?", AppVersionInfo.Format(input));

    // Zak, 4 and 12 Nov 2026: a version name is one number, or one number and one word. A test build is the number
    // followed by "test", never a build number; a finished version is just the number.
    [Theory]
    [InlineData("26-test", "26 test", "Version 26 test")]
    [InlineData("26-test+abc1234", "26 test", "Version 26 test")]
    [InlineData("26-test8", "26 test", "Version 26 test")]
    [InlineData("26", "26", "Version 26")]
    [InlineData("26-beta", "26 BETA", "Version 26 BETA")]
    public void TestBuilds_AreTheNumberAndTheWordTest(string info, string number, string format)
    {
        Assert.Equal(number, AppVersionInfo.Number(info));
        Assert.Equal(format, AppVersionInfo.Format(info));
    }

    [Fact]
    public void ATestBuild_IsStillVersion26_ForTheUpdater() =>
        Assert.Equal(26, OpenTabletArtist.Services.Updater.ParseVersion("26-test8"));
}
