using System;
using System.Diagnostics;
using System.Threading.Tasks;
using OpenTabletArtist.Helpers;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Version 2: quit with unsaved changes and no daemon must finish; the single-instance listener must close at once.</summary>
public class BuildV2Tests
{
    [Fact]
    public async Task Quit_save_with_no_daemon_writes_local_file_and_completes()
    {
        bool daemonCalled = false, local = false;
        var sw = Stopwatch.StartNew();
        var ok = await QuitSave.ResolveAsync(UnsavedSettingsChoice.Save, daemonConnected: false,
            () => { daemonCalled = true; return new TaskCompletionSource<bool>().Task; },
            () => { local = true; return true; }, () => { }, _ => { });
        Assert.True(ok);
        Assert.False(daemonCalled);
        Assert.True(local);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Quit_save_with_unresponsive_daemon_times_out_to_local_file()
    {
        bool local = false;
        var ok = await QuitSave.ResolveAsync(UnsavedSettingsChoice.Save, true,
            () => new TaskCompletionSource<bool>().Task, () => { local = true; return true; }, () => { }, _ => { },
            TimeSpan.FromMilliseconds(200));
        Assert.True(ok);
        Assert.True(local);
    }

    [Fact]
    public async Task Quit_save_with_daemon_does_not_write_local_file()
    {
        bool local = false;
        Assert.True(await QuitSave.ResolveAsync(UnsavedSettingsChoice.Save, true, () => Task.FromResult(true),
            () => { local = true; return true; }, () => { }, _ => { }));
        Assert.False(local);
    }

    [Fact]
    public async Task Quit_cancel_and_dont_save()
    {
        Assert.False(await QuitSave.ResolveAsync(UnsavedSettingsChoice.Cancel, false, () => Task.FromResult(true), () => true, () => { }, _ => { }));
        bool discarded = false;
        Assert.True(await QuitSave.ResolveAsync(UnsavedSettingsChoice.Continue, false, () => Task.FromResult(true), () => true, () => discarded = true, _ => { }));
        Assert.True(discarded);
    }

    [Fact]
    public void Quit_prompt_wording()
    {
        Assert.StartsWith("Save changes before quitting?", Dialogs.QuitPromptText);
    }

    [Fact]
    public void Single_instance_dispose_returns_promptly_while_listening()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        var si = new SingleInstance("v2test" + Guid.NewGuid().ToString("N")[..8]);
        Assert.True(si.TryAcquire());
        si.ListenForActivation(() => { });
        System.Threading.Thread.Sleep(150);
        var sw = Stopwatch.StartNew();
        si.Dispose();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"Dispose took {sw.Elapsed}");
    }

    [Fact]
    public void About_features_have_no_mouse_row_and_say_tablet_buttons()
    {
        var f = OpenTabletArtist.ViewModels.TabletAboutFacts.BuildFeatures(
            new OpenTabletArtist.Domain.TabletAboutInfo { ExpressKeys = 8, MouseButtons = 5 });
        Assert.DoesNotContain(f, x => x.Label.Contains("ouse"));
        Assert.Contains(f, x => x.Label == "Tablet buttons" && x.Value == "8");
    }
}
