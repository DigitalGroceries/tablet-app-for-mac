using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenTabletArtist.Controls;
using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>
/// Scribble's canvas draws with the pen tip and, when EraseWithEraser is on, rubs out ink with the
/// eraser end. The Pen page preview pads leave that flag off so the eraser can still show its stroke.
/// </summary>
public class ScribbleEraserTests
{
    private static PenTestCanvas Shown()
    {
        var canvas = new PenTestCanvas { IgnorePointer = true };
        new Window { Width = 400, Height = 300, Content = canvas }.Show();
        Dispatcher.UIThread.RunJobs();
        return canvas;
    }

    private static PenSample At(double x, double y, double pressure = 1, bool eraser = false) =>
        new(x, y, 0, 0, pressure, 0, 0, 0, IsDown: pressure > 0, IsEraser: eraser);

    private static int DarkInkPixels(PenTestCanvas canvas)
    {
        var (bgra, _, _) = canvas.Snapshot()!.Value;
        var n = 0;
        for (var i = 0; i + 3 < bgra.Length; i += 4)
            if (bgra[i] < 50 && bgra[i + 1] < 50 && bgra[i + 2] < 50) n++; // B, G, R — ink is #222222
        return n;
    }

    [AvaloniaFact]
    public void Eraser_Draws_WhenEraseWithEraserIsOff()
    {
        var canvas = Shown();

        canvas.AddSample(At(0.2, 0.5, eraser: true));
        canvas.AddSample(At(0.5, 0.5, eraser: true));

        Assert.True(DarkInkPixels(canvas) > 0);
    }

    [AvaloniaFact]
    public void Eraser_RubsOutATipStroke_WhenEraseWithEraserIsOn()
    {
        var canvas = Shown();
        canvas.EraseWithEraser = true;

        canvas.AddSample(At(0.2, 0.5));
        canvas.AddSample(At(0.5, 0.5));
        canvas.AddSample(At(0.8, 0.5));
        var afterInk = DarkInkPixels(canvas);
        Assert.True(afterInk > 0);

        canvas.AddSample(At(0.2, 0.5, eraser: true));
        canvas.AddSample(At(0.5, 0.5, eraser: true));
        canvas.AddSample(At(0.8, 0.5, eraser: true));
        var afterErase = DarkInkPixels(canvas);

        Assert.True(afterErase < afterInk / 5, $"eraser should remove most ink (before {afterInk}, after {afterErase})");
    }

    [AvaloniaFact]
    public void Tip_StillDraws_WhenEraseWithEraserIsOn()
    {
        var canvas = Shown();
        canvas.EraseWithEraser = true;

        canvas.AddSample(At(0.2, 0.5));
        canvas.AddSample(At(0.5, 0.5));

        Assert.True(DarkInkPixels(canvas) > 0);
    }
}
