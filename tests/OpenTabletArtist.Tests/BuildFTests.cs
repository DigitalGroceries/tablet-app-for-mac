using System.Collections.Generic;
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

/// <summary>Build F review fixes: ring-mode switch honesty, ring-mode turn-off without a backup, and the
/// All displays refit once the tablet's size is known.</summary>
[Collection(DisplayEnumeratorCollection.Name)]
public class BuildFTests
{
    private static RingMode Mapped(string name) =>
        new(name, new AuxCombo(false, false, false, "RightBracket"), new AuxCombo(false, false, false, "LeftBracket"));

    private static WheelBindingSettings Wheel(int buttons) => new()
    {
        WheelButtons = new PluginSettingStoreCollection(Enumerable.Range(0, buttons).Select(_ => (PluginSettingStore)null!)),
    };

    [Fact]
    public void CanApply_NeedsARingButtonAndAMappedModeThatIsOn()
    {
        Assert.True(RingModeBinding.CanApply(Wheel(1), new[] { Mapped("A") }));
        Assert.False(RingModeBinding.CanApply(Wheel(0), new[] { Mapped("A") }));
        Assert.False(RingModeBinding.CanApply(Wheel(1), new[] { Mapped("A") with { Enabled = false } }));
        Assert.False(RingModeBinding.CanApply(Wheel(1), new[] { new RingMode("Empty", AuxCombo.Unbound, AuxCombo.Unbound) }));
    }

    [Fact]
    public void Remove_WithoutABackup_ClearsOnlyTheRingModeSlots()
    {
        var w = Wheel(1);
        Assert.True(RingModeBinding.Apply(w, new[] { Mapped("A") }));
        Assert.True(RingModeBinding.IsActive(w));
        RingModeBinding.Remove(w, haveBackup: false, null, null, null);
        Assert.False(RingModeBinding.IsActive(w));
        Assert.Null(w.ClockwiseRotation);
        Assert.Null(w.WheelButtons[0]);

        // A ring button bound to something else (not a ring-mode binding) is left alone.
        var other = new PluginSettingStore("OpenTabletDriver.Desktop.Binding.KeyBinding") { Path = "OpenTabletDriver.Desktop.Binding.KeyBinding" };
        var w2 = Wheel(1);
        RingModeBinding.Apply(w2, new[] { Mapped("A") });
        w2.WheelButtons[0] = other;
        RingModeBinding.Remove(w2, haveBackup: false, null, null, null);
        Assert.Same(other, w2.WheelButtons[0]);
        Assert.False(RingModeBinding.IsActive(w2));
    }

    [Fact]
    public void Remove_WithABackup_PutsThePlainBindingsBack()
    {
        var cw = new PluginSettingStore("X") { Path = "X" };
        var btn = new PluginSettingStore("B") { Path = "B" };
        var w = Wheel(1);
        RingModeBinding.Apply(w, new[] { Mapped("A") });
        RingModeBinding.Remove(w, haveBackup: true, cw, null, btn);
        Assert.Same(cw, w.ClockwiseRotation);
        Assert.Null(w.CounterClockwiseRotation);
        Assert.Same(btn, w.WheelButtons[0]);
    }

    [Fact]
    public void RingModesSwitch_FlipsBack_WhenNothingCouldBeApplied()
    {
        var calls = 0;
        var editor = new RingModesEditor(0, new[] { Mapped("A") }, isOn: false, hasRingButton: true, canEdit: true,
            editBinding: null, setOn: (_, _) => { calls++; return Task.FromResult(false); }, changed: null);
        editor.IsOn = true;
        Assert.Equal(1, calls);
        Assert.False(editor.IsOn);   // was left "on" in build E
    }

    [Fact]
    public void RingModesSwitch_StaysOn_WhenApplied()
    {
        var editor = new RingModesEditor(0, new[] { Mapped("A") }, isOn: false, hasRingButton: true, canEdit: true,
            editBinding: null, setOn: (_, _) => Task.FromResult(true), changed: null);
        editor.IsOn = true;
        Assert.True(editor.IsOn);
    }

    private sealed class FixedDisplays(params DisplayInfo[] displays) : IDisplayEnumerator
    {
        public IReadOnlyList<DisplayInfo> Enumerate() => displays;
    }

    // The 20 Oct case end to end: All displays was chosen while the tablet wasn't detected (stale 113 x 64
    // area); when the page sees the tablet's size it fits the area without the artist doing anything.
    [Fact]
    public void AllDisplaysChosenBeforeDetection_IsFittedOnceTheTabletIsKnown()
    {
        var displays = new[]
        {
            new DisplayInfo(1, "Laptop", 1496, 967, 0, 0, true),
            new DisplayInfo(2, "TV", 3360, 1890, 337, -1923, false),
        };
        DisplayEnumerator.Use(new FixedDisplays(displays));
        try
        {
            const string tablet = "BuildF Refit Tablet";
            var settings = new Settings { Profiles = new ProfileCollection { new Profile
            {
                OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)),
                Tablet = tablet,
                AbsoluteModeSettings = new AbsoluteModeSettings
                {
                    Display = new AreaSettings(),
                    Tablet = new AreaSettings { Width = 113.3f, Height = 63.7f, X = 56.66f, Y = 31.87f },
                },
            } } };
            var profile = settings.Profiles.First();
            DisplayMappingApplier.ApplyAllDisplays(profile, null, displays);   // what E did with no tablet size
            AppSettings.Set($"MapAllDisplays:{tablet}", "1");
            AppSettings.Set("AllDisplaysForceProportions", "1");

            var device = new FakeDeviceData();
            device.Digitizers[tablet] = (157.48f, 98.425f);
            int applies = 0;
            var vm = new TabletDetailViewModel(profile, settings,
                applyAction: _ => { applies++; return Task.FromResult(SettingsApplyOutcome.Live); },
                isDetected: () => true, deviceData: device);

            Assert.Equal(1, applies);
            Assert.Equal(125.9f, profile.AbsoluteModeSettings!.Tablet.Width, 1);
            Assert.Equal(98.4f, profile.AbsoluteModeSettings!.Tablet.Height, 1);

            vm.RefreshDetectionStatus();   // already fitted: no further apply
            Assert.Equal(1, applies);
        }
        finally
        {
            DisplayEnumerator.Use(null);
        }
    }
}
