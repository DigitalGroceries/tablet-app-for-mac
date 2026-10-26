using System.Linq;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Version 14 (Zak, 26 Oct 2026): "The app should show the choice between stable and beta versions for
/// download." The update check reads all releases and offers a newer stable version, a newer BETA (a GitHub
/// pre-release, not tested yet), and — for someone running a BETA — going back to the stable version.</summary>
public class BuildV14UpdateChoiceTests
{
    static Updater.Release S(int v) => new(v, $"z{v}", $"s{v}");
    static Updater.Release B(int v, int b) => new(v, $"z{v}b{b}", $"s{v}b{b}", b);

    [Theory]
    [InlineData("v14-beta1", 1)] [InlineData("v14-beta", 1)] [InlineData("v14-BETA3", 3)]
    [InlineData("14-beta12+abcdef", 12)] [InlineData("v14", 0)] [InlineData("14+abc123", 0)]
    [InlineData("v7-test", 0)] [InlineData(null, 0)]
    public void ParsesBetaNumber(string? tag, int beta) => Assert.Equal(beta, Updater.ParseBeta(tag));

    [Fact]
    public void OrdersBetasBeforeTheirStableVersion()
    {
        Assert.True(Updater.RankOf(14, 1) < Updater.RankOf(14, 2));
        Assert.True(Updater.RankOf(14, 2) < Updater.RankOf(14, 0));
        Assert.True(Updater.RankOf(14, 0) < Updater.RankOf(15, 1));
        Assert.True(Updater.RankOf(13, 0) < Updater.RankOf(14, 1));
    }

    [Fact]
    public void LabelsBetas()
    {
        Assert.Equal("14 BETA 2", B(14, 2).Label);
        Assert.Equal("13", S(13).Label);
        Assert.True(B(14, 1).IsBeta);
        Assert.False(S(13).IsBeta);
    }

    [Fact]
    public void ReadsStableAndPreReleasesSkippingDraftsAndIncomplete()
    {
        var zip = Updater.ZipName;
        string Rel(string tag, bool pre, bool draft, bool full) =>
            $$"""{"tag_name":"{{tag}}","prerelease":{{(pre ? "true" : "false")}},"draft":{{(draft ? "true" : "false")}},"assets":[{"name":"{{zip}}","browser_download_url":"https://x/{{tag}}/z"}{{(full ? $$""",{"name":"SHA256SUMS","browser_download_url":"https://x/{{tag}}/s"}""" : "")}}]}""";
        var json = "[" + string.Join(",",
            Rel("v14-beta2", true, false, true),
            Rel("v14-beta1", true, false, true),
            Rel("v13", false, false, true),
            Rel("v15", false, true, true),        // draft: skipped
            Rel("v12", false, false, false),      // no SHA256SUMS: skipped
            Rel("v7-test", true, false, true)) + "]";
        var all = Updater.ParseAll(json);
        Assert.Equal(new[] { "14 BETA 2", "14 BETA 1", "13", "7 BETA 1" }, all.Select(r => r.Label).ToArray());
        Assert.Equal("https://x/v13/z", all.Single(r => r.Label == "13").ZipUrl);
        Assert.Equal("https://x/v14-beta2/s", all.Single(r => r.Label == "14 BETA 2").SumsUrl);
        Assert.Empty(Updater.ParseAll("{}"));
    }

    [Fact]
    public void OnStable_OffersNewerBetaToTry()
    {
        var c = Updater.Choose(new[] { S(13), B(14, 1), S(12) }, 13, 0);
        Assert.Null(c.StableUpgrade); Assert.Null(c.GoBack);
        Assert.Equal("14 BETA 1", c.Beta?.Label);
        Assert.True(c.Any);
    }

    [Fact]
    public void OnStable_OffersNewerStableAndNewerBeta()
    {
        var c = Updater.Choose(new[] { S(14), B(15, 1), S(13) }, 13, 0);
        Assert.Equal("14", c.StableUpgrade?.Label);
        Assert.Equal("15 BETA 1", c.Beta?.Label);
    }

    [Fact]
    public void BetaOlderThanNewestStable_IsNotOffered()
    {
        var c = Updater.Choose(new[] { S(14), B(14, 1), S(13) }, 13, 0);
        Assert.Equal("14", c.StableUpgrade?.Label);
        Assert.Null(c.Beta);
    }

    [Fact]
    public void OnBeta_OffersNewerBetaAndGoingBackToStable()
    {
        var c = Updater.Choose(new[] { B(14, 2), B(14, 1), S(13) }, 14, 1);
        Assert.Null(c.StableUpgrade);
        Assert.Equal("13", c.GoBack?.Label);
        Assert.Equal("14 BETA 2", c.Beta?.Label);
    }

    [Fact]
    public void OnNewestBeta_OffersOnlyGoingBack()
    {
        var c = Updater.Choose(new[] { B(14, 2), S(13) }, 14, 2);
        Assert.Equal("13", c.GoBack?.Label);
        Assert.Null(c.Beta); Assert.Null(c.StableUpgrade);
    }

    [Fact]
    public void OnBeta_StableOfSameVersionIsAnUpgrade()
    {
        var c = Updater.Choose(new[] { S(14), B(14, 2), S(13) }, 14, 2);
        Assert.Equal("14", c.StableUpgrade?.Label);
        Assert.Null(c.GoBack); Assert.Null(c.Beta);
    }

    [Fact]
    public void OnNewestStable_OffersNothing()
    {
        var c = Updater.Choose(new[] { S(14), B(14, 1), S(13) }, 14, 0);
        Assert.False(c.Any);
        Assert.False(Updater.Choose(System.Array.Empty<Updater.Release>(), 14, 1).Any);
    }

    [Theory]
    [InlineData("14-beta2", "Version 14 BETA 2")]
    [InlineData("14-beta2+abc1234", "Version 14 BETA 2")]
    [InlineData("14", "Version 14")]
    [InlineData("1.2.3-beta+deadbeef", "Version 1.2.3-beta")]
    public void VersionLabelShowsBeta(string input, string expected) => Assert.Equal(expected, AppVersionInfo.Format(input));
}
