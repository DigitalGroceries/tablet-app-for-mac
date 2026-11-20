using System.Linq;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Output;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>V29 #149 (Zak 20 Nov, design 1: one Touch Ring switch; On shows the plain Clockwise / Anticlockwise /
/// Centre button rows at once, already filled in after a factory restore; Wacom's 4 modes stay an optional extra) and
/// #150 (the rows showed "BrightnessUp").</summary>
public class RingPlainRowsTests
{
    private static Profile WithRing(int ringButtons)
    {
        var wheel = new WheelBindingSettings { ClockwiseActivationThreshold = 1, CounterClockwiseActivationThreshold = 1 };
        for (int i = 0; i < ringButtons; i++) wheel.WheelButtons.Add(null);
        return new Profile
        {
            Tablet = "Wacom PTK-440",
            OutputMode = new PluginSettingStore(typeof(AbsoluteMode)),
            Filters = new PluginSettingStoreCollection(),
            BindingSettings = new BindingSettings { PenButtons = new PluginSettingStoreCollection(), WheelBindings = { wheel } },
        };
    }

    [Fact]
    public void FactoryRestore_PutsTheRingInItsFourModes_CentreButtonSwitches()
    {
        var merged = FactorySettingsMerge.Apply(null, new Settings { Profiles = new ProfileCollection { WithRing(1) } });
        var w = merged.Profiles[0].BindingSettings.WheelBindings[0];
        Assert.True(RingModeBinding.IsActive(w));
        Assert.Equal(new[] { "Rotate", "Zoom", "Brush size", "Brush hardness" },
            RingModeBinding.Read(w).Select(m => m.Name).ToArray());
        Assert.Equal(RingModeBinding.NextTypeName, w.WheelButtons[0]!.Path);   // the centre button switches modes
    }

    [Fact]
    public void FactoryRestore_LeavesATouchStripAlone()
    {
        var p = WithRing(1);
        var strip = new WheelBindingSettings { ClockwiseActivationThreshold = 1, CounterClockwiseActivationThreshold = 1 };
        p.BindingSettings.WheelBindings.Add(strip);   // no centre button: a touch strip
        Assert.True(FactorySettingsMerge.ApplyFactoryRingModes(p));
        Assert.Null(strip.ClockwiseRotation);
        Assert.Null(strip.CounterClockwiseRotation);
        Assert.False(FactorySettingsMerge.ApplyFactoryRingModes(p));   // already in modes: nothing more to do
    }

    [Theory]
    [InlineData("BrightnessUp", "Brightness up")]
    [InlineData("BrightnessDown", "Brightness down")]
    [InlineData("VolumeUp", "Volume up")]
    [InlineData("PlayPause", "Play / Pause")]
    public void MediaKeys_ShowTheirNames_NotTheDriversKeyName(string key, string shown)
    {
        Assert.Equal(shown, AuxKeyBinding.MediaKeyName(key));
        Assert.Equal(shown, AuxKeyBinding.Describe(new AuxBinding(AuxKind.Keyboard, new AuxCombo(false, false, false, key),
            AuxKeyBinding.None, AuxKeyBinding.None)));
        Assert.Null(AuxKeyBinding.MediaKeyName("A"));
    }

    /// <summary>V29 #158: there is no modes switch any more; only a ring whose plain rows were set up in OpenTabletDriver in a way
    /// modes can't show keeps its rows, and the line says so.</summary>
    [Fact]
    public void ARingThatKeptItsPlainRows_SaysWhy()
    {
        var editor = new RingModesEditor(0, RingModeBinding.Defaults, isOn: false, hasRingButton: true, canEdit: true,
            editBinding: null, setOn: null, changed: null);
        Assert.Contains("OpenTabletDriver", editor.Summary);
    }
}

/// <summary>V29 #153 (Zak 20 Nov): Volume, Brightness and Previous / next track picked straight from a ring mode's Function
/// menu, no key window.</summary>
public class RingFunctionPresetTests
{
    private static RingModeRow Row(RingMode mode, System.Func<System.Threading.Tasks.Task>? changed = null) =>
        new(1, mode, canEdit: true, editBinding: null, changed: changed ?? (() => System.Threading.Tasks.Task.CompletedTask));

    private const int Volume = 6, Brightness = 7, Tracks = 8;

    [Fact]
    public void PickingBrightness_SetsBothDirections_AndSaves()
    {
        int saves = 0;
        var row = Row(RingModeBinding.Defaults[1], () => { saves++; return System.Threading.Tasks.Task.CompletedTask; });
        row.ActionIndex = Brightness;
        var mode = row.ToMode();
        Assert.Equal("BrightnessUp", mode.Clockwise.Key);
        Assert.Equal("BrightnessDown", mode.CounterClockwise.Key);
        Assert.Equal(Brightness, row.ActionIndex);
        Assert.Equal("Brightness", row.ActionShort);
        Assert.Equal("Brightness up", row.Clockwise.Summary);
        Assert.True(saves >= 1);
    }

    [Fact]
    public void AModeAlreadySetToVolumeKeys_ShowsVolume()
    {
        var mode = new RingMode("Mine", new AuxCombo(false, false, false, "VolumeUp"), new AuxCombo(false, false, false, "VolumeDown"));
        Assert.Equal(Volume, Row(mode).ActionIndex);
    }

    [Fact]
    public void BackToKeyboardShortcut_FromAPreset_StartsWithEmptyKeys()
    {
        var row = Row(RingModeBinding.Defaults[1]);
        row.ActionIndex = Tracks;
        row.ActionIndex = 0;
        Assert.Equal(0, row.ActionIndex);
        Assert.False(row.ToMode().Clockwise.IsBound);
        Assert.False(row.ToMode().CounterClockwise.IsBound);
    }

    [Fact]
    public void TheDefaultZoomMode_StaysAKeyboardShortcut()
    {
        Assert.Equal(0, Row(RingModeBinding.Defaults[1]).ActionIndex);
    }
}
