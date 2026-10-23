using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Build R6: no colour brighter than #E4E4E4 (every channel above E4) in any style, template, view or
/// drawing code. Only the paint palette, the colour-wheel maths and the colour-mixing helper may use white.</summary>
public class BuildR6NoNearWhiteTests
{
    private static readonly string[] Allowed = { "ColorPalette.cs", "HueTriangleWheel.cs", "ColorPicker.axaml" };

    private static string AppSource()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "OpenTabletArtist.slnx"))) d = d.Parent;
        return Path.Combine(d!.FullName, "OpenTabletArtist");
    }

    private static bool Bright(params int[] c) => c.All(v => v > 0xE4);

    [Fact]
    public void NoStyleTemplateOrDrawing_UsesAColourBrighterThanE4()
    {
        var hex = new Regex(@"#(?:[0-9A-Fa-f]{2})?([0-9A-Fa-f]{2})([0-9A-Fa-f]{2})([0-9A-Fa-f]{2})\b");
        var rgb = new Regex(@"From(?:A)?rgb\((?:[^,()]+,\s*)?(0x[0-9A-Fa-f]{2}|\d{1,3}),\s*(0x[0-9A-Fa-f]{2}|\d{1,3}),\s*(0x[0-9A-Fa-f]{2}|\d{1,3})\)", RegexOptions.IgnoreCase);
        var named = new Regex(@"=""(White|WhiteSmoke|Snow|GhostWhite|Ivory)""|Brushes\.White\b|Colors\.White\b");
        static int N(string s) => s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? int.Parse(s[2..], NumberStyles.HexNumber) : int.Parse(s);
        var bad = Directory.EnumerateFiles(AppSource(), "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".axaml") || f.EndsWith(".cs")) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !Allowed.Contains(Path.GetFileName(f)))
            .SelectMany(f => File.ReadAllLines(f).Select((l, i) => (f, i, l)))
            .Where(x => !x.l.TrimStart().StartsWith("//") && !x.l.Contains("Mix(c, Colors.White"))
            .Where(x => hex.Matches(x.l).Any(m => m.Length is 7 or 9 && Bright(Convert.ToInt32(m.Groups[1].Value, 16), Convert.ToInt32(m.Groups[2].Value, 16), Convert.ToInt32(m.Groups[3].Value, 16)))
                        || rgb.Matches(x.l).Any(m => Bright(N(m.Groups[1].Value), N(m.Groups[2].Value), N(m.Groups[3].Value)))
                        || named.IsMatch(x.l))
            .Select(x => $"{Path.GetFileName(x.f)}:{x.i + 1}: {x.l.Trim()}").ToList();
        Assert.True(bad.Count == 0, "near-white colours:\n" + string.Join("\n", bad));
    }
}

/// <summary>Build R8: a slider whose Minimum/Maximum is a binding falls back to the control default when the
/// page is left and clamps (and writes back) its bound Value. Two-way-bound sliders use literal bounds.</summary>
public class BuildR8SliderBoundsTests
{
    [Fact]
    public void TwoWaySliders_HaveLiteralBounds()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "OpenTabletArtist.slnx"))) d = d.Parent;
        var root = Path.Combine(d!.FullName, "OpenTabletArtist", "Views");
        var slider = new Regex(@"<(?:ctrl:LabeledSlider|Slider)\b[^>]*>", RegexOptions.Singleline);
        var bad = Directory.EnumerateFiles(root, "*.axaml")
            .SelectMany(f => slider.Matches(File.ReadAllText(f)).Select(m => (f, m.Value)))
            .Where(x => Regex.IsMatch(x.Value, @"Value=""\{Binding") && Regex.IsMatch(x.Value, @"(Minimum|Maximum)=""\{Binding")
                        && !x.Value.Contains("SpeedSlider") && !x.Value.Contains("LightnessSlider"))
            .Select(x => Path.GetFileName(x.f)).ToList();
        Assert.True(bad.Count == 0, "bound slider bounds in: " + string.Join(", ", bad));
    }
}
