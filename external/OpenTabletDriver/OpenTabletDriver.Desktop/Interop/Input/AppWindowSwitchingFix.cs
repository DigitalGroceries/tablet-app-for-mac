using System;
using System.IO;

namespace OpenTabletDriver.Desktop.Interop.Input
{
    /// <summary>
    /// Version 29 (Zak, 17 Nov 2026; TASKS #85): "App window switching fix" on the app's Advanced page, Off by default.
    /// Only while it is On does a pen click first ask macOS to activate the app under the pen
    /// (<see cref="MacOSAppActivator"/>). Zak's recordings showed macOS brings the window forward by itself
    /// (~0.1 s after the tip lifts), while the activator, where it runs, holds every click back about 1 second.
    /// The app writes <see cref="FlagPath"/> while the switch is On (the same way as the radial-menu-off file);
    /// no file means Off.
    /// </summary>
    public static class AppWindowSwitchingFix
    {
        /// <summary>Per-user temp folder shared with the app (same folder as the ring-mode file).</summary>
        public static string FlagPath { get; set; } =
            Path.Combine(Path.GetTempPath(), "OpenTabletArtist", "app-window-switching-fix");

        /// <summary>True while the app's switch is On. Never throws: any error counts as Off.</summary>
        public static bool IsOn
        {
            get
            {
                try { return File.Exists(FlagPath); }
                catch (Exception) { return false; }
            }
        }
    }
}
