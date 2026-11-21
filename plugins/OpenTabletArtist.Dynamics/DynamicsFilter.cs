using System;
using System.Numerics;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Dynamics;

/// <summary>
/// OpenTabletDriver filter implementing the OpenTabletArtist pen dynamics (#92): the "Extended"
/// pressure curve plus EMA pressure/position smoothing. Because it runs in the daemon's pipeline,
/// drawing apps (Krita, CSP, …) see the remapped, smoothed input.
///
/// The math lives in <see cref="Domain.PressureCurve"/> + <see cref="PenDynamicsProcessor"/>
/// (source-shared with the app, so the editor preview matches and the logic is unit-tested there).
/// </summary>
[PluginName("Tablet App for Mac - Pen Dynamics")]
public class DynamicsFilter : IPositionedPipelineElement<IDeviceReport>
{
    [Property("Softness"), DefaultPropertyValue(0f),
     ToolTip("Curve shape: 0 = linear, positive = lighter touch (concave), negative = heavier (convex). Range -0.9 to 0.9.")]
    public float Softness { get; set; }

    [Property("Input Minimum"), DefaultPropertyValue(0f),
     ToolTip("Pressure (0-1) below this is remapped to Output Minimum — or cut to zero if 'Cut below minimum' is on.")]
    public float InputMinimum { get; set; }

    [Property("Input Maximum"), DefaultPropertyValue(1f),
     ToolTip("Pressure (0-1) above this is clamped to Output Maximum (lets you reach full output before pressing all the way).")]
    public float InputMaximum { get; set; } = 1f;

    [Property("Output Minimum"), DefaultPropertyValue(0f),
     ToolTip("The output pressure (0-1) at the input minimum.")]
    public float Minimum { get; set; }

    [Property("Output Maximum"), DefaultPropertyValue(1f),
     ToolTip("The output pressure (0-1) at full input. Lower it to cap how hard the pen registers.")]
    public float Maximum { get; set; } = 1f;

    [Property("Cut below minimum"), DefaultPropertyValue(false),
     ToolTip("When on, pressure below Input Minimum produces zero output (a dead zone) instead of holding at Output Minimum.")]
    public bool CutBelowMinimum { get; set; }

    // Version 1: "Disable pressure sensitivity" draws at the user's own manual pressure, one per pen end.
    [Property("Manual pressure"), DefaultPropertyValue(false),
     ToolTip("Pressure sensitivity off: every pressed sample gets the manual pressure below.")]
    public bool ManualPressure { get; set; }

    [Property("Manual tip pressure"), DefaultPropertyValue(0.5f)]
    public float ManualTipPressure { get; set; } = 0.5f;

    [Property("Manual eraser pressure"), DefaultPropertyValue(0.5f)]
    public float ManualEraserPressure { get; set; } = 0.5f;

    private bool _eraser;

    // V29 #180 (Zak 21 Nov: "1 Can you make them two different setting values?"): the eraser end has its own feel curve. Off
    // (a profile written by an older app) = the eraser uses the tip's curve, as before.
    [Property("Eraser curve own"), DefaultPropertyValue(false),
     ToolTip("On: the eraser end uses the Eraser curve values below. Off: it uses the tip's curve.")]
    public bool EraserCurveOwn { get; set; }

    [Property("Eraser softness"), DefaultPropertyValue(0f)]
    public float EraserSoftness { get; set; }

    [Property("Eraser input minimum"), DefaultPropertyValue(0f)]
    public float EraserInputMinimum { get; set; }

    [Property("Eraser input maximum"), DefaultPropertyValue(1f)]
    public float EraserInputMaximum { get; set; } = 1f;

    [Property("Eraser output minimum"), DefaultPropertyValue(0f)]
    public float EraserMinimum { get; set; }

    [Property("Eraser output maximum"), DefaultPropertyValue(1f)]
    public float EraserMaximum { get; set; } = 1f;

    [Property("Eraser cut below minimum"), DefaultPropertyValue(false)]
    public bool EraserCutBelowMinimum { get; set; }

    private bool _lastCurveWasEraser;

    [Property("Pressure Smoothing"), DefaultPropertyValue(0f),
     ToolTip("EMA smoothing of pressure (amount 0 = off to 1 = max, perceptually scaled). Reduces pressure jitter at the cost of lag. Applies while drawing.")]
    public float PressureSmoothing { get; set; }

    [Property("Position Smoothing"), DefaultPropertyValue(0f),
     ToolTip("EMA smoothing of the pen position (amount 0 = off to 1 = max, perceptually scaled). Steadies wobbly lines at the cost of lag. Applies while drawing.")]
    public float PositionSmoothing { get; set; }

