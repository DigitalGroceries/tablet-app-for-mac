using OpenTabletArtist.Services;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Interop.Input;
using OpenTabletDriver.Desktop.Output;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OtdInterop;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// Zak build 6: Restore factory settings left All Double Click Fix at 60 when the tablet was not
/// on the daemon's GetDefaults list (not detected) and/or the All stash still held 60.
/// </summary>
public class FactorySettingsMergeTests
{
    private static Profile Profile(string tablet, int? dcf) => new()
    {
        Tablet = tablet,
        OutputMode = new PluginSettingStore(typeof(AbsoluteMode)),
        BindingSettings = new BindingSettings { DoubleClickFix = dcf },
    };

    [Fact]
    public void Apply_ReAttachesNotDetectedTablet_WithFactoryDoubleClickFix_AndSyncsStash()
    {
        var tablet = "Wacom PTH-651";
        AppSettings.Set(PerAppBindingRuntime.AllBindingsKey(tablet), ""); // clear
        PerAppBindingRuntime.SyncAllBindings(tablet, new BindingSettings { DoubleClickFix = 60 });

        var before = new Settings
        {
            Profiles = new ProfileCollection { Profile(tablet, 60) },
        };
        // Daemon ResetSettings with no connected tablets → empty Profiles.
        var after = new Settings { Profiles = new ProfileCollection() };

        var merged = FactorySettingsMerge.Apply(before, after);

        var profile = Assert.Single(merged.Profiles);
        Assert.Equal(tablet, profile.Tablet);
        Assert.Equal(ClickJitterGate.UiDefault, profile.BindingSettings.DoubleClickFix);

        var stash = PerAppBindingRuntime.StashedAllBindings(tablet);
        Assert.NotNull(stash);
        Assert.Equal(ClickJitterGate.UiDefault, stash!.DoubleClickFix);
    }

    [Fact]
    public void Apply_ConnectedTabletKeepsFactoryProfile_ButForcesDefaultDcfAndStash()
    {
        var tablet = "Wacom PTH-651";
        PerAppBindingRuntime.SyncAllBindings(tablet, new BindingSettings { DoubleClickFix = 60 });

        var before = new Settings { Profiles = new ProfileCollection { Profile(tablet, 60) } };
        var after = new Settings { Profiles = new ProfileCollection { Profile(tablet, ClickJitterGate.UiDefault) } };

        var merged = FactorySettingsMerge.Apply(before, after);

        var profile = Assert.Single(merged.Profiles);
        Assert.Equal(ClickJitterGate.UiDefault, profile.BindingSettings.DoubleClickFix);
        Assert.Equal(ClickJitterGate.UiDefault, PerAppBindingRuntime.StashedAllBindings(tablet)!.DoubleClickFix);
    }

    [Fact]
    public void UserDefault_Matches_SeesDoubleClickFixChange_ZakSaveAsDefaultThenEdit()
    {
        // Zak: Save as default at DCF=50, set DCF=60, Save → Revert to default must enable.
        UserDefaultSettings.Delete();
        var store = new PresetStore(AppLogBridge.Instance);
        try
        {
            var at50 = new Settings { Profiles = new ProfileCollection { Profile("Wacom PTH-651", 50) } };
            UserDefaultSettings.Save(at50, store);
            Assert.True(UserDefaultSettings.Matches(at50, store));

            var at60 = SettingsCodec.Clone(at50);
            at60.Profiles[0].BindingSettings.DoubleClickFix = 60;
            Assert.False(UserDefaultSettings.Matches(at60, store));
            Assert.False(SettingsCodec.Same(at50, at60));
        }
        finally
        {
            UserDefaultSettings.Delete();
        }
    }
}
