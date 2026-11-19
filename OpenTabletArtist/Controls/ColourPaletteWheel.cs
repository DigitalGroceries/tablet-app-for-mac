using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Controls;

/// <summary>
/// V29 #142 (Zak 19 Nov 2026: a colour menu "just like a painter uses that thing where different shades of the paint are already
/// mixed"; his pictures: an artist's colour wheel): 12 hues round the edge, 4 ready-mixed shades of each towards the centre
/// (<see cref="ColourPalette"/>), and the radial menu's centre with only the X (close) and the hand (drag) in use. Drawn solid, never
/// see-through, so an app's eyedropper picks exactly the colour shown. The same size as the radial menu.
/// </summary>
public sealed class ColourPaletteWheel : Control
{
    public static readonly StyledProperty<int> MenuSizeProperty = AvaloniaProperty.Register<ColourPaletteWheel, int>(nameof(MenuSize), RadialMenuDef.DefaultSize);
    public static readonly StyledProperty<int> HighlightProperty = AvaloniaProperty.Register<ColourPaletteWheel, int>(nameof(Highlight), -3);

    /// <summary>The menu size (1 to 50), as the radial menu's.</summary>
    public int MenuSize { get => GetValue(MenuSizeProperty); set => SetValue(MenuSizeProperty, value); }
    /// <summary>The swatch (0 to 47) or centre quarter under the pointer; anything else highlights nothing.</summary>
    public int Highlight { get => GetValue(HighlightProperty); set => SetValue(HighlightProperty, value); }

    static ColourPaletteWheel() => AffectsRender<ColourPaletteWheel>(MenuSizeProperty, HighlightProperty);

    private static readonly IBrush Rim = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
    private static readonly IBrush CentreFill = new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x30));
    private static readonly IBrush CentreHover = new SolidColorBrush(Color.FromRgb(0x48, 0x48, 0x48));
    private static readonly IPen Gap = new Pen(new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)), 1.5);
    private static readonly IPen HighlightPen = new Pen(new SolidColorBrush(Color.FromRgb(0xE4, 0xE4, 0xE4)), 3);
    private static readonly IPen Mark = new Pen(new SolidColorBrush(Color.FromRgb(0xE4, 0xE4, 0xE4)), 2.2, lineCap: PenLineCap.Round);

    public override void Render(DrawingContext ctx)
    {
        var c = new Point(Bounds.Width / 2, Bounds.Height / 2);
        double outer = Math.Min(RadialMenuGeometry.OuterRadius(MenuSize), Math.Min(Bounds.Width, Bounds.Height) / 2 - 1);
        double scale = outer / RadialMenuGeometry.OuterRadius(MenuSize);
        double inner = RadialMenuGeometry.InnerRadius(MenuSize) * scale;
        double band = (outer - inner) / ColourPalette.Shades;
        ctx.DrawEllipse(Rim, null, c, outer + 1, outer + 1);
        const double step = 360.0 / ColourPalette.Hues;
        for (int hue = 0; hue < ColourPalette.Hues; hue++)
        for (int shade = 0; shade < ColourPalette.Shades; shade++)
        {
            var (r, g, b) = ColourPalette.Colour(hue, shade);
            double ro = outer - shade * band, ri = ro - band;
            var geo = Sector(c, ri, ro, hue * step - step / 2, hue * step + step / 2);
            ctx.DrawGeometry(new SolidColorBrush(Color.FromRgb(r, g, b)), Gap, geo);
        }
        if (Highlight >= 0 && Highlight < ColourPalette.Hues * ColourPalette.Shades)
        {
            var (hue, shade) = ColourPalette.Split(Highlight);
            double ro = outer - shade * band, ri = ro - band;
            ctx.DrawGeometry(null, HighlightPen, Sector(c, ri + 1.5, ro - 1.5, hue * step - step / 2 + 0.8, hue * step + step / 2 - 0.8));
        }
        // The centre: the X (top left) closes, the hand (bottom right) drags; the other two quarters are empty here.
        ctx.DrawEllipse(CentreFill, Gap, c, inner, inner);
        if (Highlight == RadialMenuGeometry.Centre) ctx.DrawGeometry(CentreHover, null, Sector(c, 0, inner, 270, 360));
        if (Highlight == RadialMenuGeometry.Hand) ctx.DrawGeometry(CentreHover, null, Sector(c, 0, inner, 90, 180));
        ctx.DrawLine(Gap, new Point(c.X - inner, c.Y), new Point(c.X + inner, c.Y));
        ctx.DrawLine(Gap, new Point(c.X, c.Y - inner), new Point(c.X, c.Y + inner));
        double q = inner * 0.5, s = inner * 0.16;
        var x = new Point(c.X - q, c.Y - q);
        ctx.DrawLine(Mark, new Point(x.X - s, x.Y - s), new Point(x.X + s, x.Y + s));
        ctx.DrawLine(Mark, new Point(x.X - s, x.Y + s), new Point(x.X + s, x.Y - s));
        var h = new Point(c.X + q, c.Y + q);   // the drag mark: four short arrows' stems
        ctx.DrawLine(Mark, new Point(h.X - s * 1.3, h.Y), new Point(h.X + s * 1.3, h.Y));
        ctx.DrawLine(Mark, new Point(h.X, h.Y - s * 1.3), new Point(h.X, h.Y + s * 1.3));
    }

    /// <summary>An annular sector between two radii, angles in degrees clockwise from the top.</summary>
    private static StreamGeometry Sector(Point c, double ri, double ro, double fromDeg, double toDeg)
    {
        static Point At(Point c, double r, double deg)
        {
            double a = (deg - 90) * Math.PI / 180;
            return new Point(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a));
        }
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            bool large = toDeg - fromDeg > 180;
            ctx.BeginFigure(At(c, ro, fromDeg), true);
            ctx.ArcTo(At(c, ro, toDeg), new Size(ro, ro), 0, large, SweepDirection.Clockwise);
            if (ri > 0.5)
            {
                ctx.LineTo(At(c, ri, toDeg));
                ctx.ArcTo(At(c, ri, fromDeg), new Size(ri, ri), 0, large, SweepDirection.CounterClockwise);
            }
            else ctx.LineTo(c);
            ctx.EndFigure(true);
        }
        return g;
    }
}
