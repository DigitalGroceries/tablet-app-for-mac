using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using OpenTabletArtist.Controls;
using Xunit;

namespace OpenTabletArtist.SweepTests;

/// <summary>V29 #142: pictures of the Eraser page with the Colour Palette Settings (Zak's window size) and of the palette itself.
/// Output: $OTA_SHOTS/palette-*.png or /workspace.</summary>
public class PaletteShots
{
    static string Dir => System.Environment.GetEnvironmentVariable("OTA_SHOTS") ?? "/workspace";

    [AvaloniaFact]
    public void EraserPage_WithThePalette()
    {
        var (_, view, w) = RadialSpotShots.Open(1046, 1365, radialOnButton2: true, tab: "EraserTab");
        view.FindControl<StackPanel>("PaletteSpot")!.BringIntoView();
        RadialSpotShots.Settle(w);
        w.CaptureRenderedFrame()!.Save(System.IO.Path.Combine(Dir, "palette-eraser-page.png"));
        w.Close();
    }

    [AvaloniaFact]
    public void ThePaletteAlone()
    {
        var wheel = new ColourPaletteWheel { Width = 324, Height = 324, Highlight = 5 };
        var w = new Window { Content = wheel, Width = 340, Height = 340 };
        w.Show();
        RadialSpotShots.Settle(w);
        w.CaptureRenderedFrame()!.Save(System.IO.Path.Combine(Dir, "palette-wheel.png"));
        w.Close();
    }
}
