using OpenTabletArtist.Services;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Issue #69: Mapping writes AbsoluteModeSettings.Tablet.Rotation; Mouse-like reads
/// RelativeModeSettings.RelativeRotation. OtaSettingsPolicy.Prepare copies the angle on every send.</summary>
public class RelativeRotationSyncTests
{
    static Profile Profile(float rotation, RelativeModeSettings? relative)
    {
        return new Profile
        {
            Tablet = "Wacom PTK-440",
            OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)),
            AbsoluteModeSettings = new AbsoluteModeSettings
            {
                Display = new AreaSettings(),
                Tablet = new AreaSettings { Rotation = rotation },
            },
            RelativeModeSettings = relative,
        };
    }

    static void Prepare(Profile p) =>
        OtaSettingsPolicy.Prepare(new Settings { Profiles = new ProfileCollection { p } }, ownedDaemon: false);

    [Fact]
    public void Syncs_absolute_tablet_rotation_of_180_into_relative_rotation()
    {
        var p = Profile(180, RelativeModeSettings.GetDefaults());
        Assert.Equal(0, p.RelativeModeSettings!.RelativeRotation);
        Prepare(p);
        Assert.Equal(180, p.RelativeModeSettings.RelativeRotation);
    }

    [Fact]
    public void Creates_relative_defaults_when_null()
    {
        var p = Profile(90, null);
        Assert.Null(p.RelativeModeSettings);
        Prepare(p);
        Assert.NotNull(p.RelativeModeSettings);
        Assert.Equal(90, p.RelativeModeSettings.RelativeRotation);
        Assert.Equal(10, p.RelativeModeSettings.XSensitivity);
        Assert.Equal(10, p.RelativeModeSettings.YSensitivity);
    }

    [Fact]
    public void No_change_when_equal()
    {
        var rel = RelativeModeSettings.GetDefaults();
        rel.RelativeRotation = 180;
        rel.XSensitivity = 7;
        var p = Profile(180, rel);
        Prepare(p);
        Assert.Same(rel, p.RelativeModeSettings);
        Assert.Equal(180, p.RelativeModeSettings!.RelativeRotation);
        Assert.Equal(7, p.RelativeModeSettings.XSensitivity);
    }
}
