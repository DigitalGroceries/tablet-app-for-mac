using System.Linq;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Output;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>V30 build 3 (task list L6.8, Digital Groceries 24 Nov): after Restore factory settings the ExpressKeys get Digital Groceries' factory set instead
/// of staying "Not set up": Undo, Redo, Brush (B), Eye dropper (I), Move (V), Pan (Space, held).</summary>
public class FactoryExpressKeysTests
{
    private static Profile Profile(string tablet, int keys)
    {
        var p = new Profile
        {
            Tablet = tablet,
            OutputMode = new PluginSettingStore(typeof(AbsoluteMode)),
            Filters = new PluginSettingStoreCollection(),
            BindingSettings = new BindingSettings { PenButtons = new PluginSettingStoreCollection(), AuxButtons = new PluginSettingStoreCollection() },
        };
        for (int i = 0; i < keys; i++) p.BindingSettings.AuxButtons.Add(null!);
        return p;
    }

    [Fact]
    public void Six_keys_get_Digital_Groceries_factory_set_in_order()
    {
        var merged = FactorySettingsMerge.Apply(null, new Settings { Profiles = new ProfileCollection { Profile("Wacom PTK-440", 6) } });
        var aux = Assert.Single(merged.Profiles).BindingSettings.AuxButtons;
        Assert.Equal(new[] { "Application+Z", "Application+Shift+Z", "B", "I", "V", "Space" },
            aux.Select(s => AuxKeyBinding.FormatKeys(AuxKeyBinding.ReadCombo(s)!)).ToArray());
        Assert.Equal(0, OpenTabletArtist.ViewModels.MainViewModel.KeysNotSetUp(merged.Profiles[0]));   // Home: nothing "not set up"
    }

    [Fact]
    public void Fewer_keys_get_the_list_from_the_top_and_keys_after_six_stay_as_they_were()
    {
        var merged = FactorySettingsMerge.Apply(null, new Settings { Profiles = new ProfileCollection { Profile("Four", 4), Profile("Eight", 8) } });
        Assert.Equal(new[] { "Application+Z", "Application+Shift+Z", "B", "I" },
            merged.Profiles[0].BindingSettings.AuxButtons.Select(s => AuxKeyBinding.FormatKeys(AuxKeyBinding.ReadCombo(s)!)).ToArray());
        var eight = merged.Profiles[1].BindingSettings.AuxButtons;
        Assert.Equal("Space", AuxKeyBinding.FormatKeys(AuxKeyBinding.ReadCombo(eight[5])!));
        Assert.Null(eight[6]); Assert.Null(eight[7]);
    }

    [Fact]
    public void A_tablet_with_no_ExpressKeys_is_left_alone()
    {
        var merged = FactorySettingsMerge.Apply(null, new Settings { Profiles = new ProfileCollection { Profile("Pad-less", 0) } });
        Assert.Empty(merged.Profiles[0].BindingSettings.AuxButtons);
    }
}
