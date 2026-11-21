using System;
using System.Linq;
using System.Numerics;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Dynamics;
using OpenTabletArtist.Services;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletDriver.Plugin.Tablet;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>V29 #180 (Zak 21 Nov: "1 Can you make them two different setting values?"): the tip and the eraser each have their own
/// feel curve. A profile written by an older app has no eraser curve: the eraser then keeps using the tip's curve.</summary>
public class EraserCurveTests
{
    private sealed class PenReport : ITabletReport, IEraserReport
    {
        public byte[] Raw { get; set; } = Array.Empty<byte>();
        public Vector2 Position { get; set; }
        public uint Pressure { get; set; }
        public bool[] PenButtons { get; set; } = new bool[2];
        public bool Eraser { get; set; }
    }

    private static DynamicsFilter Filter()
    {
        var config = OfflineTablet.ConfigFor("Wacom PTK-440")!;
        return new DynamicsFilter
        {
            Tablet = new TabletReference(config, Array.Empty<DeviceIdentifier>()),
            Maximum = 1f, InputMaximum = 1f,
        };
    }

    private static uint Run(DynamicsFilter f, bool eraser, uint raw)
    {
        uint got = 0;
        f.Emit += r => got = ((ITabletReport)r).Pressure;
        var report = new PenReport { Pressure = raw, Eraser = eraser };
        f.Consume(report);
        return got;
    }

    // The filter stores floats: compare to float precision.
    private static void Same(PressureCurveSettings want, PressureCurveSettings? got)
    {
        Assert.NotNull(got);
        var g = got!.Value;
        foreach (var (a, b) in new[] { (want.Softness, g.Softness), (want.InputMinimum, g.InputMinimum), (want.InputMaximum, g.InputMaximum),
                                       (want.Minimum, g.Minimum), (want.Maximum, g.Maximum) })
            Assert.Equal(a, b, 5);
        Assert.Equal(want.MinApproach, g.MinApproach);
    }

    [Fact]
    public void TheEraserUsesItsOwnCurve_TheTipKeepsItsOwn()
    {
        var f = Filter();
        int max = (int)f.Tablet!.Properties!.Specifications!.Pen!.MaxPressure;
        f.Softness = 0f;                                  // tip: straight
        f.EraserCurveOwn = true; f.EraserSoftness = 0.8f; f.EraserMaximum = 1f; f.EraserInputMaximum = 1f;   // eraser: soft
        uint half = (uint)(max / 2);
        var tip = Run(f, eraser: false, half);
        var eraser = Run(f, eraser: true, half);
        Assert.InRange(Math.Abs((int)tip - (int)half), 0, max / 50);   // straight curve: about the same
        Assert.True(eraser > tip, $"a soft eraser curve gives more pressure at half press ({eraser} vs {tip})");
    }

    [Fact]
    public void WithoutItsOwnCurve_TheEraserFollowsTheTip_AsBefore()
    {
        var f = Filter();
        int max = (int)f.Tablet!.Properties!.Specifications!.Pen!.MaxPressure;
        f.Softness = 0.8f; f.EraserCurveOwn = false; f.EraserSoftness = -0.8f;
        uint half = (uint)(max / 2);
        Assert.Equal(Run(f, eraser: false, half), Run(f, eraser: true, half));
    }

    [Fact]
    public void TheApp_WritesAndReadsTheEraserCurve_AndKeepsItWhenOnlyTheTipChanges()
    {
        var profile = new Profile { Tablet = "Wacom PTK-440", OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)), Filters = new PluginSettingStoreCollection() };
        var tipCurve = PressureCurveSettings.Default with { Softness = 0.2 };
        var eraserCurve = PressureCurveSettings.Default with { Softness = -0.5, Maximum = 0.9 };
        PressureCurveProfile.WriteProfile(profile, new PenDynamicsSettings(tipCurve, 0, 0, true), true, eraser: eraserCurve);
        Same(eraserCurve, PressureCurveProfile.ReadEraserCurve(profile));

        // A later write of the tip alone (eraser not given) keeps the eraser's own curve.
        PressureCurveProfile.WriteProfile(profile, new PenDynamicsSettings(tipCurve with { Softness = 0.4 }, 0, 0, true), true);
        Same(eraserCurve, PressureCurveProfile.ReadEraserCurve(profile));
        var store = profile.Filters!.Single();
        Assert.True(store.Settings!.Single(s => s.Property == "EraserCurveOwn").GetValue<bool>());
    }

    [Fact]
    public void AnOldProfile_HasNoEraserCurve_AndAFactoryRestoreGivesTheEraserTheFactoryCurve()
    {
        var profile = new Profile { Tablet = "Wacom PTK-440", OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)), Filters = new PluginSettingStoreCollection() };
        PressureCurveProfile.WriteProfile(profile, new PenDynamicsSettings(PressureCurveSettings.Default, 0, 0, true), true);
        Assert.Null(PressureCurveProfile.ReadEraserCurve(profile));
        FactorySettingsMerge.ApplyFactoryTipFeel(profile);
        Same(PressureCurveSettings.Factory, PressureCurveProfile.ReadEraserCurve(profile));
    }
}
