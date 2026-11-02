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
        var s = new AppUpdateState();
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
