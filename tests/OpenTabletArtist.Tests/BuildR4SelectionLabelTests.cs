using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia.Media;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Build R4: labels on the blue selection (selected tabs/pills, badges) are light #E4E4E4, AA.</summary>
public class BuildR4SelectionLabelTests
{
    private static string AppSource()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "OpenTabletArtist.slnx"))) d = d.Parent;
        return Path.Combine(d!.FullName, "OpenTabletArtist");
    }

    [Fact]
    public void SelectionLabel_IsSoftLight_AndAa_AtEveryStep()
    {
        for (var s = 0; s < LightnessService.Steps; s++)
        {
            var p = LightnessService.For(s);
            Assert.Equal(Color.FromRgb(0xE4, 0xE4, 0xE4), p.OnAccent);
            Assert.True(LightnessService.Contrast(p.OnAccent, LightnessService.Accent) >= 4.5, $"step {s}");
        }
    }

    [Fact]
    public void NoStyle_PutsDarkTextOnTheSelectionBlue()
    {
        var style = new Regex(@"<Style Selector=""[^""]*"">(?:(?!</Style>).)*?</Style>", RegexOptions.Singleline);
        var bad = Directory.EnumerateFiles(AppSource(), "*.axaml", SearchOption.AllDirectories)
            .SelectMany(f => style.Matches(File.ReadAllText(f)).Select(m => (f, m.Value)))
            .Where(x => x.Value.Contains("Value=\"{DynamicResource AccentBrush}\"") && x.Value.Contains("Property=\"Background\"")
                        && Regex.IsMatch(x.Value, @"Foreground"" Value=""\{DynamicResource (BgBaseBrush|TextPrimaryBrush|BgElevatedBrush)\}"""))
            .Select(x => Path.GetFileName(x.f)).ToList();
        Assert.True(bad.Count == 0, "dark label on blue in: " + string.Join(", ", bad));
    }
}
