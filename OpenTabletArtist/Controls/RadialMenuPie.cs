using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Controls;

/// <summary>
/// Draws a radial menu ring (TASKS #42): 8 slices, slice 0 at the top then clockwise, and the centre X.
/// Used by the on-screen menu (Services/RadialMenuOverlay) and by the menu editor, so both look the same.
/// Our own drawing: no Wacom artwork. Empty slices are drawn dimmer and without text (and never crash on a
/// missing label: Wacom Center 6.4.15 crashed on exactly that, see the 9 Nov comparison).
/// </summary>
public sealed class RadialMenuPie : Control, Avalonia.Rendering.ICustomHitTest
{
    public static readonly StyledProperty<IReadOnlyList<string>?> LabelsProperty =
        AvaloniaProperty.Register<RadialMenuPie, IReadOnlyList<string>?>(nameof(Labels));
    public static readonly StyledProperty<IReadOnlyList<bool>?> SetFlagsProperty =
        AvaloniaProperty.Register<RadialMenuPie, IReadOnlyList<bool>?>(nameof(SetFlags));
    public static readonly StyledProperty<IReadOnlyList<bool>?> SubmenuFlagsProperty =
        AvaloniaProperty.Register<RadialMenuPie, IReadOnlyList<bool>?>(nameof(SubmenuFlags));
    public static readonly StyledProperty<int> HighlightProperty =
        AvaloniaProperty.Register<RadialMenuPie, int>(nameof(Highlight), -3);
    public static readonly StyledProperty<int> SelectedProperty =
        AvaloniaProperty.Register<RadialMenuPie, int>(nameof(Selected), -3);
    public static readonly StyledProperty<int> TransparencyProperty =
        AvaloniaProperty.Register<RadialMenuPie, int>(nameof(Transparency), RadialMenuDef.DefaultTransparency);
    public static readonly StyledProperty<bool> InteractiveProperty =
        AvaloniaProperty.Register<RadialMenuPie, bool>(nameof(Interactive));

    /// <summary>Slice labels, top then clockwise. Missing entries count as empty.</summary>
    public IReadOnlyList<string>? Labels { get => GetValue(LabelsProperty); set => SetValue(LabelsProperty, value); }
    /// <summary>Which slices do something (the rest are drawn dimmer).</summary>
    public IReadOnlyList<bool>? SetFlags { get => GetValue(SetFlagsProperty); set => SetValue(SetFlagsProperty, value); }
    /// <summary>Which slices open a submenu (drawn with a small arrow).</summary>
    public IReadOnlyList<bool>? SubmenuFlags { get => GetValue(SubmenuFlagsProperty); set => SetValue(SubmenuFlagsProperty, value); }
    /// <summary>Slice under the pen (on-screen menu), or one of the centre buttons (<see cref="RadialMenuGeometry.Centre"/> X, <see cref="RadialMenuGeometry.Edit"/>, <see cref="RadialMenuGeometry.Restore"/>, <see cref="RadialMenuGeometry.Hand"/>).</summary>
    public int Highlight { get => GetValue(HighlightProperty); set => SetValue(HighlightProperty, value); }
    /// <summary>Slice picked in the editor.</summary>
    public int Selected { get => GetValue(SelectedProperty); set => SetValue(SelectedProperty, value); }
    /// <summary>0 (solid) to 100 (clear): how see-through the slice and centre backgrounds are. Labels, divider lines,
    /// icons and the highlighted slice are not affected.</summary>
    public int Transparency { get => GetValue(TransparencyProperty); set => SetValue(TransparencyProperty, value); }
    /// <summary>Editor only: clicking a slice raises <see cref="SliceClicked"/>.</summary>
    public bool Interactive { get => GetValue(InteractiveProperty); set => SetValue(InteractiveProperty, value); }

    public event Action<int>? SliceClicked;

    /// <summary>Clickable inside the round menu only (the corners of the square stay see-through).</summary>
    public bool HitTest(Point point)
    {
        var (c, outer, _) = Layout();
        double dx = point.X - c.X, dy = point.Y - c.Y;
        return dx * dx + dy * dy <= outer * outer;
    }

    static RadialMenuPie()
    {
        AffectsRender<RadialMenuPie>(LabelsProperty, SetFlagsProperty, SubmenuFlagsProperty, HighlightProperty, SelectedProperty,
            CentreTextProperty, EditingProperty, TransparencyProperty);
    }

