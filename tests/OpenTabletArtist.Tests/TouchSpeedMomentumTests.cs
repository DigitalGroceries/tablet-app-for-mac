using System;
using System.Linq;
using System.Numerics;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Dynamics;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>TASKS #90 (Daniel, CTH-461, 17 Nov): "the touch works, albeit a little slow ... tracking speed and
/// momentum". Version 29: a Touch speed (1-100, 50 = Version 28) and Scroll momentum (off unless turned on).</summary>
public class TouchSpeedMomentumTests
{
    [Fact]
    public void Touch_speed_50_is_the_old_speed_and_each_25_doubles_or_halves_it()
    {
        Assert.Equal(1f, TouchTrackpadMath.SpeedFactor(50), 3);
        Assert.Equal(2f, TouchTrackpadMath.SpeedFactor(75), 3);
        Assert.Equal(4f, TouchTrackpadMath.SpeedFactor(100), 3);
        Assert.Equal(0.5f, TouchTrackpadMath.SpeedFactor(25), 3);
        Assert.Equal(TouchTrackpadMath.SpeedFactor(1), TouchTrackpadMath.SpeedFactor(-5));
        Assert.Equal(TouchTrackpadMath.SpeedFactor(100), TouchTrackpadMath.SpeedFactor(400));
    }

    [Fact]
    public void The_glide_slows_down_and_stops_after_about_a_second_and_a_half()
    {
        var v = new Vector2(0, 1500);   // a quick flick, pixels a second
        float travelled = 0, time = 0, lastStep = float.MaxValue;
        while (v != Vector2.Zero && time < 5)
        {
            var step = TouchTrackpadMath.MomentumStep(ref v, 0.016f).Y;
            Assert.True(step <= lastStep + 0.001f, "the glide sped up");
            lastStep = step;
            travelled += step;
            time += 0.016f;
        }
        Assert.InRange(time, 0.8f, 2.5f);
        // It glides about speed / friction: 1500 / 3 = 500 px, less the bit cut off at the stop speed.
        Assert.InRange(travelled, 400f, 520f);
    }

    [Fact]
    public void Only_the_last_moment_of_scrolling_sets_the_glide_speed()
    {
        var sv = new ScrollVelocity();
        var t0 = new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i <= 10; i++) sv.Add(new Vector2(0, 10), t0.AddMilliseconds(i * 10));   // 10 px every 10 ms
        Assert.Equal(1000f, sv.PixelsPerSecond(t0.AddMilliseconds(100)).Y, 0);
        // Fingers held still for a while before lifting: no glide.
        Assert.Equal(Vector2.Zero, sv.PixelsPerSecond(t0.AddMilliseconds(400)));
        sv.Reset();
        Assert.Equal(Vector2.Zero, sv.PixelsPerSecond(t0));
    }

    private static Profile Make(string tablet) =>
        new() { Tablet = tablet, OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)) };

    [Fact]
    public void The_tablet_page_stores_both_in_the_touch_filter_and_reads_them_back()
    {
        var p = Make("Wacom CTH-461");
        Assert.Equal(50, TouchTrackpad.ReadSpeed(p));
        Assert.False(TouchTrackpad.ReadMomentum(p));
        var settings = new OpenTabletDriver.Desktop.Settings();
        settings.Profiles.Add(p);
        TouchTrackpad.WriteFeel(settings, "Wacom CTH-461", 72, true);
        Assert.Equal(72, TouchTrackpad.ReadSpeed(p));
        Assert.True(TouchTrackpad.ReadMomentum(p));
        TouchTrackpad.WriteFeel(settings, "Wacom CTH-461", 300, false);
        Assert.Equal(100, TouchTrackpad.ReadSpeed(p));
        Assert.False(TouchTrackpad.ReadMomentum(p));
        var filter = p.Filters.Single(f => f.Path == TouchTrackpad.FilterTypeName);
        Assert.Single(filter.Settings, s => s.Property == TouchTrackpad.TouchSpeedProperty);
        Assert.Single(filter.Settings, s => s.Property == TouchTrackpad.ScrollMomentumProperty);
        Assert.False(TouchTrackpad.IsOn(p));   // writing the feel doesn't switch touch on (CTH-461 starts off)
    }

    [Fact]
    public void Other_tablets_are_left_alone()
    {
        var p = Make("Wacom PTK-440");
        var settings = new OpenTabletDriver.Desktop.Settings();
        settings.Profiles.Add(p);
        TouchTrackpad.WriteFeel(settings, "Wacom PTK-440", 80, true);
        Assert.DoesNotContain(p.Filters, f => f.Path == TouchTrackpad.FilterTypeName);
    }

    [Fact]
    public void The_plugin_offers_both_settings_with_the_old_behaviour_as_default()
    {
        var f = new TouchTrackpadFilter();
        Assert.Equal(50, f.TouchSpeed);
        Assert.False(f.ScrollMomentum);
        var names = typeof(TouchTrackpadFilter).GetProperties().Select(x => x.Name).ToList();
        Assert.Contains(TouchTrackpad.TouchSpeedProperty, names);
        Assert.Contains(TouchTrackpad.ScrollMomentumProperty, names);
    }
}
