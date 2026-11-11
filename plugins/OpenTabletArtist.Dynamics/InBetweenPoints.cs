using System;
using System.Diagnostics;
using System.Numerics;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletArtist.Dynamics;

/// <summary>
/// #40 (Zak, 11 Nov 2026): the path the pen took between two of its reports. An Intuos4 sends about 200 reports a
/// second, so on a fast curve the pen travels a few millimetres between two of them, and an app that draws straight
/// lines between points shows flat facets. This fills the gaps with points on a smooth curve (uniform Catmull-Rom
/// through the real points), so apps get many more points that lie on the curve the hand drew.
/// <para>
/// The curve from one real point to the next needs the point after it, so the output runs one report behind the pen
/// (about 5 ms on an Intuos4) and moves along the curve as time passes. Pressure goes the same way in a straight line,
/// so the stroke's start and end (pen down and up) stay in step with the position.
/// </para>
/// </summary>
public sealed class InBetweenPath
{
    /// <summary>A pause longer than this (the pen left the tablet and came back) starts a new path.</summary>
    public const double GapResetMs = 50;

    private readonly Vector2[] _p = new Vector2[4];
    private readonly float[] _pr = new float[4];
    private int _n;
    private double _lastMs = double.NaN;
    private double _intervalMs = 5;

    /// <summary>How many real points are held (0–4).</summary>
    public int Count => _n;

    /// <summary>The measured time between the pen's reports, in milliseconds.</summary>
    public double IntervalMs => _intervalMs;

    /// <summary>A real report from the pen.</summary>
    public void Add(Vector2 position, float pressure, double nowMs)
    {
        if (!double.IsNaN(_lastMs))
        {
            var dt = nowMs - _lastMs;
            if (dt > GapResetMs) _n = 0;
            else if (dt > 0.2) _intervalMs += (dt - _intervalMs) * 0.1;
        }
        if (_n == 4)
        {
            Array.Copy(_p, 1, _p, 0, 3);
            Array.Copy(_pr, 1, _pr, 0, 3);
            _n = 3;
        }
        _p[_n] = position;
        _pr[_n] = pressure;
        _n++;
        _lastMs = nowMs;
    }

    /// <summary>Where to put the pen at <paramref name="nowMs"/>: on the curve between the second- and third-latest
    /// real points, as far along as the time since the latest report allows. With fewer than three points (the pen has
    /// just arrived) the latest real point is used.</summary>
    public bool TrySample(double nowMs, out Vector2 position, out float pressure)
    {
        if (_n == 0) { position = default; pressure = 0; return false; }
        if (_n < 3) { position = _p[_n - 1]; pressure = _pr[_n - 1]; return true; }

        float u = (float)Math.Clamp((nowMs - _lastMs) / _intervalMs, 0, 1);
        int c = _n - 2, b = _n - 3;
        Vector2 pa = b > 0 ? _p[b - 1] : _p[b];
        position = CatmullRom(pa, _p[b], _p[c], _p[_n - 1], u);
        pressure = _pr[b] + (_pr[c] - _pr[b]) * u;
        return true;
    }

    /// <summary>Uniform Catmull-Rom between <paramref name="b"/> (u = 0) and <paramref name="c"/> (u = 1).</summary>
    public static Vector2 CatmullRom(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float u)
    {
        float u2 = u * u, u3 = u2 * u;
        return 0.5f * (2f * b
                       + (c - a) * u
                       + (2f * a - 5f * b + 4f * c - d) * u2
                       + (3f * b - a - 3f * c + d) * u3);
    }
}

/// <summary>
/// #40: OpenTabletDriver filter that sends smooth in-between pen points (<see cref="InBetweenPath"/>) at
/// <c>Frequency</c> per second (the app writes 500), before the area mapping, so it works the same in pen and mouse
/// mode. Fails open: without the driver's timer it passes every report straight through.
/// </summary>
[PluginName("Tablet App for Mac - In-Between Points")]
public sealed class InBetweenFilter : AsyncPositionedPipelineElement<IDeviceReport>
{
    private readonly InBetweenPath _path = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public override PipelinePosition Position => PipelinePosition.PreTransform;

    /// <summary>Only pen reports go through the curve; buttons, keys and touch pass straight on.</summary>
    protected override bool FilterState(IDeviceReport? value) => value is ITabletReport && base.FilterState(value);

    protected override void ConsumeState()
    {
        if (State is not ITabletReport r) return;
        _path.Add(r.Position, r.Pressure, _clock.Elapsed.TotalMilliseconds);
        if (Scheduler == null) OnEmit();   // no timer: behave as if the filter were off
    }

    protected override void UpdateState()
    {
        if (State is not ITabletReport r || !PenIsInRange()) return;
        if (!_path.TrySample(_clock.Elapsed.TotalMilliseconds, out var position, out var pressure)) return;
        r.Position = position;
        r.Pressure = (uint)MathF.Round(MathF.Max(0, pressure));
        OnEmit();
    }
}
