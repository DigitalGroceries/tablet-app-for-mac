using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Output;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OtdInterop;
using OtdInterop.Tests;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>Version 25: a remembered "All displays" choice follows the current desktop by itself and is saved,
/// without the Tablet page being opened, without saving anyone else's edit, and without a page open ever
/// creating "Unsaved changes".</summary>
public class AllDisplaysAutoRefitTests
{
    private static readonly DisplayInfo[] OneMonitor = { new(1, "Laptop", 1496, 967, 0, 0, true) };
    private static readonly DisplayInfo[] TwoMonitors =
    {
        new(1, "Laptop", 1496, 967, 0, 0, true),
        new(2, "TV", 3360, 1890, 1496, 0, false),
    };

    private sealed class FixedDisplays(IReadOnlyList<DisplayInfo> displays) : IDisplayEnumerator
    {
        public IReadOnlyList<DisplayInfo> Enumerate() => displays;
    }

    private static Settings Document(string tablet, IReadOnlyList<DisplayInfo> mappedTo, bool pressureDisabled = false)
    {
        var profile = new Profile
        {
            OutputMode = new PluginSettingStore(typeof(AbsoluteMode)),
            Tablet = tablet,
            BindingSettings = new BindingSettings { DisablePressure = pressureDisabled },
            AbsoluteModeSettings = new AbsoluteModeSettings
            {
                Display = new AreaSettings(),
                Tablet = new AreaSettings { Width = 100, Height = 60, X = 50, Y = 30 },
            },
        };
        DisplayMappingApplier.ApplyAllDisplays(profile, null, mappedTo);
        return new Settings { Profiles = new ProfileCollection { profile } };
    }

