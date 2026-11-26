using System;

namespace OpenTabletArtist.Services;

/// <summary>
/// V28 #80 Option A. Digital Groceries, 16 Nov 2026: "let the app always save and restore the last session settings like the
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
        /// <summary>Nothing to save by itself: the switch is off, nothing is unsaved, or the unsaved state isn't this
        /// session's own edits (see <see cref="AutoSaveGate"/>), so it stays for the user's Save or Revert.</summary>
        Nothing,
        /// <summary>Unsaved changes, but not now: settings busy, a typed value not yet entered, the driver not
        /// editable, or an application's or extra pen's own settings on the driver. Try again after <see cref="Delay"/>.</summary>
        Wait,
        SaveNow,
    }

    /// <param name="blocked">Settings busy, a typed value not yet entered, editing not possible right now, or an
    /// application's / extra pen's own settings on the driver (saving then would store those instead of All).</param>
    /// <param name="held">The unsaved state isn't this session's own edits (<see cref="AutoSaveGate.Holds"/>).</param>
    public static Step Next(bool enabled, SettingsSaveState state, bool blocked, bool held = false) =>
        !enabled || held || state != SettingsSaveState.Unsaved ? Step.Nothing
        : blocked ? Step.Wait
        : Step.SaveNow;

    /// <summary>The footer's "Click Save to keep them" line: shown whenever unsaved changes will not be saved by
    /// themselves (switch off, or held).</summary>
    public static bool ShowSaveHint(bool enabled, SettingsSaveState state, bool held = false) =>
        state == SettingsSaveState.Unsaved && (!enabled || held);
}

/// <summary>
/// V28 #80 (review before release): auto-save only saves what the user changed in this session. Unsaved state that was
/// already there when the app connected — changes kept from the last quit (the unsaved snapshot), a driver that outlived
/// the app with an application's or extra pen's settings still on it — is left for the user's Save or Revert, as before
/// Version 28, because saving it could store an application's bindings as the All settings. The gate opens once the
/// settings have been seen saved (equal to the file) since the last connect, and closes on a disconnect or reload.
/// </summary>
public sealed class AutoSaveGate
{
    public bool Armed { get; private set; }

    public void Observe(SettingsSaveState state)
    {
        if (state == SettingsSaveState.Saved) Armed = true;
        else if (state is SettingsSaveState.None or SettingsSaveState.Disconnected) Armed = false;
    }

    /// <param name="snapshotPending">Changes kept from the last quit are waiting (the unsaved snapshot file exists): they
    /// are the user's to keep or discard, never saved by themselves.</param>
    public bool Holds(bool snapshotPending) => !Armed || snapshotPending;
}

