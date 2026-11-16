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
    public void TheSnapshotDocument_GetsTheStashedAllBindings_ForEachTablet()
    {
        const string tablet = "Wacom Test QuitSnapshot";
        var key = PerAppBindingRuntime.AllBindingsKey(tablet);
        var before = AppSettings.Get(key);
        try
        {
            AppSettings.Set(key, JsonConvert.SerializeObject(new BindingSettings { DoubleClickFix = 11 }));
            var live = new Settings
            {
                Profiles = new ProfileCollection
                {
                    new Profile { OutputMode = new PluginSettingStore(typeof(AbsoluteMode)), Tablet = tablet, BindingSettings = new BindingSettings { DoubleClickFix = 77 } },   // Photoshop's
                    new Profile { OutputMode = new PluginSettingStore(typeof(AbsoluteMode)), Tablet = "Wacom Without A Stash", BindingSettings = new BindingSettings { DoubleClickFix = 5 } },
                },
            };

            var snapshot = PerAppBindingRuntime.WithAllBindings(live);

            Assert.Equal(11, snapshot.Profiles[0].BindingSettings.DoubleClickFix);   // All, not the app's
            Assert.Equal(5, snapshot.Profiles[1].BindingSettings.DoubleClickFix);    // nothing stashed: left as it is
        }
        finally
        {
            if (before is null) AppSettings.Remove(key);
            else AppSettings.Set(key, before);
        }
    }
}
