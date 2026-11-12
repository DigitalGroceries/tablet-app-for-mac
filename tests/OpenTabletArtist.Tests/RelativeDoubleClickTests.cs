using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Maxime (11 Nov, V25, PTH-450, Mouse-like mode): a pen button set to Double Click sent the pointer to the
/// top-left corner of the screen. In relative mode the driver's pending position is movement, not a place, and
/// PostDoubleClick used it as the click position. It must go through ClickPosition (pointer + movement in relative
/// mode, unchanged in absolute mode). The macOS mouse can't run here, so this guards the source.</summary>
public class RelativeDoubleClickTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "external", "OpenTabletDriver"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    [Fact]
    public void PostDoubleClick_ClicksWhereThePointerIs_InEveryMode()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "external", "OpenTabletDriver", "OpenTabletDriver.Desktop",
            "Interop", "Input", "MacOSVirtualMouse.cs"));
        var body = Regex.Match(src, @"public void PostDoubleClick\(\)\s*\{(?<b>.*?)\n        \}", RegexOptions.Singleline).Groups["b"].Value;
        Assert.False(string.IsNullOrEmpty(body), "PostDoubleClick not found");
        Assert.Contains("ClickPosition(", body);
        Assert.DoesNotContain("new CGPoint(x, y)", body);
    }

    [Fact]
    public void RelativePointer_ClickPosition_AddsTheMovementToThePointer()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "external", "OpenTabletDriver", "OpenTabletDriver.Desktop",
            "Interop", "Input", "Relative", "MacOSRelativePointer.cs"));
        Assert.Matches(@"override Vector2 ClickPosition\(float x, float y\)[\s\S]*?GetCursorPosition\(\)[\s\S]*?at\.x \+ x", src);
    }
}