    [Property("Smooth after curve"), DefaultPropertyValue(true),
     ToolTip("On: apply the curve, then smooth (default). Off: smooth the raw pressure, then apply the curve.")]
    public bool SmoothAfterCurve { get; set; } = true;

    [TabletReference]
    public TabletReference? Tablet { get; set; }

    public PipelinePosition Position => PipelinePosition.PreTransform;

    public event Action<IDeviceReport>? Emit;

    private readonly PenDynamicsProcessor _processor = new();

    public void Consume(IDeviceReport value)
    {
        // Pen left the sensor — reset smoothing so the next stroke doesn't lerp in from here.
        if (value is IProximityReport { NearProximity: false })
            _processor.Reset();

        // Which end is in use: reports that carry Eraser say so; Wacom Intuos-protocol tablets announce the
        // tool once in a tool report and then send plain pen reports.
        if (value is IToolReport tool)
        {
            _eraser = tool.Tool == ToolType.Eraser;
            AnnouncePen(tool);
        }
        if (value is IEraserReport er) _eraser = er.Eraser;

        // V29 #180: the curve of the end that is down (the eraser's own one when the app has written it).
        bool eraserCurve = _eraser && EraserCurveOwn;
        if (eraserCurve != _lastCurveWasEraser) { _processor.Reset(); _lastCurveWasEraser = eraserCurve; }
        _processor.Settings = new PenDynamicsSettings(CurveFor(eraserCurve), PressureSmoothing, PositionSmoothing, SmoothAfterCurve);

        // Skip entirely when MaxPressure is unknown (some custom configs) — normalizing needs it, and
        // we must not zero pressure just because we can't process it (Codex #106). Pass through.
        if (value is ITabletReport report && (Tablet?.Properties?.Specifications?.Pen?.MaxPressure ?? 0) > 0)
        {
            var max = Tablet!.Properties!.Specifications!.Pen!.MaxPressure;
            double norm = report.Pressure / (double)max;

            // "Drawing" means the curve actually emits pressure (PenDynamicsProcessor.IsDrawing, shared
            // with the app's live preview). Both hover (raw 0) and the Cut/Clamp dead zone (raw > 0 but
            // mapped to 0) count as not-drawing: zero the output and reset smoothing so the first real
            // sample starts crisp — no lag/fly-in carried over from the pre-stroke approach (Codex #106).
            // Position is smoothed only while drawing, so a pen-out coordinate is never folded into the
            // next stroke either.
            if (!_processor.IsDrawing(norm))
            {
                report.Pressure = 0;
                _processor.Reset();
            }
            else
            {
                if (PositionSmoothing > 0)
                {
                    var (x, y) = _processor.ProcessPosition(report.Position.X, report.Position.Y);
                    report.Position = new Vector2((float)x, (float)y);
                }

                var outPressure = ManualPressure
                    ? Math.Clamp(_eraser ? ManualEraserPressure : ManualTipPressure, 0f, 1f)
                    : _processor.ProcessPressure(norm);
                report.Pressure = (uint)Math.Round(outPressure * max);
            }
        }

        Emit?.Invoke(value);
    }

    /// <summary>V29 #180: the tip's curve, or the eraser's own curve.</summary>
    public PressureCurveSettings CurveFor(bool eraser) => eraser
        ? new PressureCurveSettings(EraserSoftness, EraserInputMinimum, EraserInputMaximum, EraserMinimum, EraserMaximum,
            EraserCutBelowMinimum ? PressureMinApproach.Cut : PressureMinApproach.Clamp)
        : new PressureCurveSettings(Softness, InputMinimum, InputMaximum, Minimum, Maximum,
            CutBelowMinimum ? PressureMinApproach.Cut : PressureMinApproach.Clamp);

    // Version 14: tell the app which pen (and which end) is near, for per-pen settings. Written only when it
    // changes, so the file is touched once per pen change, not on every report.
    static PenToolSignal? _lastAnnounced;
    static readonly object AnnounceGate = new();

    static void AnnouncePen(IToolReport tool)
    {
        var signal = new PenToolSignal(tool.Serial, tool.RawToolID, tool.Tool == ToolType.Eraser);
        if (signal.Serial == 0 && signal.RawToolId == 0) return;
        lock (AnnounceGate)
        {
            if (_lastAnnounced == signal) return;
            _lastAnnounced = signal;
        }
        try { signal.Write(); }
        catch (Exception) { /* the pen still works; only the per-pen switch misses this change */ }
    }
}
