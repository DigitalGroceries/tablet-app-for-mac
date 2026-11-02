using System;
using System.IO;
using System.IO.Compression;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

public class DiagnosticReportTests
{
    [Fact]
    public void Screenshots_are_added_and_non_images_skipped()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var zip = DiagnosticBundle.Create(dir, new[] { "Intuos" }, Array.Empty<string>(), "1.0");
        var png = Path.Combine(dir, "a.png"); File.WriteAllBytes(png, new byte[] { 1, 2, 3 });
        var txt = Path.Combine(dir, "b.txt"); File.WriteAllText(txt, "x");
        Assert.Equal(1, DiagnosticBundle.AddScreenshots(zip, new[] { png, txt }));
        using var z = ZipFile.OpenRead(zip);
        Assert.NotNull(z.GetEntry("screenshots/1-a.png"));
        Assert.NotNull(z.GetEntry("summary.txt"));
    }

    [Fact]
    public void Issue_url_targets_repo_and_has_title()
    {
        var u = DiagnosticBundle.IssueUrl("x.zip", "0.6", new[] { "PTH-651" });
        Assert.StartsWith("https://github.com/DigitalGroceries/tablet-app-for-mac/issues/new?", u);
        Assert.Contains("title=", u);
        Assert.Contains("x.zip", Uri.UnescapeDataString(u));
    }
}
