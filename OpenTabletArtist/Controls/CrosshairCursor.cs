using System;
using System.IO;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace OpenTabletArtist.Controls;

/// <summary>Version 2: the Scribble canvas cursor. A thin #E4E4E4 crosshair with a dark outline, so it shows on the
/// dark canvas and on light strokes. The hotspot is the exact centre pixel.</summary>
public static class CrosshairCursor
{
    public const int Size = 31;          // odd, so there is one true centre pixel
    public const int Centre = Size / 2;  // 15
    public const int Gap = 3;            // clear pixels each side of the centre point, so the point itself stays visible
    public static readonly SKColor Line = new(0xE4, 0xE4, 0xE4);
    public static readonly SKColor Outline = new(0x14, 0x14, 0x14);

    private static Cursor? _cursor;
    public static Cursor Cursor => _cursor ??= Create();

    /// <summary>The cursor image as PNG bytes (straight alpha, Size × Size).</summary>
    public static byte[] RenderPng()
    {
        using var bmp = new SKBitmap(new SKImageInfo(Size, Size, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        bmp.Erase(SKColors.Transparent);
        void Px(int x, int y, SKColor c) { if (x >= 0 && y >= 0 && x < Size && y < Size) bmp.SetPixel(x, y, c); }
        bool OnArm(int x, int y) =>
            (x == Centre && Math.Abs(y - Centre) > Gap) || (y == Centre && Math.Abs(x - Centre) > Gap) || (x == Centre && y == Centre);
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                if (OnArm(x, y))
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                            if (!OnArm(x + dx, y + dy)) Px(x + dx, y + dy, Outline);
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                if (OnArm(x, y)) Px(x, y, Line);
        using var img = SKImage.FromBitmap(bmp);
        return img.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    private static Cursor Create()
    {
        try
        {
            using var ms = new MemoryStream(RenderPng());
            return new Cursor(new Bitmap(ms), new PixelPoint(Centre, Centre));
        }
        catch (Exception)
        {
            return new Cursor(StandardCursorType.Cross);   // platform without bitmap cursors
        }
    }
}
