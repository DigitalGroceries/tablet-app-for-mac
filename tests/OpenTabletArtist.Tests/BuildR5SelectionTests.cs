using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia.Media;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Build R5: selected/highlighted = light grey fill with near-black text, no pure white;
/// every button-like template has round corners.</summary>
public class BuildR5SelectionTests
{
    private static string AppSource()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "OpenTabletArtist.slnx"))) d = d.Parent;
        return Path.Combine(d!.FullName, "OpenTabletArtist");
    }

    [Fact]
    public void SelectionText_IsDark_OnALightFill_AtEveryStep_AndNeverWhite()
    {
        for (var s = 0; s < LightnessService.Steps; s++)
        {
            var fill = LightnessService.SelectionFill(s);
            var text = LightnessService.SelectionText;
            Assert.NotEqual(Colors.White, fill);
            Assert.True(fill.R <= 0xE4, $"step {s}: fill brighter than #E4E4E4");
            Assert.True(LightnessService.Luminance(text) < LightnessService.Luminance(fill), $"step {s}: text must be darker");
            Assert.True(LightnessService.Contrast(text, fill) >= 7.0, $"step {s}: {LightnessService.Contrast(text, fill):0.0}:1");
            // the highlight must stand out from the raised control behind it (fill on dark, outline on light)
            var p = LightnessService.For(s);
            var edge = LightnessService.SelectionBorder(s);
            Assert.True(LightnessService.Contrast(fill, p.Control) >= 3.0 || LightnessService.Contrast(edge, p.Control) >= 3.0, $"step {s}");
        }
        Assert.Equal(Color.FromRgb(0xD8, 0xD8, 0xD8), LightnessService.SelectionFill(LightnessService.DefaultStep));
        Assert.Equal(Color.FromRgb(0x14, 0x14, 0x14), LightnessService.SelectionText);
    }

    [Fact]
    public void NoCheckedStyle_UsesTheBlueFill_OrALightLabel()
    {
        var style = new Regex(@"<Style Selector=""[^""]*:checked[^""]*"">(?:(?!</Style>).)*?</Style>", RegexOptions.Singleline);
        var bad = Directory.EnumerateFiles(AppSource(), "*.axaml", SearchOption.AllDirectories)
            .SelectMany(f => style.Matches(File.ReadAllText(f)).Select(m => (f, m.Value)))
            .Where(x => Regex.IsMatch(x.Value, @"""Background"" Value=""\{DynamicResource AccentBrush\}""")
                        || x.Value.Contains("TextInverseBrush"))
            .Select(x => Path.GetFileName(x.f)).ToList();
        Assert.True(bad.Count == 0, "blue fill or light label on a selected state in: " + string.Join(", ", bad));
    }

    [Fact]
    public void ButtonTemplates_HaveRoundCorners()
    {
        var styles = File.ReadAllText(Path.Combine(AppSource(), "Themes", "Styles.axaml"));
        foreach (var key in new[] { "WordmarkNav", "PivotTab", "SubtabRadioButton", "AccentButton", "GhostButton" })
        {
            var i = styles.IndexOf($"x:Key=\"{key}\"", StringComparison.Ordinal);
            Assert.True(i >= 0, key);
            var j = styles.IndexOf("</ControlTheme>", i, StringComparison.Ordinal);
            var body = styles[i..j];
            Assert.DoesNotContain("CornerRadius=\"0\"", body);
            Assert.Contains("ButtonCornerRadius", body);
        }
        var app = File.ReadAllText(Path.Combine(AppSource(), "App.axaml"));
        Assert.Contains("<CornerRadius x:Key=\"ControlCornerRadius\">6</CornerRadius>", app);
    }
}
