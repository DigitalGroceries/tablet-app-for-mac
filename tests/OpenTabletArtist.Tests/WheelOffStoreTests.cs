using System.Collections.Generic;
using System.Linq;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>TASKS #29 (Zak, 10 Nov 2026): each Touch Ring / Touch Strip has its own On/Off that never clashes with
/// the main switch: a ring works only when both are On, and the main switch never changes a ring's own state.</summary>
public class WheelOffStoreTests
{
    private static string Tablet => "TestTablet-RingOff-" + System.Guid.NewGuid().ToString("N")[..8];

    private static WheelBindingSettings Ring(string cw, string ccw, string button)
    {
        var w = new WheelBindingSettings
        {
            ClockwiseRotation = AuxKeyBinding.MakeKeyBinding(cw),
            CounterClockwiseRotation = AuxKeyBinding.MakeKeyBinding(ccw),
            ClockwiseActivationThreshold = 15,
            CounterClockwiseActivationThreshold = 20,
        };
        w.WheelButtons.Add(AuxKeyBinding.MakeKeyBinding(button)!);
        return w;
    }

    private static string Key(PluginSettingStore? s) => AuxKeyBinding.ReadKey(s);

    [Theory]
    [InlineData(true, true, true)]     // main On,  ring On  -> works
    [InlineData(true, false, false)]   // main On,  ring Off -> does nothing
    [InlineData(false, true, false)]   // main Off, ring On  -> does nothing
    [InlineData(false, false, false)]  // main Off, ring Off -> does nothing
    public void ARingWorksOnlyWhenTheMainSwitchAndTheRingAreBothOn(bool main, bool ring, bool works) =>
        Assert.Equal(works, WheelOffStore.Works(main, ring));

    [Fact]
    public void Clear_EmptiesOnlyTheListedRing_AndKeepsItsThresholds()
    {
        var wheels = new List<WheelBindingSettings> { Ring("A", "B", "C"), Ring("D", "E", "F") };
        WheelOffStore.Clear(wheels, new[] { 2 });
        Assert.Equal("A", Key(wheels[0].ClockwiseRotation));
        Assert.Null(wheels[1].ClockwiseRotation);
        Assert.Null(wheels[1].CounterClockwiseRotation);
        Assert.Null(wheels[1].WheelButtons[0]);
        Assert.Equal(15f, wheels[1].ClockwiseActivationThreshold);
        Assert.Equal(20f, wheels[1].CounterClockwiseActivationThreshold);
    }

    [Fact]
    public void Clear_IgnoresRingNumbersPastTheEnd()
    {
        var wheels = new List<WheelBindingSettings> { Ring("A", "B", "C") };
        WheelOffStore.Clear(wheels, new[] { 0, 2, 9 });
        Assert.Equal("A", Key(wheels[0].ClockwiseRotation));
    }

    [Fact]
    public void OffThenOn_BringsTheRingBackExactly()
    {
        var tablet = Tablet;
        var wheels = new List<WheelBindingSettings> { Ring("A", "B", "C"), Ring("D", "E", "F") };
        var off = WheelOffStore.Load(tablet);
        off[2] = WheelOffStore.Copy(wheels[1]);
        WheelOffStore.Save(tablet, off);
        WheelOffStore.Clear(wheels, new[] { 2 });

        var kept = WheelOffStore.Load(tablet);
        Assert.True(kept.ContainsKey(2));
        WheelOffStore.Restore(wheels, 2, kept[2]);
        kept.Remove(2);
        WheelOffStore.Save(tablet, kept);

        Assert.Equal(new[] { "D", "E", "F" },
            new[] { Key(wheels[1].ClockwiseRotation), Key(wheels[1].CounterClockwiseRotation), Key(wheels[1].WheelButtons[0]) });
        Assert.Empty(WheelOffStore.Load(tablet));
        Assert.Equal("", AppSettings.Get(WheelOffStore.Key(tablet)));
    }

    [Fact]
    public void TheKeptCopy_IsNotTheProfilesObject()
    {
        var w = Ring("A", "B", "C");
        var copy = WheelOffStore.Copy(w);
        w.ClockwiseRotation = null;
        Assert.Equal("A", Key(copy.ClockwiseRotation));
    }

    [Theory]
    [InlineData(true, new int[] { }, new int[] { })]          // all On -> nothing emptied
    [InlineData(true, new[] { 2 }, new[] { 2 })]              // ring 2 Off -> only ring 2
    [InlineData(false, new int[] { }, new[] { 1, 2 })]        // main Off -> every ring
    [InlineData(false, new[] { 2 }, new[] { 1, 2 })]          // main Off + ring 2 Off -> every ring
    [InlineData(true, new[] { 2, 2, 7 }, new[] { 2 })]        // duplicates and numbers past the end ignored
    public void AnApplicationsTable_IsEmptiedByTheSameRule(bool main, int[] offRings, int[] expected) =>
        Assert.Equal(expected, WheelOffStore.RingsToEmpty(2, main, offRings).ToArray());

    [Fact]
    public void ACorruptStore_MeansEveryRingIsOn()
    {
        var tablet = Tablet;
        AppSettings.Set(WheelOffStore.Key(tablet), "{not json");
        Assert.Empty(WheelOffStore.Load(tablet));
        AppSettings.Set(WheelOffStore.Key(tablet), "");
    }
}
