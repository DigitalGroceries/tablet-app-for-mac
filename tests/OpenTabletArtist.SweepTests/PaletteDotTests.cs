using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using OpenTabletArtist.Controls;
using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.SweepTests;

/// <summary>Zak 19 Nov ~20:13 / 20:21: "a central smaller circle that changes into the current colour chosen" - the palette's middle
/// dot shows the last colour picked, is an empty ring before the first pick, and leaves the four centre buttons' icons clear.</summary>
public class PaletteDotTests
{
    private static Color PixelAt(WriteableBitmap bmp, int x, int y)
    {
        using var fb = bmp.Lock();
        var px = new byte[4];
        System.Runtime.InteropServices.Marshal.Copy(fb.Address + y * fb.RowBytes + x * 4, px, 0, 4);
        return fb.Format == Avalonia.Platform.PixelFormat.Bgra8888
            ? Color.FromArgb(px[3], px[2], px[1], px[0])
            : Color.FromArgb(px[3], px[0], px[1], px[2]);   // Rgba8888
    }

    private static (Color Middle, Color NearEdge, Color QuarterIcon) Render(string? last)
    {
        int size = RadialMenuDef.DefaultSize;
        double outer = RadialMenuGeometry.OuterRadius(size);
        int side = (int)(outer * 2 + 4);
        var wheel = new ColourPaletteWheel { Width = side, Height = side, MenuSize = size, LastColour = last };
        var w = new Window { Width = side, Height = side, Background = Brushes.Magenta, Content = wheel };
        w.Show(); Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        var frame = w.CaptureRenderedFrame()!;
        int c = side / 2;
        double dot = ColourPalette.DotRadius(size);
        var middle = PixelAt(frame, c + 2, c + 2);
        var nearEdge = PixelAt(frame, c + (int)(dot * 0.5), c);
        // The pencil icon sits round its quarter's centroid (top right), well outside the dot.
        double d = 4 * RadialMenuGeometry.InnerRadius(size) / (3 * System.Math.PI);
        var quarter = PixelAt(frame, (int)(c + d), (int)(c - d));
        w.Close();
        return (middle, nearEdge, quarter);
    }

    private static bool Near(Color a, byte r, byte g, byte b, int tol = 6) =>
        System.Math.Abs(a.R - r) <= tol && System.Math.Abs(a.G - g) <= tol && System.Math.Abs(a.B - b) <= tol;

    [AvaloniaFact]
    public void TheMiddleDot_ShowsTheLastColour_OrIsEmptyBeforeTheFirstPick()
    {
        var red = Render("#E32322");
        Assert.True(Near(red.Middle, 0xE3, 0x23, 0x22), $"middle {red.Middle}");
        Assert.True(Near(red.NearEdge, 0xE3, 0x23, 0x22), $"inside the dot {red.NearEdge}");

        var white = Render("#FFFFFF");
        Assert.True(Near(white.Middle, 0xFF, 0xFF, 0xFF), $"middle {white.Middle}");

        var empty = Render(null);
        Assert.True(Near(empty.Middle, 0x1C, 0x1F, 0x25), $"empty dot shows the centre's own colour, got {empty.Middle}");

        var junk = Render("red");                                    // not a #RRGGBB code: empty, never a crash
        Assert.True(Near(junk.Middle, 0x1C, 0x1F, 0x25), $"junk {junk.Middle}");
    }

    [AvaloniaFact]
    public void TheMiddleDot_IsSmall_AndLeavesTheCentreButtonsClear()
    {
        int size = RadialMenuDef.DefaultSize;
        double inner = RadialMenuGeometry.InnerRadius(size);
        double dot = ColourPalette.DotRadius(size);
        // The nearest point of a quarter's icon box (side RadialMenuPie.QuarterIconBox of the inner radius, centred on the
        // quarter's centroid) is further from the middle than the dot's edge.
        double centroid = 4 * inner / (3 * System.Math.PI) * System.Math.Sqrt(2);
        double iconHalfDiagonal = inner * RadialMenuPie.QuarterIconBox / 2 * System.Math.Sqrt(2);
        Assert.True(dot < centroid - iconHalfDiagonal, $"dot {dot:0.0} vs icons from {centroid - iconHalfDiagonal:0.0}");
        // The same picture with and without a colour away from the dot: the dot changes nothing else.
        var a = Render("#E32322");
        var b = Render(null);
        Assert.Equal(a.QuarterIcon, b.QuarterIcon);
    }
}
