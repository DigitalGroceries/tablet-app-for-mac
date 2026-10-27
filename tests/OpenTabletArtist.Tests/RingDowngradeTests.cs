using System;
using System.Linq;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OtdInterop;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Version 16 BETA 3: going back to Version 15, editing the ring there, then updating to 16 again
/// reversed every ring mode (Version 15's rewrite drops DirectionVersion, so 16 read the ring as legacy).
/// Going back now first puts the ring in the layout Version 15 reads.</summary>
[Collection(DisplayEnumeratorCollection.Name)]
public class RingDowngradeTests
{
    private static AuxCombo K(string key, bool cmd = false) => new(false, false, false, key, cmd);

    private static WheelBindingSettings Wheel() => new()
    {
        WheelButtons = new PluginSettingStoreCollection(new[] { (PluginSettingStore)null! }),
        ClockwiseActivationThreshold = 5, CounterClockwiseActivationThreshold = 5,
    };

    private static string? Setting(PluginSettingStore? s, string p) => s?.Settings.FirstOrDefault(x => x.Property == p)?.Value?.ToString();

    private static WheelBindingSettings Version16Wheel()
    {
        var w = Wheel();
        Assert.True(RingModeBinding.Apply(w, new[]
        {
            new RingMode("Brush size", K("RightBracket"), K("LeftBracket"), StepDegrees: 8),
            new RingMode("Zoom", K("Equal", cmd: true), K("Minus", cmd: true), StepDegrees: 30, ScrollAmount: 60),
        }));
        return w;
    }

    [Fact]
    public void GoingBack_ThenAVersion15Edit_ThenUpdating_KeepsEveryDirection()
    {
        var w = Version16Wheel();
        var before = RingModeBinding.Read(w);

        Assert.True(RingModeBinding.ToLegacyLayout(w));
        // What Version 15 reads: physical clockwise in OTD's CounterClockwiseRotation, zoom in pixels, no marker.
        Assert.True(RingModeBinding.IsLegacy(w));
        Assert.Null(Setting(w.ClockwiseRotation, RingModeBinding.DirectionVersionProperty));
        Assert.Null(Setting(w.CounterClockwiseRotation, RingModeBinding.DirectionVersionProperty));
        Assert.Equal("RightBracket", Setting(w.CounterClockwiseRotation, "Mode1Keys"));
        var zoomSpeed = before[1].ScrollAmount;
        Assert.Equal((RingMode.LegacyScrollOfSpeed(zoomSpeed)).ToString(), Setting(w.CounterClockwiseRotation, "Mode2Scroll"));
        Assert.Equal((-RingMode.LegacyScrollOfSpeed(zoomSpeed)).ToString(), Setting(w.ClockwiseRotation, "Mode2Scroll"));

        var asVersion15 = RingModeBinding.Read(w);
        Assert.Equal(before.Select(m => (m.Name, m.Clockwise, m.CounterClockwise)),
            asVersion15.Select(m => (m.Name, m.Clockwise, m.CounterClockwise)));
        Assert.InRange(asVersion15[1].ScrollAmount, zoomSpeed - 1, zoomSpeed + 1);

        // Version 15 rewrites the ring in this same layout; Version 16 then migrates it as legacy.
        Assert.True(RingModeBinding.Apply(w, RingModeBinding.Load(w, null, null, null)));
        Assert.False(RingModeBinding.IsLegacy(w));
        Assert.Equal("RightBracket", Setting(w.ClockwiseRotation, "Mode1Keys"));
        Assert.Equal("LeftBracket", Setting(w.CounterClockwiseRotation, "Mode1Keys"));
        Assert.InRange(RingModeBinding.Read(w)[1].ScrollAmount, zoomSpeed - 1, zoomSpeed + 1);
    }

    [Fact]
    public void OnlyVersion16RingsAreConverted_AndOnlyOnce()
    {
        Assert.False(RingModeBinding.ToLegacyLayout(Wheel()));
        var w = Version16Wheel();
        Assert.True(RingModeBinding.ToLegacyLayout(w));
        Assert.False(RingModeBinding.ToLegacyLayout(w));
        Assert.Equal("RightBracket", Setting(w.CounterClockwiseRotation, "Mode1Keys"));
    }

    [Theory]
    [InlineData(15, true)]
    [InlineData(14, true)]
    [InlineData(16, false)]
    [InlineData(17, false)]
    public void OnlyVersionsBefore16NeedIt(int version, bool needed) =>
        Assert.Equal(needed, RingDowngrade.IsNeededFor(version));

    [Fact]
    public void Convert_CoversAll_TheAllStash_AndAppTables()
    {
        var tablet = "TestTablet-RingDowngrade-" + Guid.NewGuid().ToString("N")[..8];
        var profile = new Profile { Tablet = tablet, OutputMode = null! };
        profile.BindingSettings.WheelBindings.Add(Version16Wheel());
        var settings = new Settings { Profiles = new ProfileCollection { profile } };
        PerAppBindingStore.Add(tablet, "com.example.Paint", "Paint", "", profile.BindingSettings);
        PerAppBindingRuntime.SyncAllBindings(tablet, profile.BindingSettings);

        Assert.True(RingDowngrade.Convert(settings));

        Assert.True(RingModeBinding.IsLegacy(profile.BindingSettings.WheelBindings[0]));
        Assert.True(RingModeBinding.IsLegacy(PerAppBindingStore.Find(tablet, "com.example.Paint")!.Bindings.WheelBindings[0]));
        var stash = Newtonsoft.Json.JsonConvert.DeserializeObject<BindingSettings>(
            AppSettings.Get(PerAppBindingRuntime.AllBindingsKey(tablet))!)!;
        Assert.True(RingModeBinding.IsLegacy(stash.WheelBindings[0]));
        Assert.False(RingDowngrade.Convert(settings));
    }
}
