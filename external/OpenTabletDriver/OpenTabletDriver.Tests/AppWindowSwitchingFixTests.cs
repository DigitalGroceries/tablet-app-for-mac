using System.IO;
using OpenTabletDriver.Desktop.Interop.Input;
using Xunit;

namespace OpenTabletDriver.Tests
{
    /// <summary>Version 29 (#85): the activator runs only while the app's "App window switching fix" is On.</summary>
    public class AppWindowSwitchingFixTests
    {
        [Fact]
        public void IsOn_OnlyWhileTheAppsFlagFileExists()
        {
            var old = AppWindowSwitchingFix.FlagPath;
            var dir = Path.Combine(Path.GetTempPath(), "awsf-test-" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            try
            {
                AppWindowSwitchingFix.FlagPath = Path.Combine(dir, "app-window-switching-fix");
                Assert.False(AppWindowSwitchingFix.IsOn);
                File.WriteAllText(AppWindowSwitchingFix.FlagPath, "on");
                Assert.True(AppWindowSwitchingFix.IsOn);
                File.Delete(AppWindowSwitchingFix.FlagPath);
                Assert.False(AppWindowSwitchingFix.IsOn);
            }
            finally
            {
                AppWindowSwitchingFix.FlagPath = old;
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void TheDefaultFlag_IsInTheSharedOpenTabletArtistTempFolder()
        {
            Assert.EndsWith(Path.Combine("OpenTabletArtist", "app-window-switching-fix"), AppWindowSwitchingFix.FlagPath);
        }
    }
}
