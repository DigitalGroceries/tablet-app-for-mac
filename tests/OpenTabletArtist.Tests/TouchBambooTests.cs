using System.Linq;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Dynamics;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OpenTabletDriver.Configurations.Parsers.Wacom.BambooV2;
using OpenTabletDriver.Configurations.Parsers.Wacom.IntuosV2;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin.Tablet.Touch;
using OpenTabletDriver.Plugin.Tablet.Wheel;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// Version 28 (TASKS #77, Daniel's CTH-461; plan agreed by Claude and Devin, 15 Nov 22:27): finger touch on the Bamboo
/// Pen &amp; Touch models (20-byte report 0x02 = 4 keys + up to 2 fingers, format from Linux wacom_wac.c
/// wacom_bpt_touch), starting OFF, and a Touch on/off action for pen buttons and ExpressKeys.
/// </summary>
[Collection(DisplayEnumeratorCollection.Name)]
public class TouchBambooTests
{
    private static byte[] Report(params (int Index, byte Value)[] bytes)
    {
        var r = new byte[20];
        r[0] = 0x02;
        foreach (var (i, v) in bytes) r[i] = v;
        return r;
    }

    [Fact]
    public void KeysOnly_GivesTheFourKeys_NoFingers_NoWheel()
    {
        // Keys 1 and 3 down; finger bytes zero.
        var report = new BambooV2AuxReportParser().Parse(Report((1, 0x05)));
        var touch = Assert.IsType<BambooV2TouchReport>(report);
        Assert.Equal(new[] { true, false, true, false }, touch.AuxButtons);
        Assert.All(touch.Touches, Assert.Null);
        Assert.False(report is IAbsoluteWheelReport);   // Devin's (A): no false Touch Ring events from finger bytes
        Assert.False(report is IWheelButtonReport);
    }

    [Fact]
    public void TwoFingers_NineByteStep()
    {
        // data[1] bit 7 clear: finger i at offset 9*i. Finger 0: data[3..6]; finger 1: data[12..15].
        var r = Report((3, 0x81), (4, 0x23), (5, 0x02), (6, 0x10),     // touching, x = 0x123, y = 0x210
                       (12, 0x80), (13, 0x40), (14, 0x00), (15, 0x50)); // touching, x = 0x040, y = 0x050
        var touch = (BambooV2TouchReport)new BambooV2AuxReportParser().Parse(r);
        Assert.Equal(new System.Numerics.Vector2(0x123, 0x210), touch.Touches[0]!.Position);
        Assert.Equal(new System.Numerics.Vector2(0x040, 0x050), touch.Touches[1]!.Position);
        Assert.Equal(new[] { false, false, false, false }, touch.AuxButtons);
    }

    [Fact]
    public void TwoFingers_EightByteStep_AndALiftedFinger()
    {
        // data[1] bit 7 set: finger i at offset 8*i. Finger 0 lifted (bit 7 of data[3] clear), finger 1 at data[11..14].
        var r = Report((1, 0x88), (3, 0x01), (4, 0x11),
                       (11, 0x87), (12, 0xFF), (13, 0x07), (14, 0xFF)); // x = 0x7ff (the 11-bit mask), y = 0x7ff
        var touch = (BambooV2TouchReport)new BambooV2AuxReportParser().Parse(r);
        Assert.Null(touch.Touches[0]);
        Assert.Equal(new System.Numerics.Vector2(0x7ff, 0x7ff), touch.Touches[1]!.Position);
        Assert.Equal(new[] { false, false, false, true }, touch.AuxButtons);   // bit 3 = key 4; bit 7 is the format flag
    }

    [Fact]
    public void OtherLengths_StayIntuosAuxReports()
    {
        var shorter = new byte[10]; shorter[0] = 0x02;
        Assert.IsType<IntuosV2AuxReport>(new BambooV2AuxReportParser().Parse(shorter));
        var other = new byte[20]; other[0] = 0x03;
        Assert.IsNotType<BambooV2TouchReport>(new BambooV2AuxReportParser().Parse(other));
    }

    [Theory]
    [InlineData("Wacom CTH-460")]
    [InlineData("Wacom CTH-461")]
    [InlineData("Wacom CTH-661")]
    public void TheThreeModels_HaveATouchSize_AndUseTheTrackpad(string name)
    {
        var touch = OfflineTablet.ConfigFor(name)!.Specifications.Touch;
        Assert.NotNull(touch);
        Assert.True(touch!.Width > 0 && touch.Height > 0);
        Assert.Equal(1023, touch.MaxX);
        Assert.Contains(name, TouchTrackpad.Tablets);
        Assert.False(TouchTrackpad.StartsOn(name));   // Devin's (C): new models start off
    }

