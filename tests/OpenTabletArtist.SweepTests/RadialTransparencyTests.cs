using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using OpenTabletArtist.Controls;
using Xunit;

namespace OpenTabletArtist.SweepTests;

/// <summary>TASKS #58: Menu transparency changes only the slice and centre backgrounds. The slice under the pen stays
/// as clear as before.</summary>
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

    private static (Color Slice, Color Highlighted) Sample(int transparency)
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
        w.Close();
        return (a, b);
    }

    [AvaloniaFact]
    public void Slices_FollowTheTransparency_ButTheHighlightDoesNot()
    {
        var solid = Sample(0);
        var half = Sample(70);
        var clear = Sample(100);
        // Slice background over the red window: solid = the dark grey, clear = the window shows through.
        Assert.True(solid.Slice.R < 80 && solid.Slice.G < 80, $"solid slice {solid.Slice}");
        Assert.True(half.Slice.R > solid.Slice.R + 60, $"70% slice {half.Slice}");
        Assert.True(clear.Slice.R > 200 && clear.Slice.G < 80, $"clear slice {clear.Slice}");
        // The highlighted slice looks the same at every transparency.
        Assert.Equal(solid.Highlighted, clear.Highlighted);
        Assert.Equal(solid.Highlighted, half.Highlighted);
        Assert.True(solid.Highlighted.B > solid.Highlighted.R + 40, $"highlight {solid.Highlighted}");
    }
}
