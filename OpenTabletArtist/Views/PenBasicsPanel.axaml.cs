using Avalonia;
using Avalonia.Controls;

namespace OpenTabletArtist.Views;

/// <summary>Movement, position smoothing, pressing (Windows) and the pen-input switches, on Tablet › Pen (R14).
/// R18: <see cref="ForEraser"/> turns it into the Eraser tab's copy: "Disable eraser" instead of "Disable pen
/// tip", and "Shared with Pen" notes on the settings the driver has only once.</summary>
public partial class PenBasicsPanel : UserControl
{
    public static readonly StyledProperty<bool> ForEraserProperty =
        AvaloniaProperty.Register<PenBasicsPanel, bool>(nameof(ForEraser));

    public bool ForEraser { get => GetValue(ForEraserProperty); set => SetValue(ForEraserProperty, value); }

    public PenBasicsPanel()
    {
        InitializeComponent();
        Apply();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ForEraserProperty) Apply();
    }

    private void Apply()
    {
        if (DisableTipSwitch is null) return;
        DisableTipSwitch.IsVisible = !ForEraser;
        DisableEraserSwitch.IsVisible = ForEraser;
        PressureSharedNote.IsVisible = ForEraser;
        MovementSharedNote.IsVisible = ForEraser;
        InputHeading.Text = ForEraser ? "Eraser input" : "Pen input";
    }
}
