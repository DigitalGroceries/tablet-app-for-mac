using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// V28 #80 Option A. Zak, 16 Nov 2026: "let the app always save and restore the last session settings like the
/// Wacom". Unsaved changes are saved by themselves a moment later, but never while that would save the wrong thing
/// (settings busy, a typed value not yet entered, an application's or extra pen's own settings on the driver).
/// </summary>
public class AutoSaveTests
{
    [Theory]
    [InlineData(SettingsSaveState.None)]
    [InlineData(SettingsSaveState.Saved)]
    [InlineData(SettingsSaveState.Applying)]
    [InlineData(SettingsSaveState.Saving)]
    [InlineData(SettingsSaveState.Failed)]           // a failed save is not retried in a loop; Save stays for the user
    [InlineData(SettingsSaveState.ApplyFailed)]
    [InlineData(SettingsSaveState.Disconnected)]
    [InlineData(SettingsSaveState.ChangedElsewhere)] // settings paused: another program changed the file
    [InlineData(SettingsSaveState.CouldNotCheck)]
    public void NothingToDo_UnlessThereAreUnsavedChanges(SettingsSaveState state)
    {
        Assert.Equal(AutoSave.Step.Nothing, AutoSave.Next(enabled: true, state, blocked: false));
    }

    [Fact]
    public void UnsavedChanges_AreSaved_WhenNothingIsInTheWay()
    {
        Assert.Equal(AutoSave.Step.SaveNow, AutoSave.Next(enabled: true, SettingsSaveState.Unsaved, blocked: false));
    }

    [Fact]
    public void UnsavedChanges_Wait_WhileSomethingIsInTheWay()
    {
        Assert.Equal(AutoSave.Step.Wait, AutoSave.Next(enabled: true, SettingsSaveState.Unsaved, blocked: true));
    }

    [Fact]
    public void SwitchedOff_NothingIsSavedByItself()
    {
        Assert.Equal(AutoSave.Step.Nothing, AutoSave.Next(enabled: false, SettingsSaveState.Unsaved, blocked: false));
    }

    [Fact]
    public void TheClickSaveHint_ShowsOnlyWhenUnsavedChangesWontBeSavedByThemselves()
    {
        Assert.False(AutoSave.ShowSaveHint(enabled: true, SettingsSaveState.Unsaved));
        Assert.True(AutoSave.ShowSaveHint(enabled: false, SettingsSaveState.Unsaved));
        Assert.False(AutoSave.ShowSaveHint(enabled: false, SettingsSaveState.Saved));
        Assert.True(AutoSave.ShowSaveHint(enabled: true, SettingsSaveState.Unsaved, held: true));
    }

    [Fact]
    public void HeldUnsavedState_IsNeverSavedByItself()
    {
        Assert.Equal(AutoSave.Step.Nothing, AutoSave.Next(enabled: true, SettingsSaveState.Unsaved, blocked: false, held: true));
    }

    /// <summary>Review before release: unsaved state that was there when the app connected (a driver that outlived the
    /// app with Photoshop's own bindings on it) must not be saved as the All settings.</summary>
    [Fact]
    public void Gate_UnsavedRightAfterConnecting_IsHeld_UntilTheSettingsWereSeenSaved()
    {
        var gate = new AutoSaveGate();
        gate.Observe(SettingsSaveState.None);        // connected
        gate.Observe(SettingsSaveState.Unsaved);     // the driver already differs from the file
        Assert.True(gate.Holds(snapshotPending: false));

        gate.Observe(SettingsSaveState.Saving);      // the user pressed Save
        gate.Observe(SettingsSaveState.Saved);
        Assert.False(gate.Holds(snapshotPending: false));

        gate.Observe(SettingsSaveState.Applying);    // the user's own edit in this session
        gate.Observe(SettingsSaveState.Unsaved);
        Assert.False(gate.Holds(snapshotPending: false));   // saved by itself
    }

    [Fact]
    public void Gate_ClosesOnDisconnectAndReload()
    {
        var gate = new AutoSaveGate();
        gate.Observe(SettingsSaveState.Saved);
        Assert.False(gate.Holds(false));
        gate.Observe(SettingsSaveState.Disconnected);
        Assert.True(gate.Holds(false));
        gate.Observe(SettingsSaveState.Saved);
        gate.Observe(SettingsSaveState.None);        // reload, restore or a new connection
        Assert.True(gate.Holds(false));
    }

    /// <summary>Changes kept from the last quit come back as unsaved after the first (saved) load: they stay the user's
    /// to keep or discard (they may hold an application's bindings if the Mac quit with that app in front).</summary>
    [Fact]
    public void Gate_HoldsWhileChangesKeptFromTheLastQuitAreWaiting()
    {
        var gate = new AutoSaveGate();
        gate.Observe(SettingsSaveState.Saved);
        gate.Observe(SettingsSaveState.Unsaved);
        Assert.True(gate.Holds(snapshotPending: true));
        Assert.False(gate.Holds(snapshotPending: false));
    }

    [Fact]
    public void OnByDefault_AndTheSwitchIsRemembered()
    {
        var before = AppSettings.Get(AutoSave.Key);
        var raised = 0;
        void Count() => raised++;
        AutoSave.Changed += Count;
        try
        {
            AppSettings.Remove(AutoSave.Key);
            Assert.True(AutoSave.Enabled);          // a new or updated install saves by itself
            AutoSave.Enabled = false;
            Assert.False(AutoSave.Enabled);
            Assert.Equal("false", AppSettings.Get(AutoSave.Key));
            AutoSave.Enabled = false;               // no change, no event
            AutoSave.Enabled = true;
            Assert.True(AutoSave.Enabled);
            Assert.Equal(2, raised);
        }
        finally
        {
            AutoSave.Changed -= Count;
            if (before is null) AppSettings.Remove(AutoSave.Key);
            else AppSettings.Set(AutoSave.Key, before);
        }
    }
}
