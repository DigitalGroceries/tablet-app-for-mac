using System;
using System.IO;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>R9: the menu-bar icon is a black-alpha pen outline used as a macOS template image; the
/// Dock/app icon is unchanged.</summary>
public class BuildR9TrayIconTests
{
    private static string RepoFile(string rel)
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "OpenTabletArtist", "AppTray.cs"))) d = d.Parent;
        Assert.NotNull(d);
        return Path.Combine(d!.FullName, rel);
    }

    [AvaloniaTheory]
    [InlineData("avares://OpenTabletArtist/Assets/trayicon.png", 18)]
    [InlineData("avares://OpenTabletArtist/Assets/trayicon@2x.png", 36)]
    public void TrayIcon_IsABlackAlphaTemplateImage_WithATransparentBackground(string uri, int px)
    {
        using var s = AssetLoader.Open(new Uri(uri));
        // Decoded with Skia directly (the headless test platform's Bitmap is a stub).
        using var bmp = SkiaSharp.SKBitmap.Decode(s);
        Assert.Equal(px, bmp.Width);
        Assert.Equal(px, bmp.Height);
        var buf = new byte[px * px * 4];
        for (int y = 0; y < px; y++)
            for (int x = 0; x < px; x++)
            {
                var c = bmp.GetPixel(x, y); int i = (y * px + x) * 4;
                buf[i] = c.Red; buf[i + 1] = c.Green; buf[i + 2] = c.Blue; buf[i + 3] = c.Alpha;
            }
        int opaque = 0, clear = 0;
        for (int i = 0; i < buf.Length; i += 4)
        {
            byte a = buf[i + 3];
            if (a == 0) { clear++; continue; }
            if (a > 200) opaque++;
            Assert.True(buf[i] == 0 && buf[i + 1] == 0 && buf[i + 2] == 0, "template pixels must be pure black (colour comes from alpha)");
        }
        Assert.True(clear > px * px / 2, "background must be transparent (no frame or fill)");
        Assert.True(opaque > px, "the pen outline must be visible");
        // No frame: the four corners are empty.
        foreach (var (x, y) in new[] { (0, 0), (px - 1, 0), (0, px - 1), (px - 1, px - 1) })
            Assert.Equal(0, buf[(y * px + x) * 4 + 3]);
    }

    [AvaloniaFact]
    public void AppTray_UsesTheTemplateIcon_AndTheTemplateFlagIsSet()
    {
        Assert.Equal("avares://OpenTabletArtist/Assets/trayicon@2x.png", AppTray.TrayIconUri);
        var src = File.ReadAllText(RepoFile("OpenTabletArtist/AppTray.cs"));
        Assert.Contains("MacOSProperties.SetIsTemplateIcon(tray, true)", src);
        Assert.Contains("ApplyTrayIcon(_tray)", src);
        Assert.Contains("AssetLoader.Open(new Uri(iconUri ?? TrayIconUri))", src);
        Assert.DoesNotContain("Assets/appicon.png\"));\n            _tray.Icon", src);
        var t = new TrayIcon();
        MacOSProperties.SetIsTemplateIcon(t, true);
        Assert.True(MacOSProperties.GetIsTemplateIcon(t));
    }

    [Theory]
    [InlineData("OpenTabletArtist/Assets/appicon.png", "1311d382ec9c440ca86838d13d8ee14ecb1dc4aa35f51d88a2ff16d648d5b17d")]
    [InlineData("OpenTabletArtist/Assets/appicon.ico", "e3bf8aa16ec4f76a8c14cf2fee58f121e68b4c098fad5fb4999061948c7711b3")]
    [InlineData("OpenTabletArtist/Assets/appicon.svg", "d74ba4906727b557a4d88119c1a8a260c87825fd85a9e5c03ddffa62f48330bf")]
    public void AppIcon_IsUnchanged(string rel, string sha)
    {
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(RepoFile(rel)))).ToLowerInvariant();
        Assert.Equal(sha, hash);
    }
}

/// <summary>R10: tray icon setup is fail-safe.</summary>
public class BuildR10TrayFailSafeTests
{
    [AvaloniaFact]
    public void ApplyTrayIcon_UsesTheTemplateIcon_FromTheRealAssets()
    {
        var t = new TrayIcon();
        Assert.Equal("template", AppTray.ApplyTrayIcon(t));
        Assert.NotNull(t.Icon);
        Assert.True(MacOSProperties.GetIsTemplateIcon(t));
    }

    [AvaloniaFact]
    public void ApplyTrayIcon_MissingTemplateIcon_FallsBackToTheAppIcon_WithoutThrowing()
    {
        var t = new TrayIcon();
        Assert.Equal("fallback", AppTray.ApplyTrayIcon(t, "avares://OpenTabletArtist/Assets/does-not-exist.png"));
        Assert.NotNull(t.Icon);
        Assert.False(MacOSProperties.GetIsTemplateIcon(t));
    }
}
