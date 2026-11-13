using System.Linq;
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using OpenTabletArtist.Controls;
using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.SweepTests;

/// <summary>TASKS #59 (Zak 18:21): the four centre icons (X, pencil, Restore arrow, hand) have the same visual size and are
/// centred on the centroid of their quarter, clear of the dividing lines; the slice labels are centred in their slices.
/// Measured on headless renders at several menu sizes, and a picture is saved to $OTA_SHOTS (or /workspace).</summary>
public class RadialCentreIconTests
{
    static string Dir => Environment.GetEnvironmentVariable("OTA_SHOTS") ?? "/workspace";

    private static readonly string[] Labels = { "Undo", "Redo", "Copy", "Paste", "Cut", "Select all", "Zoom out", "Zoom in" };

    private static RadialMenuPie MakePie(double diameter, bool labels = false) => new()
    {
        Width = diameter, Height = diameter, Transparency = 0,
        Labels = labels ? Labels : null,
        SetFlags = new[] { true, true, true, true, true, true, true, true },
    };

    private static WriteableBitmap Render(Control content, double w, double h)
    {
        var win = new Window { Width = w, Height = h, Background = Brushes.Black, Content = content };
        win.Show(); Dispatcher.UIThread.RunJobs(); win.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        var frame = win.CaptureRenderedFrame()!;
        win.Close();
        return frame;
    }

    private static (byte R, byte G, byte B) PixelAt(WriteableBitmap bmp, int x, int y)
    {
        using var fb = bmp.Lock();
        var px = new byte[4];
        System.Runtime.InteropServices.Marshal.Copy(fb.Address + y * fb.RowBytes + x * 4, px, 0, 4);
        return fb.Format == Avalonia.Platform.PixelFormat.Bgra8888 ? (px[2], px[1], px[0]) : (px[0], px[1], px[2]);
    }

