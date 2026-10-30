using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// Editor state for a single tablet wheel: a clockwise + counter-clockwise rotation binding (each a
/// <see cref="ButtonBinding"/>), any wheel buttons, per-direction activation thresholds (how far you
/// turn before the action fires), and step-size info. Live rotation reports flash the matching row by
/// toggling its <see cref="ButtonBinding.IsPressed"/> — reusing the ExpressKeys press highlight.
/// </summary>
public partial class WheelEditor : ObservableObject
{
    private readonly Func<int, bool, double, Task>? _applyThreshold;
    private readonly double _wheelSteps;   // StepCount (360 / step°); 0 when unknown
    private int? _lastPosition;
    private double _relAccum;               // running synthetic position for relative wheels (in steps)
    private CancellationTokenSource? _flashCts;
    private bool _suppress;

    public int WheelIndex { get; }
    public string Title { get; }
    public bool ShowTitle { get; }

    public ButtonBinding Clockwise { get; }
    public ButtonBinding CounterClockwise { get; }
    public IReadOnlyList<ButtonBinding> Buttons { get; }
    public bool HasButtons => Buttons.Count > 0;

    /// <summary>Wacom-style Touch Ring modes for this wheel. While they're on, the plain direction and
    /// ring-button rows are replaced by the mode list.</summary>
    public RingModesEditor? RingModes { get; init; }

    /// <summary>False while wheel mapping is suspended / read-only — dims the sensitivity sliders.</summary>
    public bool CanEdit => _applyThreshold != null;

    public bool HasStepInfo { get; }
    public string StepInfo { get; } = "";

    // Sensitivity slider bounds (degrees). Snaps to whole steps when the step size is known.
    public double ThresholdMin { get; }
    public double ThresholdMax { get; }
    public double ThresholdTick { get; }

    [ObservableProperty] private double _clockwiseThreshold;
    [ObservableProperty] private double _counterClockwiseThreshold;
    public string ClockwiseThresholdText => ThresholdText(ClockwiseThreshold);
    public string CounterClockwiseThresholdText => ThresholdText(CounterClockwiseThreshold);

