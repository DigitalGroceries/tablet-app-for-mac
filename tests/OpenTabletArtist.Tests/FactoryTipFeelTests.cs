using System;
using System.Linq;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Output;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>V29 #159 (Zak 20 Nov ~09:03: "Also make the default factory value for the Tip Feel to be 80").</summary>
public class FactoryTipFeelTests
{
    private static Profile NewProfile(string tablet) => new()
    {
        Tablet = tablet,
        OutputMode = new PluginSettingStore(typeof(AbsoluteMode)),
        BindingSettings = new BindingSettings(),
    };

    [Fact]
    public void TheFactoryCurve_ShowsTipFeel80()
    {
        Assert.Equal(80, FeelScale.FactoryTipFeel);
        Assert.Equal(80, FeelScale.ToDisplay(-PressureCurveSettings.Factory.Softness));
        Assert.True(PressureCurveSettings.Factory.Softness < 0);   // firmer than the straight curve
        Assert.Equal(PressureCurveSettings.Default with { Softness = PressureCurveSettings.Factory.Softness }, PressureCurveSettings.Factory);
    }

    [Fact]
    public void RestoreFactorySettings_WritesTipFeel80_EvenOverASavedSoftCurve()
    {
        var tablet = "Wacom PTK-440";
        var before = new Settings { Profiles = new ProfileCollection { NewProfile(tablet) } };
        var after = new Settings { Profiles = new ProfileCollection { NewProfile(tablet) } };
        PressureCurveProfile.Write(after, tablet,
            new PenDynamicsSettings(PressureCurveSettings.Default with { Softness = 0.6 }, 0.3, 0.2, true), enable: true);

        var merged = FactorySettingsMerge.Apply(before, after);

        var read = PressureCurveProfile.Read(merged, tablet);
        Assert.NotNull(read);
        Assert.True(read!.Value.Enabled);
        Assert.Equal(80, FeelScale.ToDisplay(-read.Value.Dynamics.Curve.Softness));
        Assert.Equal(0, read.Value.Dynamics.PressureSmoothing);
        Assert.Equal(0, read.Value.Dynamics.PositionSmoothing);
        Assert.False(PressureCurveProfile.ReadManual(merged.Profiles.First()).On);
    }

    [Fact]
    public void ANotDetectedTabletKeptByTheRestore_AlsoGetsTipFeel80()
    {
        var before = new Settings { Profiles = new ProfileCollection { NewProfile("Wacom CTH-461") } };
        var after = new Settings { Profiles = new ProfileCollection() };

        var merged = FactorySettingsMerge.Apply(before, after);

        var read = PressureCurveProfile.Read(merged, "Wacom CTH-461");
        Assert.NotNull(read);
        Assert.Equal(80, FeelScale.ToDisplay(-read!.Value.Dynamics.Curve.Softness));
    }

    [Fact]
    public void AnOrdinaryWrite_StillKeepsTheUsersOwnTipFeel()
    {
        var tablet = "Wacom PTK-440";
        var s = new Settings { Profiles = new ProfileCollection { NewProfile(tablet) } };
        PressureCurveProfile.Write(s, tablet,
            new PenDynamicsSettings(PressureCurveSettings.Default with { Softness = -FeelScale.FromDisplay(35) }, 0, 0, true), enable: true);
        Assert.Equal(35, FeelScale.ToDisplay(-PressureCurveProfile.Read(s, tablet)!.Value.Dynamics.Curve.Softness));
    }
}
