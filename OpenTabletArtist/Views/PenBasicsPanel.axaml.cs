using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.Views;

/// <summary>Movement, position smoothing, pressing (Windows) and the pen-input switches, on Tablet › Pen (R14).
/// R18: <see cref="ForEraser"/> turns it into the eraser's copy: "Disable eraser" instead of "Disable pen tip".
/// V29 #180: no "Shared with Pen" notes any more: with Tip Feel | Eraser Feel on one page they pushed the box down when the
/// eraser's settings were chosen (Grok's review 21 Nov); the manual says which settings both ends share.</summary>
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
        if (MovementHeading != null) MovementHeading.Text = Localizer.T("tablet.pen.movement");
        if (PositionSmoothingCaption != null)
        {
            PositionSmoothingCaption.Text = Localizer.T("tablet.pen.position_smoothing");
            Avalonia.Automation.AutomationProperties.SetName(PositionSmoothingSlider, PositionSmoothingCaption.Text);
            PositionSmoothingNote.Text = Localizer.Te("tablet.pen.position_smoothing_note",
                "Steadies wobbly lines, at a little lag. 0 is off, 10 is the strongest.");
        }
        if (SmootherCurvesSwitch != null)
        {
            SmootherCurvesSwitch.Label = Localizer.Te("tablet.pen.smoother_curves", "Smoother fast curves");
            ToolTip.SetTip(SmootherCurvesSwitch, Localizer.Te("tablet.pen.smoother_curves_tip",
                "Adds smooth in-between points so fast curves don't show flat pieces in drawing apps. About 5 ms of extra delay."));
        }
        if (PressingHeading != null) PressingHeading.Text = "Pressing"; // Windows-only; leave English for now
        InputHeading.Text = ForEraser ? Localizer.T("tablet.eraser.input") : Localizer.T("tablet.pen.pen_input");
        DisableTipSwitch.Label = Localizer.T("tablet.pen.disable_tip");
        DisableEraserSwitch.Label = Localizer.T("tablet.eraser.disable");
        DisablePressureSwitch.Label = Localizer.T("tablet.pen.disable_pressure");
    }
}