    private static async Task PumpUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "UI did not settle.");
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Saved file and live daemon both hold the box fitted to <paramref name="mappedTo"/>.</summary>
    private static async Task<(AppSession App, FakeDaemonTransport Daemon, MemorySettingsFileStore Store)> Open(
        string tablet, IReadOnlyList<DisplayInfo> mappedTo)
    {
        var daemon = new FakeDaemonTransport { Settings = Document(tablet, mappedTo) };
        daemon.GetSettingsHandler = async () => { await Task.Yield(); return daemon.Settings; };
        var store = new MemorySettingsFileStore { Saved = Document(tablet, mappedTo) };
        var app = new AppSession(FakeSession.Over(daemon, store), new FakeLifecycle());
        daemon.Reconnect();
        await PumpUntil(() => app.CurrentSettings is not null);
        return (app, daemon, store);
    }

    private static string NewTablet(bool allDisplays = true)
    {
        var tablet = "V25 AllDisplays " + Guid.NewGuid().ToString("N")[..8];
        AppSettings.Set($"MapAllDisplays:{tablet}", allDisplays ? "1" : "0");
        AppSettings.Set("AllDisplaysForceProportions", "1");
        return tablet;
    }

    [AvaloniaFact]
    public async Task DisplayChange_RefitsTheRememberedAllDisplaysBox_AndSavesIt()
    {
        var tablet = NewTablet();
        var (app, _, store) = await Open(tablet, OneMonitor);
        using var lifetime = app;

        Assert.True(await app.RefitRememberedAllDisplaysAsync(TwoMonitors));

        Assert.False(app.HasUnsavedChanges);
        Assert.True(DisplayMappingApplier.IsAllDisplays(app.CurrentSettings!.Profiles[0], TwoMonitors));
        Assert.True(DisplayMappingApplier.IsAllDisplays(store.Saved!.Profiles[0], TwoMonitors));
        Assert.Equal(1, store.Attempts);
    }

    [AvaloniaFact]
    public async Task NothingToRefit_DoesNothing()
    {
        var tablet = NewTablet();
        var (app, daemon, store) = await Open(tablet, TwoMonitors);
        using var lifetime = app;
        var applied = daemon.Applied.Count;

        Assert.False(await app.RefitRememberedAllDisplaysAsync(TwoMonitors));
        Assert.False(app.HasUnsavedChanges);
        Assert.Equal(applied, daemon.Applied.Count);
        Assert.Equal(0, store.Attempts);
    }

    [AvaloniaFact]
    public async Task NotRememberedAsAllDisplays_IsLeftAlone()
    {
        var tablet = NewTablet(allDisplays: false);
        var (app, _, store) = await Open(tablet, OneMonitor);
        using var lifetime = app;

        Assert.False(await app.RefitRememberedAllDisplaysAsync(TwoMonitors));
        Assert.Equal(1496f, app.CurrentSettings!.Profiles[0].AbsoluteModeSettings!.Display.Width);   // still the old box
        Assert.Equal(0, store.Attempts);
    }

    [AvaloniaFact]
    public async Task OtherUnsavedEdits_AreNeverSavedByTheRefit()
    {
        var tablet = NewTablet();
        var (app, _, store) = await Open(tablet, OneMonitor);
        using var lifetime = app;

        // The user's own pending edit: pressure switched off, not saved.
        Assert.True((await app.ApplySettingsAsync(Document(tablet, OneMonitor, pressureDisabled: true))).IsLive);
        Assert.True(app.HasUnsavedChanges);

        Assert.False(await app.RefitRememberedAllDisplaysAsync(TwoMonitors));

        Assert.Equal(0, store.Attempts);
        Assert.False(store.Saved!.Profiles[0].BindingSettings.DisablePressure);
        Assert.True(app.HasUnsavedChanges);
        Assert.True(app.CurrentSettings!.Profiles[0].BindingSettings.DisablePressure);   // edit untouched

        // Once the user reverts their edit the re-fit goes through, saved on its own.
        Assert.True((await app.RestoreDefaultAsync()).IsRestored);
        Assert.False(app.HasUnsavedChanges);
        Assert.True(await app.RefitRememberedAllDisplaysAsync(TwoMonitors));
        Assert.False(store.Saved!.Profiles[0].BindingSettings.DisablePressure);
        Assert.True(DisplayMappingApplier.IsAllDisplays(store.Saved!.Profiles[0], TwoMonitors));
        Assert.False(app.HasUnsavedChanges);
    }

    [AvaloniaFact]
    public async Task OpeningTheTabletPage_WithNoChange_NeverCreatesUnsavedChanges()
    {
        foreach (var stale in new[] { false, true })
        {
            var tablet = NewTablet();
            var (app, _, store) = await Open(tablet, stale ? OneMonitor : TwoMonitors);
            using var lifetime = app;
            DisplayEnumerator.Use(new FixedDisplays(TwoMonitors));
            try
            {
                var settings = app.CurrentSettings!;
                var vm = new TabletDetailViewModel(settings.Profiles[0], settings,
                    applyAction: app.ApplySettingsAsync, isDetected: () => true, deviceData: app);
                vm.RefreshDisplaysCommand.Execute(null);
                vm.RefreshDetectionStatus();
                await PumpUntil(() => !app.SettingsBusy && app.SaveState != SettingsSaveState.Applying
                    && app.SaveState != SettingsSaveState.Saving);

                Assert.False(app.HasUnsavedChanges);
                Assert.Equal(stale ? 1 : 0, store.Attempts);
                Assert.True(DisplayMappingApplier.IsAllDisplays(app.CurrentSettings!.Profiles[0], TwoMonitors));
            }
            finally { DisplayEnumerator.Use(null); }
        }
    }

    [AvaloniaFact]
    public async Task UnsavedChangesKeptAtShutdown_AreRestored_AndNeverSavedOrDeletedByTheRefit()
    {
        var tablet = NewTablet();
        var (app, _, store) = await Open(tablet, OneMonitor);   // stale box: a re-fit would be due
        using var lifetime = app;
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "v25-snap-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            app.SnapshotDirectory = dir;
            UnsavedSnapshot.Write(dir, Document(tablet, OneMonitor, pressureDisabled: true));

            // Start-up order race: the re-fit runs first. It must not save (that would delete the snapshot).
            Assert.False(await app.RefitRememberedAllDisplaysAsync(TwoMonitors));
            Assert.Equal(0, store.Attempts);
            Assert.True(UnsavedSnapshot.Exists(dir));

            await app.RestoreUnsavedSnapshotOnceAsync();
            Assert.True(app.HasUnsavedChanges);
            Assert.True(app.CurrentSettings!.Profiles[0].BindingSettings.DisablePressure);
            Assert.False(await app.RefitRememberedAllDisplaysAsync(TwoMonitors));
            Assert.Equal(0, store.Attempts);
            Assert.True(UnsavedSnapshot.Exists(dir));
        }
        finally { System.IO.Directory.Delete(dir, true); }
    }

    [AvaloniaFact]
    public async Task HomeFix_MapsToOneMonitor_AndSaveDoesNotBringAllDisplaysBack()
    {
        var tablet = NewTablet();
        var (app, _, store) = await Open(tablet, TwoMonitors);
        using var lifetime = app;
        DisplayEnumerator.Use(new FixedDisplays(TwoMonitors));
        try
        {
            await app.MapTabletToPrimaryDisplayAsync(tablet);
            Assert.Equal("0", AppSettings.Get($"MapAllDisplays:{tablet}"));
            Assert.True(await app.SaveSettingsAsync());
            Assert.False(await app.RefitRememberedAllDisplaysAsync(TwoMonitors));
            Assert.Equal(1, DisplayMappingApplier.CurrentlyMapped(store.Saved!.Profiles[0], TwoMonitors)?.Number);
            Assert.Equal(1, DisplayMappingApplier.CurrentlyMapped(app.CurrentSettings!.Profiles[0], TwoMonitors)?.Number);
        }
        finally { DisplayEnumerator.Use(null); }
    }

    [AvaloniaFact]
    public async Task PenMenuSwitchDisplay_MapsToOneMonitor_AndSaveDoesNotBringAllDisplaysBack()
    {
        // AppTray.SwitchDisplayAsync and the monitor-cycle hotkey both map through MapToSingleDisplay.
        var tablet = NewTablet();
        var (app, _, store) = await Open(tablet, TwoMonitors);
        using var lifetime = app;
        var settings = app.CurrentSettings!;
        Assert.True(DisplayMappingApplier.MapToSingleDisplay(settings.Profiles[0], null, TwoMonitors[1], TwoMonitors));
        Assert.True((await app.ApplySettingsAsync(settings)).IsLive);
        Assert.True(await app.SaveSettingsAsync());
        Assert.False(await app.RefitRememberedAllDisplaysAsync(TwoMonitors));
        Assert.Equal(2, DisplayMappingApplier.CurrentlyMapped(store.Saved!.Profiles[0], TwoMonitors)?.Number);
        Assert.Equal("0", AppSettings.Get($"MapAllDisplays:{tablet}"));
    }

    [Fact]
    public void Health_GradesARememberedAllDisplaysTabletClean()
    {
        var tablet = NewTablet();
        Assert.Equal(DisplayMappingValidity.Clean,
            HealthCollectionAdapter.AllDisplaysAware(tablet, DisplayMappingValidity.OffScreen));
        var other = NewTablet(allDisplays: false);
        Assert.Equal(DisplayMappingValidity.OffScreen,
            HealthCollectionAdapter.AllDisplaysAware(other, DisplayMappingValidity.OffScreen));
    }
}
