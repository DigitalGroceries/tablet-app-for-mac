using Newtonsoft.Json;
using OpenTabletArtist.Services;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Output;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// V28 review before release: when the Mac quits with an application that has its own settings in front (say
/// Photoshop), the driver holds Photoshop's bindings. The unsaved snapshot written at that quit must keep the All
/// bindings, or the next launch would bring Photoshop's bindings back as All.
/// </summary>
public class QuitSnapshotAllBindingsTests
{
    [Fact]
    public void TheSnapshotDocument_GetsTheStashedAllBindings_OnlyForTheTabletWithTheOverlay()
    {
        const string tablet = "Wacom Test QuitSnapshot";
        var key = PerAppBindingRuntime.AllBindingsKey(tablet);
        var key2 = PerAppBindingRuntime.AllBindingsKey("Wacom Second Tablet");
        var before = AppSettings.Get(key);
        var before2 = AppSettings.Get(key2);
        try
        {
            AppSettings.Set(key, JsonConvert.SerializeObject(new BindingSettings { DoubleClickFix = 11 }));
            AppSettings.Set(key2, JsonConvert.SerializeObject(new BindingSettings { DoubleClickFix = 33 }));   // an older stash
            Settings Live() => new()
            {
                Profiles = new ProfileCollection
                {
                    new Profile { OutputMode = new PluginSettingStore(typeof(AbsoluteMode)), Tablet = tablet, BindingSettings = new BindingSettings { DoubleClickFix = 77 } },   // Photoshop's
                    new Profile { OutputMode = new PluginSettingStore(typeof(AbsoluteMode)), Tablet = "Wacom Second Tablet", BindingSettings = new BindingSettings { DoubleClickFix = 5 } },
                },
            };

            var snapshot = PerAppBindingRuntime.WithAllBindings(Live(), overlayTablet: tablet);
            Assert.Equal(11, snapshot.Profiles[0].BindingSettings.DoubleClickFix);   // All, not the app's
            Assert.Equal(5, snapshot.Profiles[1].BindingSettings.DoubleClickFix);    // another tablet: its older stash is not used

            var noOverlay = PerAppBindingRuntime.WithAllBindings(Live(), overlayTablet: null);
            Assert.Equal(77, noOverlay.Profiles[0].BindingSettings.DoubleClickFix);  // no overlay: nothing replaced
        }
        finally
        {
            if (before is null) AppSettings.Remove(key);
            else AppSettings.Set(key, before);
            if (before2 is null) AppSettings.Remove(key2);
            else AppSettings.Set(key2, before2);
        }
    }
}
