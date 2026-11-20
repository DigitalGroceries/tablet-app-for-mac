using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OtdInterop;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>V29 (Zak, 20 Nov 2026): "In Tablet / Functions page of the app there are two On / Off buttons for the touch ring.
/// Currently even if the top button is On the bottom button needs to be On too in order for the touch ring menu to show."
/// And: "the Factory Settings restores the app to touch ring being Off". A ring with a centre button now always works in its
/// modes, like Wacom's Touch Ring: a ring still on its plain rows moves to modes by itself when nothing would be lost.</summary>
[Collection(DisplayEnumeratorCollection.Name)]
public class RingOneSwitchTests
{
    private static AuxCombo K(string key, bool cmd = false) => new(false, false, false, key, cmd);

    private static WheelBindingSettings PlainWheel(PluginSettingStore? cw = null, PluginSettingStore? ccw = null,
        PluginSettingStore? button = null) => new()
    {
        WheelButtons = new PluginSettingStoreCollection(new[] { button! }),
        ClockwiseRotation = cw!, CounterClockwiseRotation = ccw!,
        ClockwiseActivationThreshold = 5, CounterClockwiseActivationThreshold = 5,
    };

    [Fact]
    public void AnEmptyPlainRing_GetsTheModesAsTheyAre()
    {
        var modes = RingModeBinding.ForPlainRing(PlainWheel(), RingModeBinding.Defaults.ToList());
        Assert.NotNull(modes);
        Assert.Equal(RingModeBinding.Defaults.Select(m => m.Name), modes!.Select(m => m.Name));
    }

    [Fact]
    public void ARingWithItsOwnKeys_KeepsThemAsMode1()
    {
        var w = PlainWheel(AuxKeyBinding.MakeBinding(K("VolumeUp")), AuxKeyBinding.MakeBinding(K("VolumeDown")));
        var modes = RingModeBinding.ForPlainRing(w, RingModeBinding.Defaults.ToList())!;
        Assert.Equal(RingModeBinding.MaxModes, modes.Count);
        Assert.Equal(RingModeBinding.PlainKeysModeName, modes[0].Name);
        Assert.Equal("VolumeUp", modes[0].Clockwise.Key);
        Assert.Equal("VolumeDown", modes[0].CounterClockwise.Key);
        Assert.Equal(0f, modes[0].StepDegrees);                                  // every ring step, as the plain row did
        Assert.Equal(new[] { "Rotate", "Zoom", "Brush size" }, modes.Skip(1).Select(m => m.Name));
    }

    [Fact]
    public void ARingThatWouldLoseSomething_StaysAsItIs()
    {
        var defaults = RingModeBinding.Defaults.ToList();
        Assert.Null(RingModeBinding.ForPlainRing(PlainWheel(button: AuxKeyBinding.MakeBinding(K("Space"))), defaults));
        Assert.Null(RingModeBinding.ForPlainRing(PlainWheel(cw: AuxKeyBinding.MakeMouseBinding("Left")), defaults));
        Assert.Null(RingModeBinding.ForPlainRing(new WheelBindingSettings(), defaults));            // no centre button
        var active = PlainWheel();
        Assert.True(RingModeBinding.Apply(active, defaults));
        Assert.Null(RingModeBinding.ForPlainRing(active, defaults));                                // already in modes
    }

    [Fact]
    public async Task TheTabletPage_PutsAPlainRingInItsModes_WithOneSwitch()
    {
        var tablet = "TestTablet-RingOneSwitch-" + Guid.NewGuid().ToString("N")[..8];
        var profile = new Profile { Tablet = tablet, OutputMode = null! };
        profile.BindingSettings.WheelBindings.Add(PlainWheel());          // what a factory restore leaves: On, nothing on it
        var settings = new Settings { Profiles = new ProfileCollection { profile } };
        var vm = new TabletDetailViewModel(profile, settings, applyAction: _ => Task.FromResult(SettingsApplyOutcome.Live));
        Assert.True(vm.WheelEnabled);
        for (int i = 0; i < 50 && !vm.Wheels[0].RingModes!.IsOn; i++) await Task.Delay(20);

        Assert.True(vm.Wheels[0].RingModes!.IsOn);
        Assert.True(RingModeBinding.IsActive(settings.Profiles[0].BindingSettings.WheelBindings[0]));
        Assert.Equal(4, RingModeBinding.Read(settings.Profiles[0].BindingSettings.WheelBindings[0]).Count);
    }

    [Fact]
    public void TheSecondSwitch_ShowsOnlyWhileModesAreOff()
    {
        var xaml = System.IO.File.ReadAllText(System.IO.Path.Combine(RepoRoot(), "OpenTabletArtist", "Views", "TabletDetailView.axaml"));
        int at = xaml.IndexOf("x:Name=\"RingModesSwitch\"", StringComparison.Ordinal);
        Assert.True(at > 0);
        var tag = xaml.Substring(at, xaml.IndexOf("/>", at, StringComparison.Ordinal) - at);
        Assert.Contains("IsVisible=\"{Binding !RingModes.IsOn", tag);
    }

    private static string RepoRoot()
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "OpenTabletArtist.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
