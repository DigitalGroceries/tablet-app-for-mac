using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using OpenTabletArtist.ViewModels;

namespace OpenTabletArtist.Views;

/// <summary>Build I: a ring mode's pace. The step box commits on Enter or focus loss (its binding is
/// LostFocus); the sliders commit when released or moved with the keyboard, so nothing is written — and no row
/// rebuilt — mid-typing or mid-drag.</summary>
public partial class RingStepEditor : UserControl
{
    public RingStepEditor()
    {
        InitializeComponent();
        SpeedBox.AddHandler(KeyDownEvent, OnStepBoxKeyDown, RoutingStrategies.Tunnel);
        foreach (var slider in new[] { SpeedSlider })
        {
            slider.AddHandler(PointerReleasedEvent, (_, _) => CommitSlider(slider), RoutingStrategies.Tunnel, handledEventsToo: true);
            slider.AddHandler(PointerCaptureLostEvent, (_, _) => CommitSlider(slider), RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
            slider.AddHandler(KeyUpEvent, (_, _) => CommitSlider(slider), RoutingStrategies.Bubble, handledEventsToo: true);
        }
    }

    private RingModeRow? Row => DataContext as RingModeRow;

    private void OnStepBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return)) return;
        BindingOperations.GetBindingExpressionBase(SpeedBox, TextBox.TextProperty)?.UpdateSource();
        e.Handled = true;
    }

    private void CommitSlider(Slider slider) => Row?.CommitSpeedSlider();
}
