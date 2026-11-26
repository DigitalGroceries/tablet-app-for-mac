using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace OpenTabletArtist.Controls;

/// <summary>V31 (task list L7.15 and L7.25, Digital Groceries' mockup of 25 Nov 2026): every slider has notches under it. A slider
/// draws its notches at its own steps (TickFrequency), which is right for a slider with a few steps (Menu size 1 to 10) and
/// wrong for one that moves in fine steps: Transparency snaps to every 1 of 0 to 60 and Position smoothing to every 0.1 of
/// 0 to 10, which would be 61 and 101 notches. <c>SliderNotches.Every</c> says how far apart the drawn notches are, and
/// leaves the steps the slider moves in alone.</summary>
public static class SliderNotches
{
    public static readonly AttachedProperty<double> EveryProperty =
        AvaloniaProperty.RegisterAttached<Slider, double>("Every", typeof(SliderNotches), 0d);

    public static double GetEvery(Slider slider) => slider.GetValue(EveryProperty);
    public static void SetEvery(Slider slider, double value) => slider.SetValue(EveryProperty, value);

    static SliderNotches()
    {
        EveryProperty.Changed.AddClassHandler<Slider>((slider, _) =>
        {
            slider.TemplateApplied -= OnTemplateApplied;
            slider.TemplateApplied += OnTemplateApplied;
            Apply(slider);
        });
    }

    static void OnTemplateApplied(object? sender, TemplateAppliedEventArgs e)
    {
        if (sender is not Slider slider) return;
        double every = GetEvery(slider);
        if (every <= 0) return;
        foreach (var name in new[] { "TopTickBar", "BottomTickBar", "LeftTickBar", "RightTickBar" })
            if (e.NameScope.Find<TickBar>(name) is { } bar) bar.TickFrequency = every;   // a value of its own: it wins over the template's
    }

    static void Apply(Slider slider)
    {
        double every = GetEvery(slider);
        if (every <= 0) return;
        foreach (var bar in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(slider).OfType<TickBar>()) bar.TickFrequency = every;
    }
}
