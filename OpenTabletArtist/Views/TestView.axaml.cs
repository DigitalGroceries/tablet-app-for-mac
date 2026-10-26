using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Interactivity;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Helpers;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;

namespace OpenTabletArtist.Views;

public partial class TestView : UserControl
{
    private TestViewModel? _vm;

    public TestView()
    {
        InitializeComponent();
        // The pen position always comes from the daemon, never the OS pointer (#scribble-driver-only).
        PaintCanvas.IgnorePointer = true;
        DataContextChanged += OnDataContextChanged;
        ApplyLanguage();
        Localizer.LanguageChanged += ApplyLanguage;
        DetachedFromLogicalTree += (_, _) => Localizer.LanguageChanged -= ApplyLanguage;
    }

    void ApplyLanguage()
    {
        Localizer.EnsureLoaded();
        if (LblPressureCurve != null) LblPressureCurve.Text = Localizer.T("scribble.pressure_curve");
        if (LblPressureSmoothing != null) LblPressureSmoothing.Text = Localizer.T("scribble.pressure_smoothing");
        if (LblPositionSmoothing != null) LblPositionSmoothing.Text = Localizer.T("scribble.position_smoothing");
        if (LblPointerOnly != null) LblPointerOnly.Text = Localizer.T("scribble.pointer_only_note");
        if (LblBrush != null) LblBrush.Text = Localizer.T("scribble.brush");
        if (BtnClear != null) BtnClear.Content = Localizer.T("scribble.clear");
        if (BtnCopy != null) BtnCopy.Content = Localizer.T("scribble.copy");
        if (ChkMarkPositions != null) ChkMarkPositions.Content = Localizer.T("scribble.mark_positions");
        if (BtnRecord != null) BtnRecord.Content = Localizer.T("scribble.record");
        if (BtnStop != null) BtnStop.Content = Localizer.T("scribble.stop");
        if (LblCanvas != null) LblCanvas.Text = Localizer.T("scribble.canvas");
        if (LblRaw != null) LblRaw.Text = Localizer.T("scribble.raw");
        if (LblTilt != null) LblTilt.Text = Localizer.T("scribble.tilt");
        if (LblPressure != null) LblPressure.Text = Localizer.T("scribble.pressure");
        if (LblTwist != null) LblTwist.Text = Localizer.T("scribble.twist");
        if (LblAzAl != null) LblAzAl.Text = Localizer.T("scribble.azal");
        if (LblHover != null) LblHover.Text = Localizer.T("scribble.hover");
        if (LblRate != null) LblRate.Text = Localizer.T("scribble.rate");
        if (LblReports != null) LblReports.Text = Localizer.T("scribble.reports");
        if (UsePenOnScreenButton != null) UsePenOnScreenButton.Content = Localizer.T("scribble.use_pen_on_screen");
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm != null)
        {
            _vm.DriverSample -= OnDriverSample;
            _vm.ClearRequested -= OnClearRequested;
        }
        _vm = DataContext as TestViewModel;
        if (_vm != null)
        {
            _vm.DriverSample += OnDriverSample;
            _vm.ClearRequested += OnClearRequested;
        }
    }

    // Driver stream: update readouts; in an Absolute mode, map the raw tablet position to the canvas
    // and paint under the pen. In a non-mappable (Relative) mode the canvas is disabled (note shown).
    private void OnDriverSample(PenSample s, double pressure)
    {
        if (_vm is null) return;
        _vm.UpdateReadout(s);
        if (!_vm.DriverPositioned) // disabled state: readouts only
        {
            _vm.NoteOutcome(TestViewModel.SampleOutcome.OffCanvas);
            return;
        }

        if (_vm.MapRawToDesktop(s.RawX, s.RawY) is not { } desktop
            || !PenSampleMapping.TryDesktopToCanvasNormalized(PaintCanvas, desktop, out var nx, out var ny)
            || nx < 0 || nx > 1 || ny < 0 || ny > 1)
        {
            PaintCanvas.EndStroke(); // pen points outside the canvas region — break the stroke
            _vm.NoteOutcome(TestViewModel.SampleOutcome.OffCanvas);
            return;
        }

        // The canvas draws the processed pressure, like the Pen page's processed canvas; the readouts above
        // keep showing what the tablet reported. Pressure 0 is not a stroke, so no ink.
        var inked = PaintCanvas.AddSample(s with { X = nx, Y = ny, Pressure = pressure, IsDown = pressure > 0 });
        _vm.NoteOutcome(inked ? TestViewModel.SampleOutcome.Drawn : TestViewModel.SampleOutcome.Hover);
        _vm.UpdateCanvasPosition(nx * PaintCanvas.Bounds.Width, ny * PaintCanvas.Bounds.Height);
    }

    // Typed nav creates a fresh TestView each visit; detach from the long-lived VM on unload so
    // discarded views don't keep handling driver samples / clears (Codex #94).
    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        if (_vm != null)
        {
            _vm.DriverSample -= OnDriverSample;
            _vm.ClearRequested -= OnClearRequested;
        }
    }

    private void OnClearRequested() => PaintCanvas.Clear();

    // Copy the current drawing to the clipboard as an image (Windows CF_DIB), so it can be pasted into
    // another app. A snapshot/clipboard failure surfaces a toast instead of silently doing nothing (#609).
    private void OnCopy(object? sender, RoutedEventArgs e)
    {
        if (PaintCanvas.Snapshot() is not { } snap) return;
        if (!ClipboardImage.CopyBgra(snap.Bgra, snap.Width, snap.Height))
            ProfileToast.Show(ClipboardImage.FailureHint, "IconAlert");
    }
}