    [AvaloniaTheory]
    [InlineData(180)]
    [InlineData(260)]
    [InlineData(320)]
    [InlineData(460)]
    [InlineData(640)]
    public void CentreIcons_AreTheSameSize_CentredInTheirQuarter_AndClearOfTheDividers(int diameter)
    {
        var frame = Render(MakePie(diameter), diameter, diameter);
        double c = diameter / 2.0;
        double outer = Math.Max(10, diameter / 2.0 - 2), inner = outer * RadialMenuGeometry.InnerRatio;
        double dividerHalf = Math.Max(2, inner / 28) / 2 + 1;   // the cross, with a pixel of anti-aliasing
        double d = 4 * inner / (3 * Math.PI);
        var sides = new List<double>();
        for (int q = 0; q < 4; q++)
        {
            int sx = q is 1 or 2 ? 1 : -1, sy = q is 2 or 3 ? 1 : -1;
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            for (int y = 0; y < diameter; y++)
                for (int x = 0; x < diameter; x++)
                {
                    double dx = x + 0.5 - c, dy = y + 0.5 - c;
                    if (Math.Sign(dx) != sx || Math.Sign(dy) != sy) continue;
                    if (Math.Abs(dx) <= dividerHalf || Math.Abs(dy) <= dividerHalf) continue;   // the cross itself
                    if (Math.Sqrt(dx * dx + dy * dy) > inner * 0.96) continue;                   // the disc's edge line
                    var (r, g, b) = PixelAt(frame, x, y);
                    if (r < 120 || g < 120 || b < 120) continue;                                  // icons are light on the dark centre
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x + 1); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y + 1);
                }
            Assert.True(minX < maxX, $"quarter {q} at {diameter}: no icon pixels found");
            double cx = (minX + maxX) / 2 - c, cy = (minY + maxY) / 2 - c;
            double tol = 1.5 + 0.01 * inner;
            Assert.True(Math.Abs(cx - sx * d) <= tol && Math.Abs(cy - sy * d) <= tol,
                $"quarter {q} at {diameter}: icon centre ({cx:F1},{cy:F1}) should be ({sx * d:F1},{sy * d:F1}) ± {tol:F1}");
            // Clear of the dividing lines: a real gap, not touching.
            double gapX = sx > 0 ? minX - c : c - maxX, gapY = sy > 0 ? minY - c : c - maxY;
            Assert.True(gapX >= 0.12 * inner && gapY >= 0.12 * inner, $"quarter {q} at {diameter}: gap to the dividers {gapX:F1} / {gapY:F1} (inner {inner:F1})");
            double side = Math.Max(maxX - minX, maxY - minY);
            sides.Add(side);
            Assert.True(Math.Abs(side - inner * RadialMenuPie.QuarterIconBox) <= 2.5, $"quarter {q} at {diameter}: icon size {side:F1}, wanted {inner * RadialMenuPie.QuarterIconBox:F1}");
        }
        Assert.True(sides.Max() - sides.Min() <= 3.0, $"icon sizes differ at {diameter}: {string.Join(", ", sides.Select(s => s.ToString("F1")))}");
    }

    [AvaloniaTheory]
    [InlineData(320)]
    [InlineData(460)]
    public void SliceLabels_AreCentredInTheirSlices(int diameter)
    {
        var frame = Render(MakePie(diameter, labels: true), diameter, diameter);
        double c = diameter / 2.0, outer = Math.Max(10, diameter / 2.0 - 2), inner = outer * RadialMenuGeometry.InnerRatio, mid = (inner + outer) / 2;
        for (int i = 0; i < 8; i++)
        {
            double rad = i * 45 * Math.PI / 180;
            double ex = c + mid * Math.Sin(rad), ey = c - mid * Math.Cos(rad);
            // Text pixels (light) in a box around the expected centre, away from the slice edge lines.
            int half = (int)(outer - inner) / 2;
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            for (int y = (int)(ey - 24); y <= ey + 24; y++)
                for (int x = (int)(ex - half); x <= ex + half; x++)
                {
                    if (x < 0 || y < 0 || x >= diameter || y >= diameter) continue;
                    double dx = x + 0.5 - c, dy = y + 0.5 - c, r = Math.Sqrt(dx * dx + dy * dy);
                    if (r < inner + 3 || r > outer - 3) continue;
                    // only pixels well inside this slice's wedge (not the divider lines)
                    double deg = Math.Atan2(dx, -dy) * 180 / Math.PI; if (deg < 0) deg += 360;
                    double off = Math.Abs(((deg - i * 45 + 540) % 360) - 180);   // degrees from the slice's middle
                    if (off > 20) continue;
                    var (rr, gg, bb) = PixelAt(frame, x, y);
                    if (rr < 150 || gg < 150 || bb < 150) continue;
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x + 1); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y + 1);
                }
            Assert.True(minX < maxX, $"slice {i}: no text found");
            double tx = (minX + maxX) / 2, ty = (minY + maxY) / 2;
            Assert.True(Math.Abs(tx - ex) <= 3 && Math.Abs(ty - ey) <= 4, $"slice {i} at {diameter}: label centre ({tx:F1},{ty:F1}) should be ({ex:F1},{ey:F1})");
        }
    }

    [AvaloniaFact]
    public void Picture_OfTheRadialMenuAtSeveralSizes()
    {
        System.IO.Directory.CreateDirectory(Dir);
        _ = new OpenTabletArtist.ViewModels.ThemeViewModel();
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20, Margin = new Thickness(20), VerticalAlignment = VerticalAlignment.Center };
        foreach (var dia in new[] { 180, 260, 320, 460 })
        {
            var pie = MakePie(dia, labels: true);
            pie.Transparency = RadialMenuDef.DefaultTransparency;
            row.Children.Add(pie);
        }
        var frame = Render(row, 1300, 520);
        frame.Save($"{Dir}/radial-centre-icons-v26.png");
        Assert.True(System.IO.File.Exists($"{Dir}/radial-centre-icons-v26.png"));
        // One big one, the same size as Zak's screenshot, to look at the icons closely.
        var big = MakePie(640, labels: true);
        big.Transparency = RadialMenuDef.DefaultTransparency;
        Render(big, 700, 700).Save($"{Dir}/radial-centre-icons-v26-large.png");
    }

    /// <summary>v27 (Zak 13 Nov 15:44: "an ovoid artefact in the palm of the hand icon"): on a see-through menu each icon is
    /// drawn as one shape, so where its parts overlap (fingers on the palm, the X's crossing, the arrow head on its arc) no
    /// pixel comes out brighter than the icon's own colour. Before the fix the overlaps were up to ~30 levels brighter.</summary>
    [AvaloniaTheory]
    [InlineData(40)]
    [InlineData(70)]
    public void SeeThroughMenu_IconOverlapsAreNotBrighter(int transparency)
    {
        const int diameter = 460;
        var pie = MakePie(diameter);
        pie.Transparency = transparency;
        var frame = Render(pie, diameter, diameter);
        double c = diameter / 2.0, outer = Math.Max(10, diameter / 2.0 - 2), inner = outer * RadialMenuGeometry.InnerRatio;
        double half = inner * RadialMenuPie.QuarterIconBox / 2 + 2;
        foreach (int q in new[] { 0, 2, 3 })   // X, hand, restore arrow (the pencil's cuts are dark on purpose)
        {
            var at = RadialMenuPie.QuarterCentroid(new Point(c, c), inner, q);
            var levels = new List<int>();
            for (int y = (int)(at.Y - half); y <= (int)(at.Y + half); y++)
                for (int x = (int)(at.X - half); x <= (int)(at.X + half); x++)
                    levels.Add(PixelAt(frame, x, y).G);
            // The icon body is the most common bright level; anti-aliased edges are darker than it, never brighter.
            int floor = (levels.Min() + levels.Max()) / 2;   // between the quarter's background and the icon
            var body = levels.Where(l => l > floor).GroupBy(l => l).OrderByDescending(g => g.Count()).First().Key;
            int brightest = levels.Max();
            Assert.True(brightest <= body + 3, $"quarter {q}, transparency {transparency}: body {body}, brightest {brightest} (an overlap shows through)");
        }
    }
}
