using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Build R2: lightness slider (dark by default, never pure white, readable text).</summary>
public class BuildR2LightnessTests
{
    private static string AppSource()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "OpenTabletArtist.slnx"))) d = d.Parent;
        return Path.Combine(d!.FullName, "OpenTabletArtist");
    }

    // User content and colour maths may use real white: the paint palette's white swatch, the colour
    // wheel's saturation gradient and the accent "lighten" mix.
    private static readonly string[] Allowed = { "ColorPalette.cs", "HueTriangleWheel.cs", "ThemeViewModel.cs" };
    private static readonly Regex White = new(
        @"Foreground=""White""|Value=""White""|=""White""|#(?:[0-9A-Fa-f]{2})?FFFFFF\b|#FFF\b|Brushes\.White|Colors\.White",
        RegexOptions.IgnoreCase);

    [Fact]
    public void NoViewOrStyle_HardCodesWhite()
    {
        var root = AppSource();
        var bad = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".axaml") || f.EndsWith(".cs")) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(f => File.ReadLines(f).Select((l, i) => (f, i, l)))
            .Where(x => White.IsMatch(x.l) && !(Allowed.Contains(Path.GetFileName(x.f)) && !x.l.Contains("Pen(")))
            .Select(x => $"{Path.GetRelativePath(root, x.f)}:{x.i + 1}: {x.l.Trim()}").ToList();
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    [Fact]
    public void Default_IsDarkLikeAffinity()
    {
        var p = LightnessService.For(LightnessService.DefaultStep);
        Assert.True(p.Dark);
        Assert.InRange(p.Base.R, 0x16, 0x1A);   // R3: about #181818
        Assert.InRange(p.Pane.R, 0x28, 0x2C);
        Assert.InRange(p.Well.R, 0x33, 0x37);
        Assert.InRange(p.Control.R, 0x3F, 0x43);
        Assert.InRange(p.Border.R, 0x48, 0x4C);
        Assert.True(p.Surface.R > p.Base.R);   // panels a little lighter than the window
    }

    [Fact]
    public void EveryStep_HasNoWhite_AndAaText_OnEverySurface()
    {
        for (int s = 0; s < LightnessService.Steps; s++)
        {
            var p = LightnessService.For(s);
            foreach (var (_, c) in LightnessService.Resources(s).Concat(LightnessService.BrushResources(s)))
                Assert.False(c.R == 0xFF && c.G == 0xFF && c.B == 0xFF, $"white at step {s}");
            foreach (var bg in p.Layers)
            {
                Assert.True(LightnessService.Contrast(p.Text, bg) >= 4.5, $"text step {s}");
                Assert.True(LightnessService.Contrast(p.TextSecondary, bg) >= 4.5, $"secondary step {s}");
                Assert.True(LightnessService.Contrast(p.TextMuted, bg) >= 3.0, $"muted step {s}");
            }
            Assert.True(LightnessService.Contrast(p.OnAccent, LightnessService.Accent) >= 4.5, "selected-button label");
        }
        Assert.True(LightnessService.For(0).Base.R < LightnessService.For(LightnessService.Steps - 1).Base.R);
    }

    [Fact]
    public void Setting_Persists_AndIsClamped()
    {
        var before = AppSettings.Get(LightnessService.Key);
        try
        {
            LightnessService.Set(5);
            Assert.Equal("5", AppSettings.Get(LightnessService.Key));
            Assert.Equal(5, LightnessService.Saved);
            LightnessService.Set(99);
            Assert.Equal(LightnessService.Steps - 1, LightnessService.Saved);
        }
        finally { AppSettings.Set(LightnessService.Key, before ?? LightnessService.DefaultStep.ToString()); }
    }

    [Fact]
    public void Migration_FromOpenTabletArtist_CarriesLightness() =>
        Assert.True(SettingsMigration.ShouldCopy(LightnessService.Key));

    [Fact]
    public void Layers_KeepAClearStep_AtEveryLightness()
    {
        for (int s = 0; s < LightnessService.Steps; s++)
        {
            var l = LightnessService.For(s).Layers;
            for (int i = 1; i < l.Length; i++)
            {
                Assert.True(l[i].R - l[i - 1].R >= 10, $"step {s}: layer {i} only {l[i].R - l[i - 1].R} grey levels above layer {i - 1}");
                Assert.True(LightnessService.Luminance(l[i]) > LightnessService.Luminance(l[i - 1]));
            }
            var p = LightnessService.For(s);
            Assert.True(Math.Abs(p.Border.R - p.Pane.R) >= 0x14, $"step {s}: border not visible on panels");
        }
    }
}
