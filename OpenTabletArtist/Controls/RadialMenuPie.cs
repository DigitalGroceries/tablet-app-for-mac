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
            CentreTextProperty, EditingProperty);
    }

    private static readonly IBrush SliceFill = new SolidColorBrush(Color.FromArgb(235, 40, 44, 52));
    private static readonly IBrush EmptyFill = new SolidColorBrush(Color.FromArgb(200, 40, 44, 52));
    private static readonly IBrush HighlightFill = new SolidColorBrush(Color.FromArgb(245, 61, 123, 217));
    private static readonly IBrush SelectedFill = new SolidColorBrush(Color.FromArgb(245, 86, 96, 112));
    private static readonly IBrush CentreFill = new SolidColorBrush(Color.FromArgb(240, 28, 31, 37));
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
            ctx.DrawEllipse(CentreFill, Edge, c, inner, inner);
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
            ctx.DrawGeometry(Highlight == code ? HighlightFill : on ? SelectedFill : CentreFill, Edge, Quarter(c, inner, quarter));
        }
        ctx.DrawLine(divider, new Point(c.X - inner, c.Y), new Point(c.X + inner, c.Y));
        ctx.DrawLine(divider, new Point(c.X, c.Y - inner), new Point(c.X, c.Y + inner));

        double u = inner * 0.5;                       // distance of a quarter's middle from the centre
        double s = inner * 0.30;                      // icon size
        DrawX(ctx, new Point(c.X - u, c.Y - u), s * 0.7);
        DrawPencil(ctx, new Point(c.X + u, c.Y - u), s);
        DrawRestore(ctx, new Point(c.X - u, c.Y + u), s);
        DrawHand(ctx, new Point(c.X + u, c.Y + u), s * 1.5);
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

    private static void DrawX(DrawingContext ctx, Point at, double r)
    {
        var pen = new Pen(TextBrush, Math.Max(2, r / 3.2), lineCap: PenLineCap.Round);
        ctx.DrawLine(pen, new Point(at.X - r, at.Y - r), new Point(at.X + r, at.Y + r));
        ctx.DrawLine(pen, new Point(at.X - r, at.Y + r), new Point(at.X + r, at.Y - r));
    }

    /// <summary>A pencil pointing down-left: a slanted body with a tip. Our own simple drawing.</summary>
    private static void DrawPencil(DrawingContext ctx, Point at, double s)
    {
        // Along the pencil's axis (from the tip at the lower left to the end at the upper right).
        Point P(double along, double across)
        {
            const double k = 0.7071;   // 45 degrees
            return new Point(at.X + (along * k - across * k) * s, at.Y + (-along * k - across * k) * s);
        }
        var body = new StreamGeometry();
        using (var g = body.Open())
        {
            g.BeginFigure(P(-1.15, 0), true);        // the tip
            g.LineTo(P(-0.6, 0.32));
            g.LineTo(P(1.0, 0.32));
            g.LineTo(P(1.0, -0.32));
            g.LineTo(P(-0.6, -0.32));
            g.EndFigure(true);
        }
        ctx.DrawGeometry(TextBrush, null, body);
        var cut = new Pen(CentreFill, Math.Max(1, s / 7));
        ctx.DrawLine(cut, P(-0.6, 0.32), P(-0.6, -0.32));
        ctx.DrawLine(cut, P(0.72, 0.32), P(0.72, -0.32));
    }

    /// <summary>A circular arrow turning back (counter-clockwise) with an arrow head: "restore".</summary>
    private static void DrawRestore(DrawingContext ctx, Point at, double s)
    {
        double r = s * 0.85;
        var pen = new Pen(TextBrush, Math.Max(2, s / 3.2), lineCap: PenLineCap.Round);
        var arc = new StreamGeometry();
        using (var g = arc.Open())
        {
            // From the upper left, clockwise round the right side, to the left: a 270 degree arc.
            g.BeginFigure(PointAt(at, r, -45), false);
            g.ArcTo(PointAt(at, r, -135), new Size(r, r), 0, true, SweepDirection.Clockwise);
            g.EndFigure(false);
        }
        ctx.DrawGeometry(null, pen, arc);
        // The arrow head at the start of the arc (upper left), pointing back along it.
        var tip = PointAt(at, r, -45);
        var head = new StreamGeometry();
        using (var g = head.Open())
        {
            g.BeginFigure(new Point(tip.X + s * 0.15, tip.Y - s * 0.75), true);
            g.LineTo(new Point(tip.X - s * 0.9, tip.Y + s * 0.1));
            g.LineTo(new Point(tip.X + s * 0.85, tip.Y + s * 0.55));
            g.EndFigure(true);
        }
        ctx.DrawGeometry(TextBrush, null, head);
    }

    /// <summary>Our own open-hand "pan" icon (four fingers, a thumb and a palm, all rounded), drawn from simple shapes.
    /// <paramref name="s"/> is the unit: the icon is about 1.1 s tall.</summary>
    private static void DrawHand(DrawingContext ctx, Point at, double s)
    {
        var o = new Point(at.X, at.Y - 0.2 * s);   // y 0 = the top of the palm
        double w = 0.17 * s;
        var pen = new Pen(TextBrush, w, lineCap: PenLineCap.Round);
        Point P(double x, double y) => new(o.X + x * s, o.Y + y * s);
        // fingers: from the palm up to their tips (the middle one is the longest)
        ctx.DrawLine(pen, P(-0.30, 0.05), P(-0.30, -0.38));
        ctx.DrawLine(pen, P(-0.10, 0.05), P(-0.10, -0.55));
        ctx.DrawLine(pen, P(0.10, 0.05), P(0.10, -0.50));
        ctx.DrawLine(pen, P(0.30, 0.05), P(0.30, -0.33));
        // thumb: out to the left and up
        ctx.DrawLine(pen, P(-0.30, 0.28), P(-0.52, 0.02));
        // palm
        ctx.DrawGeometry(TextBrush, null, new RectangleGeometry(new Rect(P(-0.39, 0.0), P(0.39, 0.46)), 0.2 * s, 0.2 * s));
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
