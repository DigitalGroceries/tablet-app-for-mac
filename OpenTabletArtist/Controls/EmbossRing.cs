using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Controls;

/// <summary>V31 (task list L7.22, Digital Groceries' mockup of 25 Nov 2026): the colour palette's picture is embossed - a thin ring just
/// outside the wheel, light at the top left and dark at the bottom right (the page's EmbossRingBrush). It is a control of its
/// own, 6 larger than the wheel on every side, so the ring is drawn inside its own bounds and is never cut off.</summary>
public sealed class EmbossRing : Control
{
    /// <summary>How far outside the wheel the ring's middle lies.</summary>
    public const double Gap = 6;

    public static readonly StyledProperty<int> MenuSizeProperty = AvaloniaProperty.Register<EmbossRing, int>(nameof(MenuSize), RadialMenuDef.DefaultSize);
    public int MenuSize { get => GetValue(MenuSizeProperty); set => SetValue(MenuSizeProperty, value); }

    public static readonly StyledProperty<IBrush?> BrushProperty = AvaloniaProperty.Register<EmbossRing, IBrush?>(nameof(Brush));
    public IBrush? Brush { get => GetValue(BrushProperty); set => SetValue(BrushProperty, value); }

    static EmbossRing() => AffectsRender<EmbossRing>(MenuSizeProperty, BrushProperty);

    public EmbossRing() => IsHitTestVisible = false;

    public override void Render(DrawingContext ctx)
    {
        if (Brush is not { } brush) return;
        var c = new Point(Bounds.Width / 2, Bounds.Height / 2);
        // the wheel's own radius (ColourPaletteWheel.Render), in a square that is Gap larger on every side
        double wheel = Math.Min(RadialMenuGeometry.OuterRadius(MenuSize), Math.Min(Bounds.Width, Bounds.Height) / 2 - Gap - 1);
        ctx.DrawEllipse(null, new Pen(brush, 2.5), c, wheel + Gap, wheel + Gap);
    }
}