    private static Profile Make(string tablet) =>
        new() { Tablet = tablet, OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)) };

    private static PluginSettingStore Filter(Profile p) => p.Filters.First(f => f.Path == TouchTrackpad.FilterTypeName);

    [Fact]
    public void Bamboo_FilterRuns_ButTouchStartsOff_TheSwitchTurnsItOn()
    {
        var p = Make("Wacom CTH-461");
        Assert.False(TouchTrackpad.IsOn(p));          // not written yet: off for this model
        Assert.True(TouchTrackpad.SyncFilter(p));
        Assert.True(Filter(p).Enable);                // the filter runs, so the Touch on/off key works
        Assert.False(TouchTrackpad.IsOn(p));
        var settings = new OpenTabletDriver.Desktop.Settings();
        settings.Profiles.Add(p);
        TouchTrackpad.Write(settings, "Wacom CTH-461", true);
        Assert.True(TouchTrackpad.IsOn(p));
        Assert.Single(Filter(p).Settings, s => s.Property == TouchTrackpad.TouchOnProperty);
        TouchTrackpad.Write(settings, "Wacom CTH-461", false);
        Assert.False(TouchTrackpad.IsOn(p));
        Assert.True(Filter(p).Enable);
    }

    [Fact]
    public void Pth651_StillStartsOn_AndOldProfilesReadTheSame()
    {
        var p = Make("Wacom PTH-651");
        Assert.True(TouchTrackpad.IsOn(p));
        TouchTrackpad.SyncFilter(p);
        Assert.True(TouchTrackpad.IsOn(p));
        // Before Version 28: no TouchOn setting; on = the filter enabled, off = the filter disabled.
        var legacy = AuxKeyBinding.NewStore(TouchTrackpad.FilterTypeName);
        legacy.Enable = true;
        Assert.True(TouchTrackpad.StoreIsOn(legacy));
        legacy.Enable = false;
        Assert.False(TouchTrackpad.StoreIsOn(legacy));
    }

    [Fact]
    public void TouchSwitch_KeyFlips_SwitchChangeWins()
    {
        TouchSwitch.Reset();
        const string t = "Wacom CTH-461";
        TouchSwitch.Seen(t, false);
        Assert.False(TouchSwitch.IsOn(t, false));
        Assert.True(TouchSwitch.Toggle(t));           // key: on
        TouchSwitch.Seen(t, false);                   // same switch value: the key's choice stays
        Assert.True(TouchSwitch.IsOn(t, false));
        Assert.False(TouchSwitch.Toggle(t));          // key again: off
        Assert.True(TouchSwitch.Toggle(t));
        TouchSwitch.Seen(t, true);                    // the user changed the switch: it wins
        Assert.True(TouchSwitch.IsOn(t, true));
        TouchSwitch.Seen(t, false);
        Assert.False(TouchSwitch.IsOn(t, false));
        Assert.True(TouchSwitch.IsOn("Wacom PTH-651", true));   // other tablets untouched
        TouchSwitch.Reset();
    }

    [Fact]
    public void TouchOnOff_ReadsWritesAndDescribes()
    {
        var store = AuxKeyBinding.MakeBinding(AuxBinding.TouchToggle)!;
        Assert.Equal(OpenTabletArtist.Domain.TouchToggleBinding.BindingTypeName, store.Path);
        Assert.Equal(typeof(OpenTabletArtist.Dynamics.TouchToggleBinding).FullName, store.Path);
        Assert.Equal(AuxBinding.TouchToggle, AuxKeyBinding.ReadBinding(store));
        Assert.True(AuxBinding.TouchToggle.IsBound);
        Assert.Equal("Touch on/off", AuxKeyBinding.Describe(AuxBinding.TouchToggle));
    }

    [Fact]
    public void TouchTab_OnlyForTouchTablets()
    {
        Assert.False(new BindingEditorViewModel(AuxBinding.Unbound, "Pen button 1").ShowTouchTab);
        using (BindingEditorContext.TouchTabletScope(true))
        {
            var vm = new BindingEditorViewModel(AuxBinding.TouchToggle, "ExpressKey 1");
            Assert.True(vm.ShowTouchTab);
            Assert.True(vm.IsTouchTab);
            Assert.True(vm.CanSave);
            vm.SaveCommand.Execute(null);
            Assert.Equal(AuxBinding.TouchToggle, vm.Result);
            using (BindingEditorContext.KeysOnlyScope())
                Assert.False(new BindingEditorViewModel(AuxBinding.Unbound, "Ring mode 1").ShowTouchTab);
        }
        Assert.False(BindingEditorContext.TouchTablet);
    }
}
