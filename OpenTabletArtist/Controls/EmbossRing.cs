using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Controls;

/// <summary>The bevel all the way round a round menu (the radial menu and the colour palette, on the Pen page and in their
/// own windows). Version 31 test build 2 (Digital Groceries, 27 Nov 2026): by the logic of the light from the top left, not
/// by the mockup's pixels - the round menus are RAISED above the page, like the buttons. A raised disc has a slope ALL the
/// way round: lit along the top left, in shadow along the bottom right; and outside it the thin dark line of its shadow on
/// the page, which is darker than the page everywhere, so the circle is never broken. It is <see cref="Gap"/> larger than
/// the menu on every side, so it is drawn inside its own bounds and is never cut off.</summary>
public sealed class EmbossRing : Control
{
    /// <summary>How far the bevel reaches outside the menu.</summary>
    public const double Gap = 10;
    /// <summary>The slope's width, and the thin line of shadow outside it.</summary>
    public const double Band = 7.5, Edge = 1.6;

    public static readonly StyledProperty<int> MenuSizeProperty = AvaloniaProperty.Register<EmbossRing, int>(nameof(MenuSize), RadialMenuDef.DefaultSize);
    public int MenuSize { get => GetValue(MenuSizeProperty); set => SetValue(MenuSizeProperty, value); }

    public static readonly StyledProperty<IBrush?> BrushProperty = AvaloniaProperty.Register<EmbossRing, IBrush?>(nameof(Brush));
    public IBrush? Brush { get => GetValue(BrushProperty); set => SetValue(BrushProperty, value); }

    public static readonly StyledProperty<IBrush?> EdgeBrushProperty = AvaloniaProperty.Register<EmbossRing, IBrush?>(nameof(EdgeBrush));
    public IBrush? EdgeBrush { get => GetValue(EdgeBrushProperty); set => SetValue(EdgeBrushProperty, value); }

    /// <summary>True: the menu fills the space inside the bevel (the radial menu, whose holder has the menu's own size).
    /// False: the menu has the radius its size says (the colour wheel).</summary>
    public static readonly StyledProperty<bool> FillsProperty = AvaloniaProperty.Register<EmbossRing, bool>(nameof(Fills));
    public bool Fills { get => GetValue(FillsProperty); set => SetValue(FillsProperty, value); }

    static EmbossRing() => AffectsRender<EmbossRing>(MenuSizeProperty, BrushProperty, EdgeBrushProperty, FillsProperty);

    public EmbossRing() => IsHitTestVisible = false;

    /// <summary>The menu's radius inside this control.</summary>
    public double MenuRadius()
    {
        double room = Math.Min(Bounds.Width, Bounds.Height) / 2 - Gap;
        return Fills ? room : Math.Min(RadialMenuGeometry.OuterRadius(MenuSize), room - 1);
    }

    public override void Render(DrawingContext ctx)
    {
        if (Brush is not { } brush) return;
        var c = new Point(Bounds.Width / 2, Bounds.Height / 2);
        double menu = MenuRadius();
        // the slope: from just under the menu's edge outwards; then the thin line of shadow where the page begins
        double slope = menu - 0.5 + Band / 2;
        ctx.DrawEllipse(null, new Pen(brush, Band), c, slope, slope);
        if (EdgeBrush is { } edge)
        {
            double r = menu - 0.5 + Band + Edge / 2;
            ctx.DrawEllipse(null, new Pen(edge, Edge), c, r, r);
        }
    }
}
