using System;
using System.Collections.Generic;
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
/// Pen scroll: hold a pen button (or an ExpressKey) bound to this and move the pen to scroll, like a mouse
/// wheel or Wacom's Pan/Scroll. The cursor stays where it was, so the window under it keeps receiving the
/// scroll, and the pen tip does not click while scrolling.
///
/// <para>A binding only hears press and release, so the movement is read by <see cref="PenScrollFilter"/>,
/// which the app adds to the profile whenever this binding is used. Both live in this assembly and share
/// <see cref="PenScrollState"/>. Tablet-agnostic: the filter works in millimetres from the tablet's own
/// digitizer size.</para>
/// </summary>
[PluginName("Tablet App for Mac - Pen scroll")]
public class PenScrollBinding : IStateBinding
{
    [Resolved] public IMouseScrollHandler? Pointer { set; get; }

    [Property("Speed"), DefaultPropertyValue(PenScrollMath.DefaultSpeed),
     ToolTip("1–100, higher = faster. 50 scrolls about 20 pixels per millimetre of pen movement.")]
    public int Speed { set; get; } = PenScrollMath.DefaultSpeed;

    [Property("Direction"), DefaultPropertyValue("Both"), PropertyValidated(nameof(ValidDirections)),
     ToolTip("Both, Vertical only or Horizontal only.")]
    public string Direction { set; get; } = "Both";

    [BooleanProperty("Invert", "Off: the page follows the pen, as if you grabbed it. On: the classic mouse-wheel direction.")]
    public bool Invert { set; get; }

    public static IEnumerable<string> ValidDirections => Enum.GetNames<PenScrollAxes>();

    private bool _warned;

    public void Press(TabletReference tablet, IDeviceReport report)
    {
        if (Pointer == null)
        {
            if (!_warned)
                Log.Write("Pen scroll", "No scroll output for this output mode, so pen scroll can't work.", LogLevel.Warning);
            _warned = true;
            return;
        }
        if (!PenScrollFilter.Installed && !_warned)
        {
            Log.Write("Pen scroll", "The pen scroll filter isn't in this profile; save the binding again in Tablet App for Mac.",
                LogLevel.Warning);
            _warned = true;
        }
        PenScrollState.Begin(this);
    }

    public void Release(TabletReference tablet, IDeviceReport report) => PenScrollState.End(this);

    internal void Send(int vertical, int horizontal)
    {
        var p = Pointer;
        if (p == null || (vertical == 0 && horizontal == 0)) return;
        if (vertical != 0) p.ScrollVertically(vertical);
        if (horizontal != 0) p.ScrollHorizontally(horizontal);
        // The output mode flushes after every report anyway; flushing here makes the scroll independent of
        // whether the cursor report gets through.
        if (p is ISynchronousPointer sync) sync.Flush();
    }

    /// <summary>True when the output mode moves the cursor by deltas (relative mode).</summary>
    internal bool IsRelative => Pointer is IRelativePointer && Pointer is not IAbsolutePointer;

    public override string ToString() => $"Pen scroll: {Direction}, Speed {Speed}{(Invert ? ", inverted" : "")}";
}

/// <summary>The binding that is scrolling right now, if any. Shared by the binding and the filter.</summary>
public static class PenScrollState
{
    private static PenScrollBinding? _active;

    public static PenScrollBinding? Active => Volatile.Read(ref _active);

    internal static void Begin(PenScrollBinding binding) => Volatile.Write(ref _active, binding);

    /// <summary>Ends only the session this binding started (two buttons bound to scroll can't cut each other off).</summary>
    internal static void End(PenScrollBinding binding) => Interlocked.CompareExchange(ref _active, null, binding);
}

/// <summary>
/// Reads pen movement while a <see cref="PenScrollBinding"/> is held: sends the scroll, holds the cursor
/// still and lifts the tip (pressure 0) so nothing is drawn or clicked. Passes every report untouched
/// otherwise. Added to the profile by the app when a pen scroll binding exists.
/// </summary>
[PluginName("Tablet App for Mac - Pen scroll filter")]
public class PenScrollFilter : IPositionedPipelineElement<IDeviceReport>
{
    private static long _constructed;
    public static bool Installed => Interlocked.Read(ref _constructed) != 0;

    public PenScrollFilter() => Interlocked.Exchange(ref _constructed, 1);

    [TabletReference] public TabletReference? Tablet { get; set; }

    // Before the output mode, so positions are raw tablet units (same meaning in absolute and relative mode)
    // and a held position keeps the cursor still in both.
    public PipelinePosition Position => PipelinePosition.PreTransform;

    public event Action<IDeviceReport>? Emit;

    private PenScrollBinding? _owner;
    private Vector2? _lastSeen;          // last pen position, scrolling or not
    private Vector2 _anchor;             // where the cursor is held
    private Vector2 _last;               // previous position during a scroll
    private Vector2 _remainder;
    private bool _moving;                // past the start dead zone
    private Vector2 _relativeOffset;     // relative mode: hides the travel made while scrolling

    public void Consume(IDeviceReport value)
    {
        if (value is OutOfRangeReport)
        {
            // Pen left the tablet: OTD releases the pen buttons, relative mode starts afresh.
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
        var active = PenScrollState.Active;
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
            // Hold the cursor where it was on the report that pressed the button.
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
                _last = pos;   // start from here, so the dead zone doesn't arrive as a jump
            }
            if (_moving)
            {
                var deltaMm = (pos - _last) * mm;
                var (v, h) = PenScrollMath.Step(deltaMm, active.Speed, PenScrollMath.ParseAxes(active.Direction),
                    active.Invert, ref _remainder);
                active.Send(v, h);
            }
        }

        _last = pos;
        _lastSeen = pos;
        pen.Position = _anchor - _relativeOffset;
        pen.Pressure = 0;
    }

    /// <summary>A scroll ended. In relative mode the cursor would jump by the pen's travel, so that travel
    /// is subtracted from later reports until the pen leaves the tablet.</summary>
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
