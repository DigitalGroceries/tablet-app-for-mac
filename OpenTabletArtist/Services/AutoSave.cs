using System;

namespace OpenTabletArtist.Services;

/// <summary>
/// V28 #80 Option A. Zak, 16 Nov 2026: "let the app always save and restore the last session settings like the
/// Wacom". Every change already reaches the driver at once; with this on, it is also saved by itself a moment later,
/// so it is still there after a restart and "Unsaved changes" no longer waits for the Save button.
/// On by default. Settings › System has a switch for anyone who prefers to keep changes only when they press Save.
/// </summary>
public static class AutoSave
{
    public const string Key = "AutoSave";

    /// <summary>How long after the last change the save happens (a slider drag or a run of key edits is one save).</summary>
    public static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(800);

    /// <summary>Raised when the switch changes, so the shell can save what is waiting right away.</summary>
    public static event Action? Changed;

    public static bool Enabled
    {
        get => AppSettings.Get(Key) != "false";
        set
        {
            if (value == Enabled) return;
            AppSettings.Set(Key, value ? "true" : "false");
            AppLog.Info($"Auto-save: switched {(value ? "on" : "off")}.");
            Changed?.Invoke();
        }
    }

    public enum Step
    {
        /// <summary>Nothing to save (switch off, or no unsaved changes).</summary>
        Nothing,
        /// <summary>Unsaved changes, but not now: settings busy, a typed value not yet entered, the driver not
        /// editable, or an application's or extra pen's settings on the driver. Try again after <see cref="Delay"/>.</summary>
        Wait,
        SaveNow,
    }

    /// <param name="blocked">Settings busy, a typed value not yet entered, editing not possible right now, or an
    /// application's / extra pen's own settings on the driver (saving then would store those instead of All).</param>
    public static Step Next(bool enabled, SettingsSaveState state, bool blocked) =>
        !enabled || state != SettingsSaveState.Unsaved ? Step.Nothing
        : blocked ? Step.Wait
        : Step.SaveNow;

    /// <summary>The footer's "Click Save to keep them" line only makes sense when changes are not saved by themselves.</summary>
    public static bool ShowSaveHint(bool enabled, SettingsSaveState state) => !enabled && state == SettingsSaveState.Unsaved;
}
