using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;
using OpenTabletArtist.Dynamics;
using OpenTabletArtist.Services;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletDriver.Plugin.Tablet;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>#40 (Zak): smooth in-between pen points for fast curves.</summary>
public class InBetweenPointsTests
{
    private static Vector2 OnCircle(double deg, float r = 100) =>
        new((float)(r * Math.Cos(deg * Math.PI / 180)), (float)(r * Math.Sin(deg * Math.PI / 180)));

    [Fact]
    public void Curve_starts_and_ends_on_the_real_points()
    {
        var a = new Vector2(0, 0); var b = new Vector2(10, 0); var c = new Vector2(20, 5); var d = new Vector2(30, 15);
        Assert.Equal(b, InBetweenPath.CatmullRom(a, b, c, d, 0));
        var end = InBetweenPath.CatmullRom(a, b, c, d, 1);
        Assert.True(Vector2.Distance(c, end) < 1e-4f);
    }

    [Fact]
    public void A_fast_circle_comes_out_round_not_flat()
    {
        var path = new InBetweenPath();
        // 30° of a 100-unit circle between reports, 5 ms apart: a fast stroke.
        for (int i = 0; i < 4; i++) path.Add(OnCircle(30 * i), 1000, 5 * i);

        Assert.True(path.TrySample(15, out var start, out _));
        Assert.True(Vector2.Distance(OnCircle(30), start) < 1e-3f);          // one report behind the pen
        Assert.True(path.TrySample(17.5, out var mid, out _));
        float chordMid = Vector2.Lerp(OnCircle(30), OnCircle(60), 0.5f).Length();   // a straight line: 96.6
        Assert.True(mid.Length() > 99f, $"in-between point at radius {mid.Length()}");
        Assert.True(MathF.Abs(mid.Length() - 100) < MathF.Abs(chordMid - 100));
        Assert.True(path.TrySample(20, out var end, out _));
        Assert.True(Vector2.Distance(OnCircle(60), end) < 1e-3f);
    }

    [Fact]
    public void Pressure_follows_the_same_delay_so_strokes_end_where_they_should()
    {
        var path = new InBetweenPath();
        path.Add(new Vector2(0, 0), 800, 0);
        path.Add(new Vector2(1, 0), 800, 5);
        path.Add(new Vector2(2, 0), 800, 10);
        path.Add(new Vector2(3, 0), 0, 15);       // pen lifted
        Assert.True(path.TrySample(15, out _, out var p0));
        Assert.Equal(800, p0);                    // still drawing the last pressed piece
        Assert.True(path.TrySample(17.5, out _, out var p1));
        Assert.Equal(800, p1);
        path.Add(new Vector2(3, 0), 0, 20);
        Assert.True(path.TrySample(22.5, out _, out var p2));
        Assert.Equal(400, p2, 1);                 // now easing out
    }

    [Fact]
    public void Just_arrived_pen_uses_the_latest_point_and_a_pause_starts_over()
    {
        var path = new InBetweenPath();
        Assert.False(path.TrySample(0, out _, out _));
        path.Add(new Vector2(5, 5), 0, 0);
        Assert.True(path.TrySample(1, out var p, out _));
        Assert.Equal(new Vector2(5, 5), p);
        path.Add(new Vector2(6, 5), 0, 5);
        path.Add(new Vector2(7, 5), 0, 10);
        Assert.Equal(3, path.Count);
        path.Add(new Vector2(50, 50), 0, 10 + InBetweenPath.GapResetMs + 1);
        Assert.Equal(1, path.Count);
    }

    [Fact]
    public void Learns_the_report_interval()
    {
        var path = new InBetweenPath();
        for (int i = 0; i < 100; i++) path.Add(new Vector2(i, 0), 0, i * 8);
        Assert.InRange(path.IntervalMs, 7.5, 8.5);
    }

    // ---- #120: Bluetooth sends up to 4 pen frames in one input report; they reach the filter microseconds apart ----

    [Fact]
    public void Pens_that_send_one_report_at_a_time_get_exactly_the_same_curve_as_before()
    {
        var now = new InBetweenPath(); var before = new InBetweenPathBefore120();
        var rnd = new Random(7); double t = 0; int deg = 0;
        for (int i = 0; i < 400; i++)
        {
            t += i % 97 == 96 ? InBetweenPath.GapResetMs + 5 : 4 + rnd.NextDouble() * 3;   // jitter, now and then a pause
            deg += 9;
            var p = OnCircle(deg); float pr = rnd.Next(0, 4096);
            now.Add(p, pr, t); before.Add(p, pr, t);
            for (double s = t; s < t + 8; s += 0.7)
            {
                bool a = now.TrySample(s, out var pa, out var pra), b = before.TrySample(s, out var pb, out var prb);
                Assert.Equal(b, a);
                Assert.Equal(pb, pa);
                Assert.Equal(prb, pra);
            }
        }
        Assert.Equal(before.IntervalMs, now.IntervalMs);
    }

