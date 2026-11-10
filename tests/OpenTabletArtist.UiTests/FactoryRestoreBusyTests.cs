using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenTabletArtist.Services;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OtdInterop;
using OtdInterop.Tests;
using OpenTabletArtist.Tests;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>Build 7 (Zak, 10 Nov 2026): "Restore factory settings ... does not restore the double click fix ...
/// it stays to the custom value". His app.log: "Factory restore failed (ApplyFailed)" three times. The session's
/// RestoreFactoryAsync holds SettingsBusy itself and refuses while it is already held; the footer's handler had set
/// it first, so every restore was refused before it reached the driver.</summary>
public class FactoryRestoreBusyTests
{
    private static async Task<(AppSession App, FakeDaemonTransport Daemon)> Session()
    {
        var doc = new Settings { Profiles = new ProfileCollection { new Profile { Tablet = "Wacom PTK-440", OutputMode = new OpenTabletDriver.Desktop.Reflection.PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)) } } };
        doc.Profiles[0].BindingSettings.DoubleClickFix = 60;
        var daemon = new FakeDaemonTransport { Settings = doc };
        var app = new AppSession(FakeSession.Over(daemon, new MemorySettingsFileStore { Saved = doc }), new FakeLifecycle());
        daemon.Reconnect();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (app.CurrentSettings is null && DateTime.UtcNow < deadline) { Dispatcher.UIThread.RunJobs(); await Task.Delay(5); }
        Assert.NotNull(app.CurrentSettings);
        return (app, daemon);
    }

    [AvaloniaFact]
    public async Task TheSessionRefusesAFactoryRestore_WhileSomeoneElseHoldsTheBusyFlag()
    {
        var (app, daemon) = await Session();
        app.SettingsBusy = true;
        var refused = await app.RestoreFactoryAsync();
        Assert.False(refused.IsRestored);
        Assert.Equal(0, daemon.ResetSettingsCalls);

        app.SettingsBusy = false;
        var done = await app.RestoreFactoryAsync();
        Assert.True(done.IsRestored);
        Assert.Equal(1, daemon.ResetSettingsCalls);
        Assert.False(app.SettingsBusy);
    }

    static string Source(string relative)
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, relative))) d = d.Parent;
        Assert.NotNull(d);
        return File.ReadAllText(Path.Combine(d!.FullName, relative));
    }

    [Fact]
    public void TheFooterHandler_DoesNotTakeTheBusyFlag_BeforeAskingTheSessionToRestore()
    {
        var src = Source(Path.Combine("OpenTabletArtist", "ViewModels", "MainViewModel.cs"));
        var start = src.IndexOf("private async Task RestoreFactoryCoreAsync()", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var body = src[start..];
        var call = body.IndexOf("_session.RestoreFactoryAsync()", StringComparison.Ordinal);
        Assert.True(call > 0);
        Assert.DoesNotMatch(new Regex(@"_session\.SettingsBusy\s*=\s*true"), body[..call]);
    }
}
