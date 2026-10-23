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
}