    // Live gauge state: LivePosition is 0..1 around the ring (null before the first touch / relative
    // wheels); the turning flags drive the direction glyph and fade with the row flash.
    [ObservableProperty, NotifyPropertyChangedFor(nameof(LiveReadout))] private double? _livePosition;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(LiveReadout))] private bool _turningClockwise;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(LiveReadout))] private bool _turningCounterClockwise;

    /// <summary>Version 1: the angle and turn direction, shown under the Touch ring picture
    /// ("350° · Clockwise"); "Touch the ring" before the first touch.</summary>
    /// <summary>Version 16: this "wheel" is a touch strip (Intuos3, Cintiq 21UX/12WX/22HD). Its position
    /// counts from the top (0) down, so a falling position is a finger moving up. The Clockwise row is the
    /// strip's Up row (both are OTD's CounterClockwiseRotation store).</summary>
    public bool IsStrip { get; }

    /// <summary>The ring picture only makes sense for a ring.</summary>
    public bool IsRing => !IsStrip;

    public string LiveReadout
    {
        get
        {
            if (IsStrip)   // Version 16: a touch strip has no angle, only up and down
                return TurningClockwise ? Services.Localizer.Te("tablet.strip.up", "Up")
                     : TurningCounterClockwise ? Services.Localizer.Te("tablet.strip.down", "Down")
                     : Services.Localizer.Te("tablet.strip.touch", "Touch the strip");
            string? dir = TurningClockwise ? "Clockwise" : TurningCounterClockwise ? "Anticlockwise" : null;
            if (LivePosition is double n)
            {
                var deg = Math.Round(OpenTabletArtist.Controls.WheelGauge.AngleDeg(n)) % 360.0;   // Version 4: same zero as the marker (12 o'clock = 0°)
                return dir == null ? $"{deg:0}°" : $"{deg:0}° · {dir}";
            }
            return dir ?? "Touch the ring";
        }
    }

    public WheelEditor(int wheelIndex, string title, bool showTitle,
        ButtonBinding clockwise, ButtonBinding counterClockwise, IReadOnlyList<ButtonBinding> buttons,
        double clockwiseThreshold, double counterClockwiseThreshold, double? stepSizeDegrees,
        Func<int, bool, double, Task>? applyThreshold, bool isStrip = false)
    {
        WheelIndex = wheelIndex;
        IsStrip = isStrip;
        Title = title;
        ShowTitle = showTitle;
        Clockwise = clockwise;
        CounterClockwise = counterClockwise;
        Buttons = buttons;
        _applyThreshold = applyThreshold;

        if (stepSizeDegrees is > 0 and double step)
        {
            HasStepInfo = true;
            _wheelSteps = Math.Round(360.0 / step);
            StepInfo = $"≈{step:0.#}° per step · {_wheelSteps:0} steps per full turn";
            ThresholdMin = step;
            ThresholdMax = Math.Max(step * 2, 90);
            ThresholdTick = step;
        }
        else
        {
            ThresholdMin = 1;
            ThresholdMax = 90;
            ThresholdTick = 1;
        }

        _suppress = true;   // seed from the profile without scheduling a save
        ClockwiseThreshold = Clamp(clockwiseThreshold);
        CounterClockwiseThreshold = Clamp(counterClockwiseThreshold);
        _suppress = false;
    }

    private double Clamp(double v) => v < ThresholdMin ? ThresholdMin : v > ThresholdMax ? ThresholdMax : v;

    private string ThresholdText(double deg) => HasStepInfo
        ? $"{deg:0}° · every {Math.Max(1, Math.Round(deg / ThresholdTick)):0} step(s)"
        : $"{deg:0}°";

    partial void OnClockwiseThresholdChanged(double value)
    {
        OnPropertyChanged(nameof(ClockwiseThresholdText));
        if (_suppress || IsSpuriousWriteback(value)) return;
        _ = _applyThreshold?.Invoke(WheelIndex, true, value);
    }

    partial void OnCounterClockwiseThresholdChanged(double value)
    {
        OnPropertyChanged(nameof(CounterClockwiseThresholdText));
        if (_suppress || IsSpuriousWriteback(value)) return;
        _ = _applyThreshold?.Invoke(WheelIndex, false, value);
    }

    /// <summary>
    /// A slider rebuilt by RefreshWheels (which runs after every apply) can push its raw stored value back
    /// through the TwoWay binding before its Minimum is applied. For wheels whose stored threshold sits
    /// below one full step, that lands below <see cref="ThresholdMin"/> — which the slider clamps user
    /// input to, so it's never a real gesture. Persisting it would clobber the just-applied value and, via
    /// the rebuild it triggers, loop forever (the sensitivity slider "snapping back"). Snap the display
    /// back to a valid value and don't apply it.
    /// </summary>
    private bool IsSpuriousWriteback(double value)
    {
        if (value >= ThresholdMin) return false;
        _suppress = true;
        ClockwiseThreshold = Clamp(ClockwiseThreshold);
        CounterClockwiseThreshold = Clamp(CounterClockwiseThreshold);
        _suppress = false;
        return true;
    }

    // ── Live rotation feedback ──────────────────────────────────────────────
    /// <summary>Absolute-wheel (touch-ring) position update. Computes a wrapped delta the same way the
    /// daemon does (see OTD WheelBindings.ComputeAbsoluteWheelDelta) so the flash matches the binding
    /// that actually fires; null resets tracking (finger lifted).</summary>
    public void OnAbsolutePosition(uint? position)
    {
        if (position is not uint pos) { _lastPosition = null; return; }  // idle / no change → keep last marker
        if (_wheelSteps > 0) LivePosition = pos / _wheelSteps;
        if (_lastPosition is int last && _wheelSteps > 0)
        {
            // Version 3: on his ring the readout showed Anticlockwise for a clockwise turn, so a
            // rising position (positive delta) is a physical clockwise turn. Readout only: the binding stores
            // (TabletDetailViewModel.RefreshWheels, RingModes.Apply) are unchanged.
            double d = ((pos - (double)last + 1.5 * _wheelSteps) % _wheelSteps) - 0.5 * _wheelSteps;
            if (IsStrip) d = -d;   // Version 16: a strip's falling position is a finger moving up (the Up row)
            if (d > 0) Flash(clockwise: true);
            else if (d < 0) Flash(clockwise: false);
        }
        _lastPosition = (int)pos;
    }

    /// <summary>Relative-wheel step delta (signed steps since the last report). Inverted to match the
    /// physical CW/CCW mapping used for the binding stores (see TabletDetailViewModel.RefreshWheels).</summary>
    public void OnRelativeDelta(int delta)
    {
        if (delta == 0) return;
        Flash(clockwise: delta > 0);   // Version 3: same reversal as the touch ring readout

        // Relative wheels report no absolute position, so accumulate the signed steps into a synthetic
        // ring position — that drives the same moving marker + fading trail an absolute ring gets, so a
        // relative wheel (e.g. the Wacom PTK-670, 24 steps/turn) animates instead of only flashing a
        // direction (#308). Subtract the delta so the marker turns the same physical way you turn the
        // wheel (OTD's delta sign runs opposite to physical rotation on tested relative wheels).
        if (_wheelSteps > 0)
        {
            _relAccum += delta;   // Version 3: positive steps are clockwise, like the readout
            double m = _relAccum % _wheelSteps;
            if (m < 0) m += _wheelSteps;
            LivePosition = m / _wheelSteps;
        }
    }

    /// <summary>Clears live gauge/flash state when leaving the Wheel tab or stopping the stream.</summary>
    public void ClearLiveState()
    {
        _flashCts?.Cancel();
        _lastPosition = null;
        _relAccum = 0;
        LivePosition = null;
        Clockwise.IsPressed = false;
        CounterClockwise.IsPressed = false;
        TurningClockwise = false;
        TurningCounterClockwise = false;
        foreach (var b in Buttons) b.IsPressed = false;
    }

    private void Flash(bool clockwise)
    {
        Clockwise.IsPressed = clockwise;
        CounterClockwise.IsPressed = !clockwise;
        TurningClockwise = clockwise;
        TurningCounterClockwise = !clockwise;
        _flashCts?.Cancel();
        var cts = _flashCts = new CancellationTokenSource();
        _ = ResetAsync(cts.Token);

        async Task ResetAsync(CancellationToken ct)
        {
            try { await Task.Delay(220, ct); }
            catch (TaskCanceledException) { return; }
            if (ct.IsCancellationRequested) return;
            Clockwise.IsPressed = false;
            CounterClockwise.IsPressed = false;
            TurningClockwise = false;
            TurningCounterClockwise = false;
        }
    }
}
