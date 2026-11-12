using Avalonia;
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

/// <summary>v27 (Zak 21:49): Menu transparency fades the whole menu (slices, labels, icons, ring and highlight)
/// so work behind shows through. 0% is solid; 100% is still <see cref="RadialMenuDef.MinVisibleOpacity"/> (10%)
/// so the menu stays usable. The Pen page preview uses the same pie, so it changes with the slider.</summary>
public class RadialTransparencyTests
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

    private static (Color Slice, Color Highlighted, double Opacity) Sample(int transparency)
    {
        var pie = new RadialMenuPie
        {
            Width = 300, Height = 300, Transparency = transparency,
            Labels = new[] { "Undo", "Redo", "Copy", "Paste", "Cut", "All", "Out", "In" },
            SetFlags = new[] { true, true, true, true, true, true, true, true },
            Highlight = 2,
        };
        var w = new Window { Width = 300, Height = 300, Background = Brushes.Red, Content = pie };
        w.Show(); Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        var frame = w.CaptureRenderedFrame()!;
        // slice 1 (upper right, not highlighted) and slice 2 (right, highlighted), near the inner edge, away from the label text
        var a = PixelAt(frame, 150 + 62, 150 - 30);
        var b = PixelAt(frame, 150 + 66, 150 + 4);
        double opacity = pie.Opacity;
        w.Close();
        return (a, b, opacity);
    }

    [AvaloniaFact]
    public void WholeMenu_FollowsTheTransparency_WithAUsableFloor_AndThePreviewWouldMatch()
    {
        Assert.Equal(1.0, RadialMenuDef.OpacityFor(0));
        Assert.Equal(RadialMenuDef.MinVisibleOpacity, RadialMenuDef.OpacityFor(100));

        var solid = Sample(0);
        var mid = Sample(40);
        var clear = Sample(100);

        Assert.Equal(1.0, solid.Opacity);
        Assert.Equal(RadialMenuDef.OpacityFor(40), mid.Opacity);
        Assert.Equal(RadialMenuDef.MinVisibleOpacity, clear.Opacity);

        // Slice background over the red window: solid = the dark grey; 100% lets most of the red show through.
        Assert.True(solid.Slice.R < 80 && solid.Slice.G < 80, $"solid slice {solid.Slice}");
        Assert.True(mid.Slice.R > solid.Slice.R + 40, $"40% slice {mid.Slice} vs solid {solid.Slice}");
        Assert.True(clear.Slice.R > mid.Slice.R + 40, $"100% slice {clear.Slice} vs 40% {mid.Slice}");
        Assert.True(clear.Slice.R > 200, $"100% slice should show the red window through: {clear.Slice}");
        // Not fully invisible: a little of the grey remains (the 10% floor).
        Assert.True(clear.Slice.G > 0 || clear.Slice.B > 0 || clear.Slice.R < 255, $"100% slice vanished: {clear.Slice}");

        // The highlighted slice fades too, so the preview at 0% and 100% cannot look the same.
        Assert.True(solid.Highlighted.B > solid.Highlighted.R + 40, $"highlight {solid.Highlighted}");
        Assert.True(clear.Highlighted.R > solid.Highlighted.R + 40, $"100% highlight {clear.Highlighted} vs solid {solid.Highlighted}");
        Assert.NotEqual(solid.Highlighted, clear.Highlighted);
        Assert.NotEqual(solid.Slice, clear.Slice);
    }
}
