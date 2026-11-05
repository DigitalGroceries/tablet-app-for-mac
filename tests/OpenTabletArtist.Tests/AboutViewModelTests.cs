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
    public void GetHelp_OpensGitHubsFeedbackChooser_NotTheManual()
    {
        var vm = new AboutViewModel();
        Assert.Equal("https://github.com/DigitalGroceries/tablet-app-for-mac/issues/new/choose", vm.HelpUrl);
        Assert.NotEqual(OpenTabletArtist.Services.AppLinks.Manual, vm.HelpUrl);
    }

    [Fact]
    public void UserManual_AlwaysOpensTheOfflineManual()
    {
        var vm = new AboutViewModel();
        bool opened = false;
        vm.ManualRequested += () => opened = true;
        vm.OpenHelpCommand.Execute(null);
        Assert.True(opened);
    }

    [Fact]
    public void GoBack_uses_a_short_label_and_the_full_name_as_the_tooltip()
    {
        var state = OpenTabletArtist.Services.AppUpdateState.Shared;
        try
        {
            state.SetChoicesForTest(new OpenTabletArtist.Services.Updater.UpdateChoices(
                Stable: new OpenTabletArtist.Services.Updater.Release(21, "z", "s"),
                Beta: null,
                StableIsGoBack: true));
            var vm = new AboutViewModel();
            Assert.Equal("Previous Version", vm.GoBackLabel);
            Assert.Equal("Go back to Version 21", vm.GoBackTip);
        }
        finally { state.SetChoicesForTest(OpenTabletArtist.Services.Updater.UpdateChoices.None); }
    }
}