    [Fact]
    public void Bluetooth_frames_of_one_report_are_played_back_through_the_gap_not_skipped()
    {
        const double gap = 30;        // ms between Bluetooth input reports
        var path = new InBetweenPath(); double t = 0; int frame = 0;
        void Report() { for (int k = 0; k < 4; k++) path.Add(OnCircle(7.5 * frame++), 1000, t + k * 0.01); t += gap; }
        for (int i = 0; i < 80; i++) Report();          // learns the 30 ms interval
        Assert.InRange(path.IntervalMs, 28, 32);

        double start = t - gap;                          // the newest report arrived here
        var angles = new List<double>();
        for (double s = start; s < start + gap - 0.5; s += 0.5)
        {
            Assert.True(path.TrySample(s, out var p, out _));
            Assert.InRange(p.Length(), 99f, 101f);       // stays on the round curve
            angles.Add(Math.Atan2(p.Y, p.X) * 180 / Math.PI);
        }
        var unwrapped = angles.Select(a => (a - angles[0] + 360) % 360).ToList();
        // Before #120 the curve crossed one frame (7.5 degrees) per report and then jumped; now it walks all four.
        Assert.InRange(unwrapped.Last(), 7.5 * 3 - 1, 7.5 * 4 + 1);
        for (int i = 1; i < unwrapped.Count; i++) Assert.True(unwrapped[i] >= unwrapped[i - 1] - 1e-3, "the curve went backwards");
    }

    private sealed class Pen : ITabletReport
    {
        public byte[] Raw { get; set; } = Array.Empty<byte>();
        public Vector2 Position { get; set; }
        public uint Pressure { get; set; }
        public bool[] PenButtons { get; set; } = new bool[2];
    }

    private sealed class Aux : IAuxReport
    {
        public byte[] Raw { get; set; } = Array.Empty<byte>();
        public bool[] AuxButtons { get; set; } = new bool[8];
    }

    [Fact]
    public void Without_the_driver_timer_the_filter_passes_everything_through()
    {
        var f = new InBetweenFilter();
        var seen = new List<IDeviceReport>();
        f.Emit += seen.Add;
        f.Consume(new Pen { Position = new Vector2(1, 2), Pressure = 10 });
        f.Consume(new Aux());
        Assert.Equal(2, seen.Count);
        Assert.Equal(new Vector2(1, 2), ((ITabletReport)seen[0]).Position);
        Assert.Equal(OpenTabletDriver.Plugin.Output.PipelinePosition.PreTransform, f.Position);
    }

    private static Settings OneTablet(out Profile profile)
    {
        var settings = new Settings();
        profile = new Profile
        {
            Tablet = "Wacom PTK-440",
            OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)),
            Filters = new PluginSettingStoreCollection(),
        };
        settings.Profiles.Add(profile);
        return settings;
    }

    [Fact]
    public void Setting_on_adds_the_filter_last_at_500_per_second_and_off_turns_it_off()
    {
        var settings = OneTablet(out var profile);
        var other = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)) { Enable = true };
        profile.Filters.Add(other);
        Assert.False(InBetweenProfile.IsOn(settings, "Wacom PTK-440"));

        InBetweenProfile.Write(settings, "Wacom PTK-440", true);
        Assert.True(InBetweenProfile.IsOn(settings, "Wacom PTK-440"));
        var store = profile.Filters.Last();
        Assert.Equal(InBetweenProfile.FilterTypeName, store.Path);
        Assert.Equal(500f, store.Settings.Single(s => s.Property == "Frequency").GetValue<float>());

        profile.Filters.Add(new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)));
        InBetweenProfile.Write(settings, "Wacom PTK-440", true);
        Assert.Equal(InBetweenProfile.FilterTypeName, profile.Filters.Last().Path);   // moved back to the end
        Assert.Single(profile.Filters, f => f.Path == InBetweenProfile.FilterTypeName);

        InBetweenProfile.Write(settings, "Wacom PTK-440", false);
        Assert.False(InBetweenProfile.IsOn(settings, "Wacom PTK-440"));
    }

    [Fact]
    public void The_app_names_the_plugin_class()
    {
        Assert.Equal(InBetweenProfile.FilterTypeName, typeof(InBetweenFilter).FullName);
    }

    [Fact]
    public void Pen_page_switch_and_texts_in_every_language()
    {
        var panel = File.ReadAllText(Path.Combine(Root(), "OpenTabletArtist/Views/PenBasicsPanel.axaml"));
        Assert.Contains("x:Name=\"SmootherCurvesSwitch\"", panel);
        Assert.Contains("IsChecked=\"{Binding SmootherCurves}\"", panel);
        var dir = Path.Combine(Root(), "OpenTabletArtist/Assets/i18n");
        var en = JObject.Parse(File.ReadAllText(Path.Combine(dir, "en.json")));
        foreach (var f in Directory.GetFiles(dir, "*.json"))
        {
            var j = JObject.Parse(File.ReadAllText(f));
            foreach (var k in new[] { "tablet.pen.smoother_curves", "tablet.pen.smoother_curves_tip" })
            {
                Assert.False(string.IsNullOrWhiteSpace((string?)j[k]), $"{Path.GetFileName(f)} misses {k}");
                if (!f.EndsWith("en.json")) Assert.NotEqual((string?)en[k], (string?)j[k]);
            }
        }
    }

    private static string Root([CallerFilePath] string f = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(f)!, "..", ".."));
}

/// <summary>The path exactly as it was before #120 (commit 89f003f), to prove single reports are unchanged.</summary>
internal sealed class InBetweenPathBefore120
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
    private static Vector2 CatmullRom(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float u)
    {
        float u2 = u * u, u3 = u2 * u;
        return 0.5f * (2f * b
                       + (c - a) * u
                       + (2f * a - 5f * b + 4f * c - d) * u2
                       + (3f * b - a - 3f * c + d) * u3);
    }
}
