using System;
using System.Collections.Generic;
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

    /// <summary>#120: reports closer together than this came in one input report (a Wacom Bluetooth report holds up to
    /// 4 pen frames, so they reach the filter microseconds apart).</summary>
    public const double BurstMs = 0.2;

    private readonly Vector2[] _p = new Vector2[4];
    private readonly float[] _pr = new float[4];
    private int _n;
    private double _lastMs = double.NaN;
    private double _intervalMs = 5;

    // #120: the later frames of one input report wait here and are played back evenly over the next interval, so the
    // curve passes through every frame instead of jumping three of them. A pen that sends one report at a time never
    // uses this, and behaves exactly as before.
    private readonly Queue<(Vector2 P, float Pr)> _waiting = new();
    private double _arrivalMs = double.NaN;    // when the newest input report arrived
    private double _lastAddMs = double.NaN;    // when Add was last called
    private int _burst = 1;                    // frames in the newest input report
    private int _released;                     // of those, how many after the first are on the curve already

    /// <summary>How many real points are held (0–4).</summary>
    public int Count => _n;

    /// <summary>The measured time between the pen's reports, in milliseconds.</summary>
    public double IntervalMs => _intervalMs;

    /// <summary>The time from one point on the curve to the next: the report interval, shared by the frames of one
    /// input report.</summary>
    private double StepMs => _intervalMs / _burst;

    /// <summary>A real report from the pen.</summary>
    public void Add(Vector2 position, float pressure, double nowMs)
    {
        if (_n > 0 && !double.IsNaN(_lastAddMs) && nowMs - _lastAddMs <= BurstMs)
        {
            _lastAddMs = nowMs;
            _waiting.Enqueue((position, pressure));
            _burst++;
            return;
        }

        Release(double.PositiveInfinity);          // a frame still waiting from the last input report goes on now
        if (!double.IsNaN(_arrivalMs))
        {
            var dt = nowMs - _arrivalMs;
            if (dt > GapResetMs) _n = 0;
            else if (dt > BurstMs) _intervalMs += (dt - _intervalMs) * 0.1;
        }
        _burst = 1;
        _released = 0;
        _arrivalMs = nowMs;
        _lastAddMs = nowMs;
        Put(position, pressure, nowMs);
    }

    private void Put(Vector2 position, float pressure, double atMs)
    {
        if (_n == 4)
        {
            Array.Copy(_p, 1, _p, 0, 3);
            Array.Copy(_pr, 1, _pr, 0, 3);
            _n = 3;
        }
        _p[_n] = position;
        _pr[_n] = pressure;
        _n++;
        _lastMs = atMs;
    }

    /// <summary>Puts on the curve the waiting frames whose turn has come by <paramref name="nowMs"/>: frame k of an
    /// input report is due k steps after the report arrived.</summary>
    private void Release(double nowMs)
    {
        while (_waiting.Count > 0)
        {
            double due = _arrivalMs + (_released + 1) * StepMs;
            if (due > nowMs) break;
            var (p, pr) = _waiting.Dequeue();
            _released++;
            Put(p, pr, due);
        }
    }

    /// <summary>Where to put the pen at <paramref name="nowMs"/>: on the curve between the second- and third-latest
    /// real points, as far along as the time since the latest report allows. With fewer than three points (the pen has
    /// just arrived) the latest real point is used.</summary>
    public bool TrySample(double nowMs, out Vector2 position, out float pressure)
    {
        Release(nowMs);
        if (_n == 0) { position = default; pressure = 0; return false; }
        if (_n < 3) { position = _p[_n - 1]; pressure = _pr[_n - 1]; return true; }

        float u = (float)Math.Clamp((nowMs - _lastMs) / StepMs, 0, 1);
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
