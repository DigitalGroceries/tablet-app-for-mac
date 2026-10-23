using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using OpenTabletArtist.Controls;
using OpenTabletArtist.Converters;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>0.89.1 heading rule: headings show in sentence case — first letter capitalised, never all lowercase —
/// with ExpressKeys, JSON, OTD, OTA, OpenTabletArtist kept as written.</summary>
public class HeadingCaseTests
{
    [Theory]
    [InlineData("start at login", "Start at login")]
    [InlineData("about touch ring", "About touch ring")]
    [InlineData("about ExpressKeys", "About ExpressKeys")]
    [InlineData("about expresskeys", "About ExpressKeys")]
    [InlineData("HOME", "Home")]
    [InlineData("expresskeys", "ExpressKeys")]
    [InlineData("json", "JSON")]
    [InlineData("otd settings", "OTD settings")]
    [InlineData("opentabletartist", "OpenTabletArtist")]
    public void Converter_GivesSentenceCase(string input, string expected)
    {
        Assert.Equal(expected, TitleCaseConverter.Apply(input));
        Assert.Equal(expected, LowerCaseConverter.Instance.Convert(input, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture));
    }

    private static string SourceDir()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "OpenTabletArtist", "Views"))) d = d.Parent;
        Assert.NotNull(d);
        return Path.Combine(d!.FullName, "OpenTabletArtist");
    }

    /// <summary>Every heading-style TextBlock literal (Display / section / Heading classes) and every
    /// Section/Title/Header literal in the app's XAML starts with a capital.</summary>
    [Fact]
    public void NoHeadingLiteral_StartsLowercase()
    {
        var bad = new System.Collections.Generic.List<string>();
        var tag = new Regex(@"<[A-Za-z:]+\b[^>]*>", RegexOptions.Singleline);
        foreach (var f in Directory.GetFiles(SourceDir(), "*.axaml", SearchOption.AllDirectories))
        {
            if (f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") || f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
            foreach (Match m in tag.Matches(File.ReadAllText(f)))
            {
                var t = m.Value;
                bool heading = Regex.IsMatch(t, @"Classes=""[^""]*\b(Display|section|Heading)\b");
                foreach (Match a in Regex.Matches(t, @"\b(Text|Title|Header)=""([^""{]+)"""))
                {
                    if (a.Groups[1].Value == "Text" && !heading) continue;
                    var v = a.Groups[2].Value;
                    if (char.IsLetter(v[0]) && char.IsLower(v[0])) bad.Add($"{Path.GetFileName(f)}: {v}");
                }
            }
        }
        Assert.Empty(bad);
    }

    [AvaloniaFact]
    public void RenderedSectionTitle_IsSentenceCase()
    {
        var host = new StackPanel();
        host.Children.Add(new Section { Title = "start at login" });
        host.Children.Add(new Section { Title = "about ExpressKeys" });
        var w = new Window { Content = host, Width = 600, Height = 400 };
        w.Show(); Dispatcher.UIThread.RunJobs();
        var titles = w.GetLogicalDescendants().OfType<TextBlock>()
            .Concat(host.Children.OfType<Section>().SelectMany(s => Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(s).OfType<TextBlock>()))
            .Where(t => t.Name == "PART_Title").Select(t => t.Text).Distinct().ToList();
        Assert.Contains("Start at login", titles);
        Assert.Contains("About ExpressKeys", titles);
        Assert.All(titles, t => Assert.True(string.IsNullOrEmpty(t) || !char.IsLower(t![0]), t));
    }
}
