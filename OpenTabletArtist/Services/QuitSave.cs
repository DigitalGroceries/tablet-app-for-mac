using System;
using System.Threading.Tasks;
using OpenTabletArtist.Helpers;

namespace OpenTabletArtist.Services;

/// <summary>Version 2: what "Save" means when quitting. Quit always completes. The settings go to the daemon when it
/// answers in time; otherwise they are written to the local unsaved-changes file, which the next launch restores,
/// so nothing is lost and nothing waits on a daemon that is not there.</summary>
internal static class QuitSave
{
    public static readonly TimeSpan SaveTimeout = TimeSpan.FromSeconds(3);

    /// <returns>False only when the user chose Cancel.</returns>
    public static async Task<bool> ResolveAsync(UnsavedSettingsChoice choice, bool daemonConnected,
        Func<Task<bool>> saveToDaemon, Func<bool> writeLocal, Action discard, Action<string> warn, TimeSpan? timeout = null)
    {
        if (choice == UnsavedSettingsChoice.Cancel) return false;
        if (choice == UnsavedSettingsChoice.Continue) { discard(); return true; }

        bool saved = false;
        if (daemonConnected)
        {
            try
            {
                var save = saveToDaemon();
                if (await Task.WhenAny(save, Task.Delay(timeout ?? SaveTimeout)).ConfigureAwait(true) == save)
                    saved = await save.ConfigureAwait(true);
                else
                {
                    warn("Quit: saving to the daemon timed out; keeping the changes in the local file instead.");
                    _ = save.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                }
            }
            catch (Exception ex) { warn($"Quit: saving to the daemon failed: {ex.Message}"); }
        }
        if (!saved)
        {
            try { if (!writeLocal()) warn("Quit: the local unsaved-changes file could not be written."); }
            catch (Exception ex) { warn($"Quit: the local unsaved-changes file could not be written: {ex.Message}"); }
        }
        return true;
    }
}
