using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Controls;

/// <summary>
/// V29 #142 (Zak 19 Nov 2026: a colour menu "just like a painter uses that thing where different shades of the paint are already
/// mixed"; his pictures: an artist's colour wheel): 12 hues round the edge, 4 ready-mixed shades of each towards the centre
/// (<see cref="ColourPalette"/>), a ring of greys round the outside, the radial menu's own four centre buttons, and a small middle dot
/// in the last colour picked. Drawn solid, never see-through, so an app's eyedropper picks exactly the colour shown. The same size as
/// the radial menu.
/// </summary>
public sealed class ColourPaletteWheel : Control
{
    public static readonly StyledProperty<int> MenuSizeProperty = AvaloniaProperty.Register<ColourPaletteWheel, int>(nameof(MenuSize), RadialMenuDef.DefaultSize);
    public static readonly StyledProperty<int> HighlightProperty = AvaloniaProperty.Register<ColourPaletteWheel, int>(nameof(Highlight), -3);
    public static readonly StyledProperty<bool> EditingProperty = AvaloniaProperty.Register<ColourPaletteWheel, bool>(nameof(Editing));
    public static readonly StyledProperty<RadialMenuDef?> MenuProperty = AvaloniaProperty.Register<ColourPaletteWheel, RadialMenuDef?>(nameof(Menu));
    public static readonly StyledProperty<string?> LastColourProperty = AvaloniaProperty.Register<ColourPaletteWheel, string?>(nameof(LastColour));

    /// <summary>The menu size (1 to 50), as the radial menu's.</summary>
    public int MenuSize { get => GetValue(MenuSizeProperty); set => SetValue(MenuSizeProperty, value); }
    /// <summary>The swatch (0 to 59) or centre quarter under the pointer; anything else highlights nothing.</summary>
    public int Highlight { get => GetValue(HighlightProperty); set => SetValue(HighlightProperty, value); }
    /// <summary>The pencil is on: the next swatch tapped is changed.</summary>
    public bool Editing { get => GetValue(EditingProperty); set => SetValue(EditingProperty, value); }
    /// <summary>The menu whose palette colours (the user's own, changed with the pencil) are drawn; null = the built-in ones.</summary>
    public RadialMenuDef? Menu { get => GetValue(MenuProperty); set => SetValue(MenuProperty, value); }
    /// <summary>The last colour picked from the palette ("#RRGGBB"), drawn in the middle dot; null or not a colour = an empty dot
    /// (Zak 19 Nov 20:21).</summary>
    public string? LastColour { get => GetValue(LastColourProperty); set => SetValue(LastColourProperty, value); }

    static ColourPaletteWheel() => AffectsRender<ColourPaletteWheel>(MenuSizeProperty, HighlightProperty, EditingProperty, MenuProperty, LastColourProperty);

    private static readonly IBrush Rim = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
    private static readonly IBrush CentreFill = new SolidColorBrush(Color.FromRgb(0x1C, 0x1F, 0x25));
    private static readonly IPen Gap = new Pen(new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)), 1.5);
    private static readonly IPen HighlightPen = new Pen(new SolidColorBrush(Color.FromRgb(0xE4, 0xE4, 0xE4)), 3);
    private static readonly IPen HighlightPenDark = new Pen(new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14)), 3);

    public override void Render(DrawingContext ctx)
    {
        var c = new Point(Bounds.Width / 2, Bounds.Height / 2);
        double outer = Math.Min(RadialMenuGeometry.OuterRadius(MenuSize), Math.Min(Bounds.Width, Bounds.Height) / 2 - 1);
        double scale = outer / RadialMenuGeometry.OuterRadius(MenuSize);
        double inner = RadialMenuGeometry.InnerRadius(MenuSize) * scale;
        double greyEdge = outer - (outer - inner) * ColourPalette.GreyBand;
        double band = (greyEdge - inner) / ColourPalette.Shades;
        ctx.DrawEllipse(Rim, null, c, outer + 1, outer + 1);
        const double step = 360.0 / ColourPalette.Hues;
        for (int i = 0; i < ColourPalette.Count; i++)
        {
            var (ri, ro, sector) = Ring(i, outer, greyEdge, band);
            ctx.DrawGeometry(Fill(i), Gap, Sector(c, ri, ro, sector * step - step / 2, sector * step + step / 2));
        }
        if (Highlight >= 0 && Highlight < ColourPalette.Count)
        {
            var (ri, ro, sector) = Ring(Highlight, outer, greyEdge, band);
            var (r, g, b) = ColourOf(Highlight);
            var pen = r * 0.299 + g * 0.587 + b * 0.114 > 170 ? HighlightPenDark : HighlightPen;   // visible on light and dark swatches
            ctx.DrawGeometry(null, pen, Sector(c, ri + 1.5, ro - 1.5, sector * step - step / 2 + 0.8, sector * step + step / 2 - 0.8));
        }
        // The centre: the radial menu's own four buttons and pictures (Zak ~20:05): X closes, pencil changes a colour,
        // circular arrow puts the palette's colours back, open hand drags.
        var divider = new Pen(new SolidColorBrush(Color.FromArgb(220, 228, 228, 228)), Math.Max(2, inner / 28));
        RadialMenuPie.DrawCentreQuarters(ctx, c, inner, Highlight, Editing, CentreFill, RadialMenuPie.HighlightFill,
            RadialMenuPie.SelectedFill, RadialMenuPie.Edge, divider);
        DrawDot(ctx, c, inner * ColourPalette.DotRatio, LastColour);
    }

    private static readonly IPen DotRing = new Pen(new SolidColorBrush(Color.FromRgb(0xE4, 0xE4, 0xE4)), 2);
    private static readonly IPen DotRingDark = new Pen(new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14)), 1);

    /// <summary>Zak 19 Nov 20:21: the small middle circle in the last colour picked (an empty ring until a colour is picked). It covers
    /// the crossing of the dividing lines; a light ring outside and a thin dark one inside keep it visible on any colour.</summary>
    private static void DrawDot(DrawingContext ctx, Point c, double r, string? hex)
    {
        IBrush fill = CentreFill;
        if (ColourPalette.IsHex(hex))
            fill = new SolidColorBrush(Color.FromRgb(Convert.ToByte(hex!.Substring(1, 2), 16), Convert.ToByte(hex.Substring(3, 2), 16),
                Convert.ToByte(hex.Substring(5, 2), 16)));
        ctx.DrawEllipse(fill, DotRing, c, r, r);
        if (r > 4) ctx.DrawEllipse(null, DotRingDark, c, r - 1.5, r - 1.5);
    }

    /// <summary>Inner and outer radius and the angle sector of a swatch: the greys on the outside edge, then the 4 shades inwards.</summary>
    private static (double Inner, double Outer, int Sector) Ring(int swatch, double outer, double greyEdge, double band)
    {
        if (ColourPalette.IsGrey(swatch)) return (greyEdge, outer, swatch - ColourPalette.FirstGrey);
        var (hue, shade) = ColourPalette.Split(swatch);
        double ro = greyEdge - shade * band;
        return (ro - band, ro, hue);
    }

    private (byte R, byte G, byte B) ColourOf(int swatch)
    {
        var hex = ColourPalette.HexFor(Menu, swatch);
        return (Convert.ToByte(hex.Substring(1, 2), 16), Convert.ToByte(hex.Substring(3, 2), 16), Convert.ToByte(hex.Substring(5, 2), 16));
    }

    private IBrush Fill(int swatch)
    {
        var (r, g, b) = ColourOf(swatch);
        return new SolidColorBrush(Color.FromRgb(r, g, b));
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
