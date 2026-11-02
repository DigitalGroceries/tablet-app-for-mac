using System.Collections.Generic;
using System.Threading.Tasks;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Zak (2 Nov): thank donors while an app update downloads.</summary>
public class DonorThanksTests
{
    [Fact]
    public async Task IsDownloading_OnlyWhileTheDownloadRuns()
    {
        var s = new AppUpdateState { MinNoteTime = System.TimeSpan.Zero };
        var seen = new List<bool>();
        s.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(AppUpdateState.IsDownloading)) seen.Add(s.IsDownloading); };
        var gate = new TaskCompletionSource<string?>();
        s.Download = _ => gate.Task;
        var t = s.DownloadWithNoteAsync(new Updater.Release(99, "z", "s"));
        Assert.True(s.IsDownloading);
        gate.SetResult("failed");
        Assert.Equal("failed", await t);
        Assert.False(s.IsDownloading);
        Assert.Equal(new[] { true, false }, seen);
    }

    [Fact]
    public void Wording_IsZaksLine()
    {
        const string line = "We are grateful to all who donated and keep this project alive.";
        Assert.Equal(line, OpenTabletArtist.Views.UpdatePromptWindow.DonorThanks);
        var en = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(Root(), "OpenTabletArtist/Assets/i18n/en.json")));
        Assert.Equal(line, (string?)en["update.donor_thanks"]);
    }

    static string Root([System.Runtime.CompilerServices.CallerFilePath] string f = "") =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(f)!, "..", ".."));
}

public class DonorThanksMinTimeTests
{
    [Fact]
    public async System.Threading.Tasks.Task FastDownload_StillKeepsTheNoteUp()
    {
        var s = new AppUpdateState { MinNoteTime = System.TimeSpan.FromMilliseconds(300), Download = _ => System.Threading.Tasks.Task.FromResult<string?>(null) };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await s.DownloadWithNoteAsync(new Updater.Release(99, "z", "s"));
        Assert.True(sw.ElapsedMilliseconds >= 250);
        Assert.False(s.IsDownloading);
    }
}

/// <summary>The old app does the download, so the new version thanks donors on its first run after an update.</summary>
public class ThankAfterUpdateTests
{
    [Theory]
    [InlineData("19", "21-beta2", true, true)]       // updated from 19
    [InlineData("21-beta2", "21-beta2", true, false)] // same version again
    [InlineData(null, "21-beta2", true, true)]       // no record yet (came from 19/20/21 BETA 1), app used before
    [InlineData(null, "21-beta2", false, false)]     // first install
    public void ShowsOnlyAfterAnUpdate(string? last, string current, bool usedBefore, bool expected) =>
        Assert.Equal(expected, OpenTabletArtist.Views.UpdatePromptWindow.ShouldThankAfterUpdate(last, current, usedBefore));
}
