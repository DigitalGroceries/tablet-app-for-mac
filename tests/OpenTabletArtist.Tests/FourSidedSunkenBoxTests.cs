using System.Linq;
using Avalonia.Media;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Version 31 test build 2 (Digital Groceries, 27 Nov 2026: "How many sides a squared sunken menu should have?" -
/// four; the app drew two). On every Lightness step, dark and light, a sunken panel and the sunken tray have four slopes:
/// a dark band inside the left and top edges, a light band inside the right and bottom edges, each pair equally wide.</summary>
public class FourSidedSunkenBoxTests
{
    [Theory]
    [InlineData("SunkenPanelShadow", 8, 6.5)]
    [InlineData("SunkenTrayShadow", 12.5, 9.5)]
    public void ASunkenBoxHasFourSlopesOnEveryStep(string key, double side, double topBottom)
    {
        for (int step = 0; step < LightnessService.Steps; step++)
        {
            var shadows = (BoxShadows)LightnessService.SunkenResources(step).First(r => r.Key == key).Value;
            var all = Enumerable.Range(0, shadows.Count).Select(i => shadows[i]).ToList();
            Assert.Equal(4, all.Count);
            Assert.All(all, s => Assert.True(s.IsInset));
            var right = all.Single(s => s.OffsetX == -side);
            var bottom = all.Single(s => s.OffsetY == -topBottom);
            var left = all.Single(s => s.OffsetX == side);
            var top = all.Single(s => s.OffsetY == topBottom);
            Assert.True(right.Color.R > 0xC0 && bottom.Color.R > 0xC0, $"step {step}: the right and bottom slopes are light");
            Assert.True(left.Color.R == 0 && top.Color.R == 0, $"step {step}: the left and top slopes are dark");
            Assert.True(left.Color.A >= 0x28 && top.Color.A >= 0x28, $"step {step}: the dark slopes are strong enough to be seen");
            Assert.True(top.Color.A >= left.Color.A, $"step {step}: the top slope faces away from the light most");
        }
    }
}
