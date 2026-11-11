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
    /// <summary>Slice under the pen (on-screen menu), or <see cref="RadialMenuGeometry.Centre"/> for the X.</summary>
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
        AffectsRender<RadialMenuPie>(LabelsProperty, SetFlagsProperty, SubmenuFlagsProperty, HighlightProperty, SelectedProperty);
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
        return (c, outer, outer * 0.26);
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

        // Centre: the X closes the menu.
        ctx.DrawEllipse(Highlight == RadialMenuGeometry.Centre ? HighlightFill : CentreFill, Edge, c, inner, inner);
        double x = inner * 0.32;
        var xPen = new Pen(TextBrush, Math.Max(2, inner / 10), lineCap: PenLineCap.Round);
        ctx.DrawLine(xPen, new Point(c.X - x, c.Y - x), new Point(c.X + x, c.Y + x));
        ctx.DrawLine(xPen, new Point(c.X - x, c.Y + x), new Point(c.X + x, c.Y - x));
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
