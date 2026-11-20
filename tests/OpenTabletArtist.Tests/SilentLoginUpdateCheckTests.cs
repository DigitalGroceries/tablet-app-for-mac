using System;
using System.IO;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>V29 (Zak, 20 Nov 2026): "The app shows a message on the Mac screen after every Mac boot saying that it checked for
/// updates. Remove that. The login should be silent like all other Mac apps." The update check at a launch shows no
/// "Checked for updates" line, and at a login start it opens no window: a found update waits on Home.</summary>
public class SilentLoginUpdateCheckTests
{
    [Theory]
    [InlineData(false, false, false, AboutViewModel.LaunchUpdateStep.Nothing)]
    [InlineData(false, true, true, AboutViewModel.LaunchUpdateStep.Nothing)]
    [InlineData(true, false, true, AboutViewModel.LaunchUpdateStep.Nothing)]          // login start: no window, Home shows it
    [InlineData(true, true, true, AboutViewModel.LaunchUpdateStep.Nothing)]           // login start, automatic: no window either
    [InlineData(true, false, false, AboutViewModel.LaunchUpdateStep.Ask)]             // the user opened the app: asks as before
    [InlineData(true, true, false, AboutViewModel.LaunchUpdateStep.DownloadThenAsk)]
    public void ALoginStart_NeverOpensAWindow(bool waiting, bool automatic, bool loginStart, AboutViewModel.LaunchUpdateStep expected) =>
        Assert.Equal(expected, AboutViewModel.LaunchStep(waiting, automatic, loginStart));

    [Fact]
    public void TheLaunchCheck_ShowsNoCheckedForUpdatesLine()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "OpenTabletArtist", "ViewModels", "AboutViewModel.cs"));
        int start = src.IndexOf("internal async Task LaunchCheckAsync(bool loginStart)", StringComparison.Ordinal);
        Assert.True(start > 0, "the launch check is gone or renamed");
        int end = src.IndexOf("\n    }\n", start, StringComparison.Ordinal);
        var body = src[start..end];
        Assert.DoesNotContain("ActionConfirmation", body);
        Assert.DoesNotContain("CheckForUpdates()", body);                               // the button's command shows the line
        Assert.Contains("public Task LaunchCheckAsync() => LaunchCheckAsync(Program.LaunchInBackground);", src);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "OpenTabletArtist.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
