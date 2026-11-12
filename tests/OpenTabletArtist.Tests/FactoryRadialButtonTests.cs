using System.Linq;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Output;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletDriver.Plugin.Tablet;
using OtdInterop;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>TASKS #56 (Zak 17:12): when settings go back to the defaults, pen button 2 is the Radial menu (with its
/// filter in the profile) and the radial menu switch is on. Button 1 and the other bindings are untouched.</summary>
public class FactoryRadialButtonTests
{
    private const string Adaptive = "OpenTabletDriver.Desktop.Binding.AdaptiveBinding";

    private static PluginSettingStore Plain(string path) => AuxKeyBinding.NewStore(path);

    private static Profile Profile(string tablet, int buttons)
    {
        var p = new Profile
        {
            Tablet = tablet,
            OutputMode = new PluginSettingStore(typeof(AbsoluteMode)),
            Filters = new PluginSettingStoreCollection(),
            BindingSettings = new BindingSettings { PenButtons = new PluginSettingStoreCollection() },
        };
        for (int i = 0; i < buttons; i++) p.BindingSettings.PenButtons.Add(Plain(Adaptive));
        return p;
    }

    [Fact]
    public void FactoryRestore_PutsTheRadialMenuOnButton2_AndKeepsButton1()
    {
        var after = new Settings { Profiles = new ProfileCollection { Profile("Wacom PTH-651", 2) } };
        var merged = FactorySettingsMerge.Apply(null, after);
        var p = Assert.Single(merged.Profiles);
        Assert.Equal(Adaptive, p.BindingSettings.PenButtons[0]!.Path);                 // button 1 as it was
        Assert.True(RadialMenuBinding.Is(p.BindingSettings.PenButtons[1]));            // button 2 = Radial menu
        Assert.True(p.BindingSettings.PenButtons[1]!.Enable);
        Assert.Single(p.Filters.OfType<PluginSettingStore>(), f => f.Path == RadialMenuBinding.FilterTypeName && f.Enable);
        Assert.Equal(8, RadialMenuBinding.ReadMenu(p.BindingSettings.PenButtons[1]).Slices.Count(s => s.IsSet));
    }

    [Fact]
    public void ThirdButton_AndAPenWithOneButton_AreLeftAlone()
    {
        var after = new Settings { Profiles = new ProfileCollection { Profile("A", 3), Profile("B", 1) } };
        var merged = FactorySettingsMerge.Apply(null, after);
        Assert.Equal(Adaptive, merged.Profiles[0].BindingSettings.PenButtons[2]!.Path);
        Assert.Equal(Adaptive, merged.Profiles[1].BindingSettings.PenButtons[0]!.Path);
        Assert.False(RadialMenuBinding.IsUsed(merged.Profiles[1]));
        Assert.Empty(merged.Profiles[1].Filters);
    }

    [Fact]
    public void ANotDetectedTablet_KeptFromBefore_AlsoGetsIt()
    {
        var before = new Settings { Profiles = new ProfileCollection { Profile("Gone", 2) } };
        var merged = FactorySettingsMerge.Apply(before, new Settings { Profiles = new ProfileCollection() });
        Assert.True(RadialMenuBinding.Is(Assert.Single(merged.Profiles).BindingSettings.PenButtons[1]));
    }

    [Fact]
    public void ANormalSend_DoesNotTouchButton2()
    {
        var s = new Settings { Profiles = new ProfileCollection { Profile("Wacom PTH-651", 2) } };
        OtaSettingsPolicy.Prepare(s, ownedDaemon: false);
        Assert.Equal(Adaptive, s.Profiles[0].BindingSettings.PenButtons[1]!.Path);
    }

    [Fact]
    public void TheDaemonsFreshDefaults_HaveTheRadialMenuOnButton2()
    {
        var spec = new TabletSpecifications { Digitizer = new DigitizerSpecifications { Width = 100, Height = 50, MaxX = 1000, MaxY = 500 }, Pen = new PenSpecifications { ButtonCount = 3 } };
        var b = BindingSettings.GetDefaults(spec);
        Assert.Equal(Adaptive, b.PenButtons[0]!.Path);
        Assert.True(RadialMenuBinding.Is(b.PenButtons[1]));
        Assert.Equal(Adaptive, b.PenButtons[2]!.Path);
        var back = SettingsCodec.Clone(new Settings { Profiles = new ProfileCollection { new Profile { Tablet = "T", OutputMode = new PluginSettingStore(typeof(AbsoluteMode)), BindingSettings = b } } });
        Assert.True(RadialMenuBinding.Is(back.Profiles[0].BindingSettings.PenButtons[1]));   // survives save and load
    }
}
