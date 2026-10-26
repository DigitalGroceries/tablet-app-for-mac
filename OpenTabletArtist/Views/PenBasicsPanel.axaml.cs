using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using OpenTabletArtist.Services;

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
        Localizer.LanguageChanged += OnLanguageChanged;
        DetachedFromLogicalTree += (_, _) => Localizer.LanguageChanged -= OnLanguageChanged;
    }

    void OnLanguageChanged() => Apply();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ForEraserProperty) Apply();
    }

    private void Apply()
    {
        if (DisableTipSwitch is null) return;
        Localizer.EnsureLoaded();
        DisableTipSwitch.IsVisible = !ForEraser;
        DisableEraserSwitch.IsVisible = ForEraser;
        PressureSharedNote.IsVisible = ForEraser;
        MovementSharedNote.IsVisible = ForEraser;
        if (MovementHeading != null) MovementHeading.Text = Localizer.T("tablet.pen.movement");
        if (PositionSmoothingSlider != null) PositionSmoothingSlider.Label = Localizer.T("tablet.pen.position_smoothing");
        if (PressingHeading != null) PressingHeading.Text = "Pressing"; // Windows-only; leave English for now
        InputHeading.Text = ForEraser ? Localizer.T("tablet.eraser.input") : Localizer.T("tablet.pen.pen_input");
        DisableTipSwitch.Label = Localizer.T("tablet.pen.disable_tip");
        DisableEraserSwitch.Label = Localizer.T("tablet.eraser.disable");
        DisablePressureSwitch.Label = Localizer.T("tablet.pen.disable_pressure");
        if (MovementSharedNote != null) MovementSharedNote.Text = Localizer.T("tablet.pen.shared");
        if (PressureSharedNote != null) PressureSharedNote.Text = Localizer.T("tablet.eraser.pressure_shared");
    }
}
