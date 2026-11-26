using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace OpenTabletArtist.Controls;

/// <summary>V31 (task list L7.14, L7.15, L7.25): the notches under a slider. It stands in for the framework's TickBar, which
/// draws lines 1 wide wherever they fall: between two picture dots such a line is smeared over both at half strength, and
/// the notches under the long sliders were almost invisible (Digital Groceries, 26 Nov 2026). Here every notch is a small bar of a
/// set thickness that starts on a whole picture dot, so all notches are equally sharp.</summary>
public class NotchBar : Control
{
    public static readonly StyledProperty<IBrush?> FillProperty = AvaloniaProperty.Register<NotchBar, IBrush?>(nameof(Fill));
    public IBrush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }

    public static readonly StyledProperty<double> MinimumProperty = AvaloniaProperty.Register<NotchBar, double>(nameof(Minimum));
    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }

    public static readonly StyledProperty<double> MaximumProperty = AvaloniaProperty.Register<NotchBar, double>(nameof(Maximum), 100);
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }

    public static readonly StyledProperty<double> TickFrequencyProperty = AvaloniaProperty.Register<NotchBar, double>(nameof(TickFrequency));
    public double TickFrequency { get => GetValue(TickFrequencyProperty); set => SetValue(TickFrequencyProperty, value); }

    public static readonly StyledProperty<AvaloniaList<double>?> TicksProperty = AvaloniaProperty.Register<NotchBar, AvaloniaList<double>?>(nameof(Ticks));
    public AvaloniaList<double>? Ticks { get => GetValue(TicksProperty); set => SetValue(TicksProperty, value); }

    public static readonly StyledProperty<Orientation> OrientationProperty = AvaloniaProperty.Register<NotchBar, Orientation>(nameof(Orientation));
    public Orientation Orientation { get => GetValue(OrientationProperty); set => SetValue(OrientationProperty, value); }

    public static readonly StyledProperty<bool> IsDirectionReversedProperty = AvaloniaProperty.Register<NotchBar, bool>(nameof(IsDirectionReversed));
    public bool IsDirectionReversed { get => GetValue(IsDirectionReversedProperty); set => SetValue(IsDirectionReversedProperty, value); }

    /// <summary>The handle's bounds: half of it is kept free at each end, so a notch lies under the handle's line.</summary>
    public static readonly StyledProperty<Rect> ReservedSpaceProperty = AvaloniaProperty.Register<NotchBar, Rect>(nameof(ReservedSpace));
    public Rect ReservedSpace { get => GetValue(ReservedSpaceProperty); set => SetValue(ReservedSpaceProperty, value); }

    /// <summary>How thick a notch is, in points.</summary>
    public static readonly StyledProperty<double> ThicknessProperty = AvaloniaProperty.Register<NotchBar, double>(nameof(Thickness), 1.6);
    public double Thickness { get => GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }

    /// <summary>Digital Groceries, 26 Nov 2026: the notches divide the length of a big slider into tenths - 11 notches from end
    /// to end, whatever steps the slider moves in. Off: a notch at each listed value or each TickFrequency.</summary>
    public static readonly StyledProperty<bool> TenthsProperty = AvaloniaProperty.Register<NotchBar, bool>(nameof(Tenths));
    public bool Tenths { get => GetValue(TenthsProperty); set => SetValue(TenthsProperty, value); }

    /// <summary>Set by SliderNotches.Every: how far apart the notches are when that is not the slider's own step. 0: not set.</summary>
    public static readonly StyledProperty<double> EveryProperty = AvaloniaProperty.Register<NotchBar, double>(nameof(Every));
    public double Every { get => GetValue(EveryProperty); set => SetValue(EveryProperty, value); }

    static NotchBar() => AffectsRender<NotchBar>(FillProperty, MinimumProperty, MaximumProperty, TickFrequencyProperty, TicksProperty,
        OrientationProperty, IsDirectionReversedProperty, ReservedSpaceProperty, ThicknessProperty, TenthsProperty, EveryProperty);

    public NotchBar() => IsHitTestVisible = false;

    /// <summary>The values that get a notch: every tenth of the length, or the listed ones, or every step from the minimum
    /// and both ends.</summary>
    public IReadOnlyList<double> NotchValues()
    {
        var values = new List<double>();
        double min = Minimum, max = Maximum;
        if (!(max > min)) return values;
        if (Tenths) { for (int i = 0; i <= 10; i++) values.Add(min + (max - min) * i / 10); return values; }
        if (Ticks is { Count: > 0 } listed) { foreach (var t in listed) if (t >= min && t <= max) values.Add(t); return values; }
        double step = Every > 0 ? Every : TickFrequency;
        if (step > 0 && (max - min) / step <= 400)
            for (double v = min; v < max - step * 1e-6; v += step) values.Add(v);
        else values.Add(min);
        values.Add(max);
        return values;
    }

    public override void Render(DrawingContext dc)
    {
        if (Fill is not { } fill) return;
        bool across = Orientation == Orientation.Horizontal;
        double length = across ? Bounds.Width : Bounds.Height;
        double reserved = across ? ReservedSpace.Width : ReservedSpace.Height;
        double travel = length - reserved;
        if (travel <= 0) return;
        var top = TopLevel.GetTopLevel(this);
        double scale = top?.RenderScaling ?? 1;
        var origin = top != null ? this.TranslatePoint(default, top) ?? default : default;
        double start = across ? origin.X : origin.Y;
        double thick = Math.Max(1, Math.Round(Thickness * scale)) / scale;   // a whole number of picture dots
        double min = Minimum, range = Maximum - Minimum;
        foreach (var v in NotchValues())
        {
            double at = reserved / 2 + (v - min) / range * travel;
            if (across ? IsDirectionReversed : !IsDirectionReversed) at = length - at;   // an upright slider counts upwards
            double edge = Math.Round((start + at - thick / 2) * scale) / scale - start;   // starts on a whole picture dot
            dc.FillRectangle(fill, across ? new Rect(edge, 0, thick, Bounds.Height) : new Rect(0, edge, Bounds.Width, thick));
        }
    }
}
