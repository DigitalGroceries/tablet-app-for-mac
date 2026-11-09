using System;
using System.Numerics;
using System.Threading;
using OpenTabletArtist.Domain;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.DependencyInjection;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Platform.Pointer;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletArtist.Dynamics;

/// <summary>
/// Pen zoom / pan+zoom: hold a pen button (or ExpressKey) and move the pen. Zoom sends Option+scroll
/// (same as touch-ring Smooth zoom — Photoshop and Affinity). Pan+Zoom pans with horizontal movement
/// and zooms with vertical movement. Cursor stays still; tip pressure is lifted.
/// </summary>
[PluginName("Tablet App for Mac - Pen zoom")]
public class PenZoomBinding : IStateBinding
{
    // Keep in sync with OpenTabletArtist.Domain.PenZoomBinding.ModeZoom / ModePanZoom.
    public const string ModeZoom = "Zoom";
    public const string ModePanZoom = "PanZoom";

    [Resolved] public IMouseScrollHandler? Pointer { set; get; }

    [Property("Mode"), DefaultPropertyValue(ModeZoom),
     PropertyValidated(nameof(ValidModes)),
     ToolTip("Zoom: move the pen up/down to zoom. PanZoom: horizontal pans, vertical zooms.")]
    public string Mode { set; get; } = ModeZoom;

    [Property("Speed"), DefaultPropertyValue(PenScrollMath.DefaultSpeed),
     ToolTip("1–100, higher = faster. Same scale as Pen scroll.")]
    public int Speed { set; get; } = PenScrollMath.DefaultSpeed;

    [BooleanProperty("Invert", "Off: pen tip up zooms in. On: the opposite.")]
    public bool Invert { set; get; }

    public static System.Collections.Generic.IEnumerable<string> ValidModes { get; } =
        new[] { ModeZoom, ModePanZoom };

    private bool _warned;

    public void Press(TabletReference tablet, IDeviceReport report)
    {
        if (!PenZoomFilter.Installed && !_warned)
        {
            Log.Write("Pen zoom", "The pen zoom filter isn't in this profile; save the binding again in Tablet App for Mac.",
                LogLevel.Warning);
            _warned = true;
        }
        PenZoomState.Begin(this);
    }

    public void Release(TabletReference tablet, IDeviceReport report) => PenZoomState.End(this);

    internal bool IsPanZoom => Mode == ModePanZoom;

    /// <summary>Zoom amount in MacScroll pixels (positive = zoom in).</summary>
    internal void SendZoom(int pixels)
    {
        if (pixels == 0) return;
        MacScroll.PostWithOption(pixels);
    }

    internal void SendPan(int horizontal)
    {
        var p = Pointer;
        if (p == null || horizontal == 0) return;
        p.ScrollHorizontally(horizontal);
        if (p is ISynchronousPointer sync) sync.Flush();
    }

    internal bool IsRelative => Pointer is IRelativePointer && Pointer is not IAbsolutePointer;

    public override string ToString() =>
        $"Pen {(IsPanZoom ? "pan+zoom" : "zoom")}: Speed {Speed}{(Invert ? ", inverted" : "")}";
}

public static class PenZoomState
{
    private static PenZoomBinding? _active;
    public static PenZoomBinding? Active => Volatile.Read(ref _active);
    internal static void Begin(PenZoomBinding binding) => Volatile.Write(ref _active, binding);
    internal static void End(PenZoomBinding binding) => Interlocked.CompareExchange(ref _active, null, binding);
}

[PluginName("Tablet App for Mac - Pen zoom filter")]
public class PenZoomFilter : IPositionedPipelineElement<IDeviceReport>
{
    private static long _constructed;
    public static bool Installed => Interlocked.Read(ref _constructed) != 0;

    public PenZoomFilter() => Interlocked.Exchange(ref _constructed, 1);

    [TabletReference] public TabletReference? Tablet { get; set; }
    public PipelinePosition Position => PipelinePosition.PreTransform;
    public event Action<IDeviceReport>? Emit;

    private PenZoomBinding? _owner;
    private Vector2? _lastSeen;
    private Vector2 _anchor;
    private Vector2 _last;
    private Vector2 _remainder;
    private bool _moving;
    private Vector2 _relativeOffset;

    public void Consume(IDeviceReport value)
    {
        if (value is OutOfRangeReport)
        {
            _owner = null;
            _lastSeen = null;
            _relativeOffset = Vector2.Zero;
        }
        else if (RingGestureGate.IsPenOnly(value) && value is ITabletReport pen)
        {
            Handle(pen);
        }
        Emit?.Invoke(value);
    }

    private void Handle(ITabletReport pen)
    {
        var active = PenZoomState.Active;
        var pos = pen.Position;

        if (active == null)
        {
            if (_owner != null) Finish(pos);
            _lastSeen = pos;
            pen.Position = pos - _relativeOffset;
            return;
        }

        if (!ReferenceEquals(active, _owner))
        {
            if (_owner != null) Finish(pos);
            _owner = active;
            _anchor = _lastSeen ?? pos;
            _last = _anchor;
            _remainder = Vector2.Zero;
            _moving = false;
        }

        var mmPerUnit = MmPerUnit();
        if (mmPerUnit is { } mm)
        {
            if (!_moving && Vector2.Distance(pos * mm, _anchor * mm) >= PenScrollMath.StartDeadZoneMm)
            {
                _moving = true;
                _last = pos;
            }
            if (_moving)
            {
                var deltaMm = (pos - _last) * mm;
                // Reuse pen-scroll maths (page-follow = invert false). For zoom: pen tip up (negative Y)
                // should zoom in → MacScroll wants positive for zoom in → negate vertical.
                var axes = active.IsPanZoom ? PenScrollAxes.Both : PenScrollAxes.Vertical;
                var (v, h) = PenScrollMath.Step(deltaMm, active.Speed, axes, active.Invert, ref _remainder);
                // Pen scroll vertical v: negative when pen moves up with invert=false (page follows).
                // Zoom in on pen tip up → PostWithOption(-v).
                active.SendZoom(-v);
                if (active.IsPanZoom) active.SendPan(h);
            }
        }

        _last = pos;
        _lastSeen = pos;
        pen.Position = _anchor - _relativeOffset;
        pen.Pressure = 0;
    }

    private void Finish(Vector2 pos)
    {
        if (_owner?.IsRelative == true) _relativeOffset += pos - _anchor;
        _owner = null;
    }

    private Vector2? MmPerUnit()
    {
        var d = Tablet?.Properties?.Specifications?.Digitizer;
        return d == null ? null : PenScrollMath.MmPerUnit(d.Width, d.Height, d.MaxX, d.MaxY);
    }
}
