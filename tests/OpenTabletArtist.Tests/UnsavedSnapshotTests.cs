using System;
using System.IO;
using System.Threading.Tasks;
using OpenTabletArtist.Services;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OtdInterop;
using OtdInterop.Tests;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>R11: a system quit keeps unsaved edits in pending-unsaved.json, never in the real settings.</summary>
public class UnsavedSnapshotTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ota-r11-snap-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static Settings Document()
    {
        var s = new Settings { Profiles = new ProfileCollection { new Profile { OutputMode = new OpenTabletDriver.Desktop.Reflection.PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)), Tablet = "A" } } };
        ProfileSanitizer.EnsureValidAbsoluteAreas(s);
        return s;
    }

    private static async Task<(OtdSession, FakeDaemonTransport, MemorySettingsFileStore)> Connected()
    {
        var daemon = new FakeDaemonTransport { Settings = Document() };
        var store = new MemorySettingsFileStore { Saved = Document() };
        var session = FakeSession.Over(daemon, store);
        daemon.Reconnect();
        await session.InitializeAsync();
        return (session, daemon, store);
    }

    [Fact]
    public async Task UnsavedEdit_IsWrittenToSnapshot_AndRealSettingsStayUntouched()
    {
        var (session, _, store) = await Connected();
        using var _s = session;
        var ws = new SettingsWorkspace(session.Settings!, false);
        var edit = ws.Current!; edit.Profiles[0].BindingSettings.DisablePressure = true;
        Assert.True((await ws.ApplyAsync(edit)).IsLive);
        Assert.True(ws.HasUnsavedChanges);

        UnsavedSnapshot.Write(_dir, ws.Current!);

        Assert.True(File.Exists(Path.Combine(_dir, "pending-unsaved.json")));
        Assert.Equal(0, store.Attempts);                                     // never saved
        Assert.False(store.Saved!.Profiles[0].BindingSettings.DisablePressure); // real file as it was
        Assert.True(UnsavedSnapshot.TryRead(_dir, out var back));
        Assert.True(back!.Profiles[0].BindingSettings.DisablePressure);
    }

    [Fact]
    public async Task Restore_MarksTheWorkspaceUnsaved_WithoutSaving()
    {
        var snap = Document(); snap.Profiles[0].BindingSettings.DisablePressure = true;
        UnsavedSnapshot.Write(_dir, snap);
        var (session, daemon, store) = await Connected();
        using var _s = session;
        var ws = new SettingsWorkspace(session.Settings!, false);
        Assert.False(ws.HasUnsavedChanges);

        Assert.True(await UnsavedSnapshot.RestoreIntoAsync(ws, _dir));

        Assert.True(ws.HasUnsavedChanges);                       // Save button active, discard possible
        Assert.True(daemon.Settings!.Profiles[0].BindingSettings.DisablePressure);
        Assert.Equal(0, store.Attempts);
        Assert.True(UnsavedSnapshot.Exists(_dir));               // kept until saved or discarded
    }

    [Fact]
    public async Task Discard_RemovesTheSnapshot()
    {
        var snap = Document(); snap.Profiles[0].BindingSettings.DisablePressure = true;
        UnsavedSnapshot.Write(_dir, snap);
        var (session, _, store) = await Connected();
        using var _s = session;
        var ws = new SettingsWorkspace(session.Settings!, false);
        await UnsavedSnapshot.RestoreIntoAsync(ws, _dir);

        await ws.ReloadAsync(); UnsavedSnapshot.Delete(_dir);   // what AppSession.ReloadSettingsAsync does
        Assert.False(UnsavedSnapshot.Exists(_dir));
        Assert.False(File.Exists(Path.Combine(_dir, "pending-unsaved.json.tmp")));
        Assert.Equal(0, store.Attempts);
    }

    [Fact]
    public void NothingUnsaved_WritesNothing_AndUnreadableSnapshotIsDropped()
    {
        Assert.False(UnsavedSnapshot.Exists(_dir));
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "pending-unsaved.json"), "{not json");
        Assert.False(UnsavedSnapshot.TryRead(_dir, out _));
    }
}
