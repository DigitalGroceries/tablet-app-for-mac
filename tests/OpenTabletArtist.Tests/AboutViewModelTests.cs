using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.Tests;

public class AboutViewModelTests
{
    // R18: the About links come from Services.AppLinks; until the user's own pages exist they are null, so no
    // button sends anyone to the upstream OpenTabletArtist / OpenTabletDriver pages.
    [Fact]
    public void Links_ComeFromAppLinks_AndPointNowhereUpstream()
    {
        var vm = new AboutViewModel();
        Assert.Equal(OpenTabletArtist.Services.AppLinks.SourceCode, vm.RepoUrl);
        Assert.Equal(OpenTabletArtist.Services.AppLinks.Releases, vm.ReleasesUrl);
        Assert.Equal(OpenTabletArtist.Services.AppLinks.GetHelp, vm.HelpUrl);
        foreach (var u in new[] { vm.RepoUrl, vm.ReleasesUrl, vm.HelpUrl, vm.CreditsUrl })
            Assert.True(u is null || !u.Contains("TheSevenPens") && !u.Contains("OpenTabletDriver/OpenTabletDriver"));
    }

    [Fact]
    public void GetHelp_WithoutAUrl_OpensTheInAppManual()
    {
        var vm = new AboutViewModel();
        bool opened = false;
        vm.ManualRequested += () => opened = true;
        vm.OpenHelpCommand.Execute(null);
        Assert.Equal(vm.HelpUrl is null, opened);
    }
}