    // The slice and centre backgrounds follow Transparency (0 = solid, 255 alpha); an empty slice is a little clearer,
    // the highlight, the selected slice, text, lines and icons never are.
    private IBrush SliceFill => Back(40, 44, 52, 1.0);
    private IBrush EmptyFill => Back(40, 44, 52, 0.85);
    private static readonly IBrush PencilCut = new SolidColorBrush(Color.FromRgb(28, 31, 37));
    private IBrush CentreBack => Back(28, 31, 37, 1.0);
    private IBrush Back(byte r, byte g, byte b, double k)
    {
        double opaque = 1 - Math.Clamp(Transparency, RadialMenuDef.MinTransparency, RadialMenuDef.MaxTransparency) / 100.0;
        return new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * opaque * k), r, g, b));
    }
    private static readonly IBrush HighlightFill = new SolidColorBrush(Color.FromArgb(245, 61, 123, 217));
    private static readonly IBrush SelectedFill = new SolidColorBrush(Color.FromArgb(245, 86, 96, 112));
    private static readonly IPen Edge = new Pen(new SolidColorBrush(Color.FromArgb(160, 228, 228, 228)), 1.5);
    private static readonly IBrush TextBrush = new SolidColorBrush(Color.FromRgb(228, 228, 228));   // the app never goes brighter than #E4
    private static readonly IBrush TextMuted = new SolidColorBrush(Color.FromArgb(150, 220, 220, 220));

    /// <summary>Radii for this control's size, in the same ratio as <see cref="RadialMenuGeometry"/>.</summary>
    private (Point C, double Outer, double Inner) Layout()
    {
        var c = new Point(Bounds.Width / 2, Bounds.Height / 2);
        double outer = Math.Max(10, Math.Min(Bounds.Width, Bounds.Height) / 2 - 2);
        return (c, outer, outer * RadialMenuGeometry.InnerRatio);
    }

    public override void Render(DrawingContext ctx)
    {
        var (c, outer, inner) = Layout();
        int n = RadialMenuDef.SliceCount;
        double step = 360.0 / n;
        for (int i = 0; i < n; i++)
        {
            bool set = Flag(SetFlags, i);
            var fill = i == Highlight ? HighlightFill : i == Selected ? SelectedFill : set ? SliceFill : EmptyFill;
            ctx.DrawGeometry(fill, Edge, Wedge(c, inner, outer, i * step - step / 2, i * step + step / 2));

            var label = Labels != null && i < Labels.Count ? Labels[i] ?? "" : "";
            if (Flag(SubmenuFlags, i) && label.Length > 0) label += " ›";
            if (label.Length == 0) continue;
            double mid = (inner + outer) / 2;
            var p = PointAt(c, mid, i * step);
            double maxW = Math.Max(30, (outer - inner) * 0.95);
            double size = Math.Clamp(outer / 11, 10, 18);
            var ft = new FormattedText(label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                Typeface.Default, size, set ? TextBrush : TextMuted)
            {
                MaxTextWidth = maxW, MaxLineCount = 2, TextAlignment = TextAlignment.Center, Trimming = TextTrimming.CharacterEllipsis,
            };
            ctx.DrawText(ft, new Point(p.X - maxW / 2, p.Y - ft.Height / 2));
        }

        // Centre: four quarter buttons split by a cross. Top left: the X closes the menu. Top right: the pencil, then tap
        // a slice to choose what it does. Bottom left: the circular arrow restores the default slices. Bottom right: the
        // open hand, press and move to drag the menu somewhere else. While the Yes / No question shows, the centre is
        // just the question.
        var divider = new Pen(new SolidColorBrush(Color.FromArgb(220, 228, 228, 228)), Math.Max(2, inner / 28));
        if (!string.IsNullOrEmpty(CentreText))
        {
            ctx.DrawEllipse(CentreBack, Edge, c, inner, inner);
            var q = new FormattedText(CentreText!, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default,
                Math.Clamp(inner / 5, 10, 16), TextBrush)
            { MaxTextWidth = inner * 1.6, MaxLineCount = 4, TextAlignment = TextAlignment.Center, Trimming = TextTrimming.CharacterEllipsis };
            ctx.DrawText(q, new Point(c.X - inner * 0.8, c.Y - q.Height / 2));
            return;
        }
        for (int quarter = 0; quarter < 4; quarter++)
        {
            int code = QuarterCodes[quarter];
            bool on = code == RadialMenuGeometry.Edit && Editing;
            ctx.DrawGeometry(Highlight == code ? HighlightFill : on ? SelectedFill : CentreBack, Edge, Quarter(c, inner, quarter));
        }
        ctx.DrawLine(divider, new Point(c.X - inner, c.Y), new Point(c.X + inner, c.Y));
        ctx.DrawLine(divider, new Point(c.X, c.Y - inner), new Point(c.X, c.Y + inner));

        // TASKS #59 (Zak 18:21): the four icons have the same visual size (one square box, QuarterIconBox of the inner
        // radius) and sit centred on the centroid of their quarter, away from the dividing lines. Each icon is drawn in
        // its own units, its real bounds (strokes included) are measured, and it is then scaled and moved so those bounds
        // fill the box centred on the quarter's centroid.
        double box = inner * QuarterIconBox;
        foreach (var quarter in new[] { 0, 1, 2, 3 })
        {
            var at = QuarterCentroid(c, inner, quarter);
            var icon = quarter switch { 0 => XIcon(), 1 => PencilIcon(), 2 => HandIcon(), _ => RestoreIcon() };
            DrawIcon(ctx, icon, at, box);
        }
    }

    /// <summary>Side of the square every centre icon fits in, as a share of the centre disc's radius.</summary>
    internal const double QuarterIconBox = 0.46;

    /// <summary>The centroid of a quarter of the centre disc: 4r / (3 pi) from each of the two dividing lines.
    /// Quarters: 0 top left, 1 top right, 2 bottom right, 3 bottom left (the order of <c>QuarterCodes</c>).</summary>
    internal static Point QuarterCentroid(Point c, double r, int quarter)
    {
        double d = 4 * r / (3 * Math.PI);
        return quarter switch
        {
            0 => new Point(c.X - d, c.Y - d),
            1 => new Point(c.X + d, c.Y - d),
            2 => new Point(c.X + d, c.Y + d),
            _ => new Point(c.X - d, c.Y + d),
        };
    }

    private static readonly int[] QuarterCodes =
    {
        RadialMenuGeometry.Centre, RadialMenuGeometry.Edit, RadialMenuGeometry.Hand, RadialMenuGeometry.Restore,
    };

    /// <summary>The Yes / No question in the centre (null: the four buttons).</summary>
    public string? CentreText { get => GetValue(CentreTextProperty); set => SetValue(CentreTextProperty, value); }
    public static readonly StyledProperty<string?> CentreTextProperty =
        AvaloniaProperty.Register<RadialMenuPie, string?>(nameof(CentreText));

    /// <summary>The pencil has been tapped: it is drawn as pressed until a slice is chosen.</summary>
    public bool Editing { get => GetValue(EditingProperty); set => SetValue(EditingProperty, value); }
    public static readonly StyledProperty<bool> EditingProperty =
        AvaloniaProperty.Register<RadialMenuPie, bool>(nameof(Editing));

    // ---- Centre icons: drawn in their own units, measured, then fitted into the same box (see Render) -------------------

    /// <summary>One piece of an icon: a round-capped line, a filled polygon, an open arc or a rounded rectangle.</summary>
    private sealed class IconPart
    {
        public Point[] Points = Array.Empty<Point>();
        public double Stroke;                              // 0 = filled
        public bool Dark;                                  // drawn in the dark centre colour (a cut inside the icon)
        public double CornerRadius;                        // rounded rectangle (Points = two corners)
        public double ArcRadius; public Point ArcCentre;   // open arc (Points = the arc sampled, drawn as a polyline)
    }

    private sealed class Icon
    {
        public readonly System.Collections.Generic.List<IconPart> Parts = new();
        public Rect Bounds
        {
            get
            {
                double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
                foreach (var part in Parts)
                {
                    if (part.Dark) continue;
                    double h = part.Stroke / 2;
                    foreach (var p in part.Points)
                    {
                        x0 = Math.Min(x0, p.X - h); y0 = Math.Min(y0, p.Y - h);
                        x1 = Math.Max(x1, p.X + h); y1 = Math.Max(y1, p.Y + h);
                    }
                }
                return new Rect(x0, y0, x1 - x0, y1 - y0);
            }
        }
        public Icon Line(Point a, Point b, double w) { Parts.Add(new IconPart { Points = new[] { a, b }, Stroke = w }); return this; }
        public Icon Poly(params Point[] pts) { Parts.Add(new IconPart { Points = pts }); return this; }
        public Icon Cut(Point a, Point b, double w) { Parts.Add(new IconPart { Points = new[] { a, b }, Stroke = w, Dark = true }); return this; }
        public Icon RoundRect(Point a, Point b, double radius) { Parts.Add(new IconPart { Points = new[] { a, b }, CornerRadius = radius }); return this; }
        public Icon Arc(Point centre, double radius, double fromDeg, double toDeg, double w)
        {
            // Clockwise from fromDeg to toDeg (degrees clockwise from the top); sampled every 5 degrees for the bounds.
            var pts = new System.Collections.Generic.List<Point>();
            for (double d = fromDeg; d < toDeg; d += 5) pts.Add(PointAt(centre, radius, d));
            pts.Add(PointAt(centre, radius, toDeg));
            Parts.Add(new IconPart { Points = pts.ToArray(), Stroke = w, ArcRadius = radius, ArcCentre = centre });
            return this;
        }
    }

    private static void DrawIcon(DrawingContext ctx, Icon icon, Point at, double box)
    {
        var b = icon.Bounds;
        double k = box / Math.Max(b.Width, b.Height);
        Point T(Point p) => new(at.X + (p.X - b.Center.X) * k, at.Y + (p.Y - b.Center.Y) * k);
        foreach (var part in icon.Parts)
        {
            var brush = part.Dark ? PencilCut : TextBrush;
            if (part.ArcRadius > 0)
            {
                var pen = new Pen(brush, part.Stroke * k, lineCap: PenLineCap.Round);
                var first = part.Points[0]; var last = part.Points[^1];
                var g = new StreamGeometry();
                using (var gc = g.Open())
                {
                    gc.BeginFigure(T(first), false);
                    // Our arcs are 270 degrees, so always the large arc.
                    gc.ArcTo(T(last), new Size(part.ArcRadius * k, part.ArcRadius * k), 0, true, SweepDirection.Clockwise);
                    gc.EndFigure(false);
                }
                ctx.DrawGeometry(null, pen, g);
            }
            else if (part.CornerRadius > 0)
            {
                ctx.DrawGeometry(brush, null, new RectangleGeometry(new Rect(T(part.Points[0]), T(part.Points[1])), part.CornerRadius * k, part.CornerRadius * k));
            }
            else if (part.Stroke > 0)
            {
                ctx.DrawLine(new Pen(brush, part.Stroke * k, lineCap: PenLineCap.Round), T(part.Points[0]), T(part.Points[1]));
            }
            else
            {
                var g = new StreamGeometry();
                using (var gc = g.Open())
                {
                    gc.BeginFigure(T(part.Points[0]), true);
                    for (int i = 1; i < part.Points.Length; i++) gc.LineTo(T(part.Points[i]));
                    gc.EndFigure(true);
                }
                ctx.DrawGeometry(brush, null, g);
            }
        }
    }

    /// <summary>The X that closes the menu.</summary>
    private static Icon XIcon() => new Icon()
        .Line(new Point(-1, -1), new Point(1, 1), 0.4)
        .Line(new Point(-1, 1), new Point(1, -1), 0.4);

    /// <summary>A pencil pointing down-left: a slanted body with a tip. Our own simple drawing.</summary>
    private static Icon PencilIcon()
    {
        // Along the pencil's axis (from the tip at the lower left to the end at the upper right), turned 45 degrees.
        Point P(double along, double across) { const double k = 0.7071; return new Point((along * k - across * k), (-along * k - across * k)); }
        return new Icon()
            .Poly(P(-1.15, 0), P(-0.6, 0.32), P(1.0, 0.32), P(1.0, -0.32), P(-0.6, -0.32))
            .Cut(P(-0.6, 0.32), P(-0.6, -0.32), 0.12)     // always dark: part of the icon, not the background
            .Cut(P(0.72, 0.32), P(0.72, -0.32), 0.12);
    }

    /// <summary>A circular arrow turning back (counter-clockwise) with an arrow head: "restore".</summary>
    private static Icon RestoreIcon()
    {
        const double r = 1.0, w = 0.34;
        var icon = new Icon().Arc(new Point(0, 0), r, -45, 225, w);   // 270 degrees: upper left, clockwise round to the lower left
        // The arrow head at the start of the arc (upper left), pointing back along it (down and to the left).
        var tip = PointAt(new Point(0, 0), r, -45);
        var dir = new Point(-0.7071, 0.7071);                          // counter-clockwise tangent there
        var side = new Point(0.7071, 0.7071);
        const double halfBase = 0.48, length = 0.62;
        icon.Poly(new Point(tip.X + side.X * halfBase, tip.Y + side.Y * halfBase),
                  new Point(tip.X + dir.X * length, tip.Y + dir.Y * length),
                  new Point(tip.X - side.X * halfBase, tip.Y - side.Y * halfBase));
        return icon;
    }

    /// <summary>Our own open-hand "pan" icon (four fingers, a thumb and a palm, all rounded), drawn from simple shapes.</summary>
    private static Icon HandIcon()
    {
        const double w = 0.17;
        Point P(double x, double y) => new(x, y);
        return new Icon()
            .Line(P(-0.30, 0.05), P(-0.30, -0.38), w).Line(P(-0.10, 0.05), P(-0.10, -0.55), w)
            .Line(P(0.10, 0.05), P(0.10, -0.50), w).Line(P(0.30, 0.05), P(0.30, -0.33), w)
            .Line(P(-0.30, 0.28), P(-0.52, 0.02), w)                       // thumb: out to the left and up
            .RoundRect(P(-0.39, 0.0), P(0.39, 0.46), 0.2);                 // palm
    }

    /// <summary>One quarter of the centre disc: 0 top left, 1 top right, 2 bottom right, 3 bottom left.</summary>
    private static Geometry Quarter(Point c, double r, int quarter)
    {
        // Angles clockwise from the top: top right is 0..90, bottom right 90..180, bottom left 180..270, top left 270..360.
        double from = quarter switch { 0 => 270, 1 => 0, 2 => 90, _ => 180 };
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(c, true);
            ctx.LineTo(PointAt(c, r, from));
            ctx.ArcTo(PointAt(c, r, from + 90), new Size(r, r), 0, false, SweepDirection.Clockwise);
            ctx.EndFigure(true);
        }
        return g;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!Interactive) return;
        var (c, outer, inner) = Layout();
        var p = e.GetPosition(this);
        double dx = p.X - c.X, dy = p.Y - c.Y, r = Math.Sqrt(dx * dx + dy * dy);
        if (r <= inner || r > outer) return;
        // Same maths as the driver, at this control's scale.
        double deg = Math.Atan2(dx, -dy) * 180 / Math.PI;
        if (deg < 0) deg += 360;
        double step = 360.0 / RadialMenuDef.SliceCount;
        int slice = (int)Math.Floor((deg + step / 2) / step) % RadialMenuDef.SliceCount;
        SliceClicked?.Invoke(slice);
        e.Handled = true;
    }

    private static bool Flag(IReadOnlyList<bool>? flags, int i) => flags != null && i < flags.Count && flags[i];

    /// <summary>Point at <paramref name="radius"/> and <paramref name="deg"/> clockwise from the top.</summary>
    private static Point PointAt(Point c, double radius, double deg)
    {
        double rad = deg * Math.PI / 180;
        return new Point(c.X + radius * Math.Sin(rad), c.Y - radius * Math.Cos(rad));
    }

    private static Geometry Wedge(Point c, double inner, double outer, double fromDeg, double toDeg)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(PointAt(c, inner, fromDeg), true);
            ctx.LineTo(PointAt(c, outer, fromDeg));
            ctx.ArcTo(PointAt(c, outer, toDeg), new Size(outer, outer), 0, false, SweepDirection.Clockwise);
            ctx.LineTo(PointAt(c, inner, toDeg));
            ctx.ArcTo(PointAt(c, inner, fromDeg), new Size(inner, inner), 0, false, SweepDirection.CounterClockwise);
            ctx.EndFigure(true);
        }
        return g;
    }
}
