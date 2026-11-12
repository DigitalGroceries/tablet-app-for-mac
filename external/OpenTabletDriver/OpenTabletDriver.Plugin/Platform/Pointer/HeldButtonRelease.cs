using System;

namespace OpenTabletDriver.Plugin.Platform.Pointer
{
    /// <summary>
    /// Tablet App for Mac (#73): lets go of the buttons a pointer still holds, once, with an honest log line for
    /// every outcome. Never throws: it runs while a pointer is being dropped, and a failure there must not stop
    /// the rest of the shutdown (it is logged, not hidden).
    /// </summary>
    public static class HeldButtonRelease
    {
        /// <returns>True when the pointer held buttons and they were released; false when nothing was held,
        /// the pointer can't hold buttons, or the release failed (the failure is logged as a warning).</returns>
        public static bool TryRelease(object? pointer, string group, string reason)
        {
            if (pointer is not IHeldButtonReleaser releaser)
                return false;
            try
            {
                if (!releaser.HasHeldButtons)
                    return false;
                releaser.ReleaseHeldButtons();
                Log.Write(group, $"Released the mouse buttons that were held ({reason})", LogLevel.Info);
                return true;
            }
            catch (Exception e)
            {
                Log.Write(group, $"Couldn't release the held mouse buttons ({reason}): {e.Message}", LogLevel.Warning);
                return false;
            }
        }
    }
}
