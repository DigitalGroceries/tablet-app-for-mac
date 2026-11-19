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

    /// <summary>V28 second review: changes kept at the last quit that turn out equal to the saved settings (a quit with an
    /// application's own settings on the driver and no real edit) are spent, not left to hold automatic saving all session.</summary>
    [AvaloniaFact]
    public async Task KeptChanges_EqualToTheSavedSettings_AreDropped_NotLeftWaiting()
    {
        var tablet = NewTablet();
        var (app, _, store) = await Open(tablet, OneMonitor);
        using var lifetime = app;
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "v28-snap-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            app.SnapshotDirectory = dir;
            // As in real use: the saved file is what the app itself saved (its own settings policy applied).
            Assert.True((await app.ApplySettingsAsync(app.CurrentSettings!)).IsLive);
            Assert.True(await app.SaveSettingsAsync());
            Assert.False(app.HasUnsavedChanges);
            UnsavedSnapshot.Write(dir, app.CurrentSettings!);   // kept at the quit, but the same as the saved settings
            var saves = store.Attempts;

            await app.RestoreUnsavedSnapshotOnceAsync();

            Assert.False(app.HasUnsavedChanges);
            Assert.False(UnsavedSnapshot.Exists(dir));
            Assert.Equal(saves, store.Attempts);   // nothing saved, nothing to save
        }
        finally { System.IO.Directory.Delete(dir, true); }
    }

    /// <summary>V29 #126/#129 (Zak's V29 test, 19 Nov): his whole-desktop mapping had been made for an older arrangement
    /// (3512x2890) and the choice wasn't remembered, so Home said "partly off-screen"; its Fix put the pen on the built-in display
    /// only. Now a mapping made for several screens is fixed to today's whole desktop and remembered.</summary>
    [AvaloniaFact]
    public async Task OffScreenFix_OfAnOldWholeDesktopMapping_MapsAllDisplays_AndRemembersIt()
    {
        DisplayInfo[] zaks =
        {
            new(1, "Built-in Retina Display", 1496, 967, 896, 2160, true),
            new(2, "SONY TV", 3840, 2160, 0, 0, false),
            new(3, "Display", 1600, 1000, 2392, 2160, false),
        };
        DisplayInfo[] older =
        {
            new(1, "Built-in Retina Display", 1496, 967, 0, 1923, true),
            new(2, "SONY TV", 3512, 1923, 0, 0, false),
        };
        var tablet = NewTablet(allDisplays: false);
        var (app, _, _) = await Open(tablet, older);
        using var lifetime = app;
        DisplayEnumerator.Use(new FixedDisplays(zaks));
        try
        {
            Assert.False(DisplayMappingApplier.IsAllDisplays(app.CurrentSettings!.Profiles[0], zaks));
            Assert.True(DisplayMappingApplier.SpansSeveralDisplays(app.CurrentSettings!.Profiles[0], zaks));
            Assert.True(await app.FixOffScreenMappingAsync(tablet));
            Assert.True(DisplayMappingApplier.IsAllDisplays(app.CurrentSettings!.Profiles[0], zaks));
            Assert.Equal("1", AppSettings.Get($"MapAllDisplays:{tablet}"));
        }
        finally { DisplayEnumerator.Use(null); }
    }

    [AvaloniaFact]
    public async Task OffScreenFix_OfAOneScreenMapping_StillGoesToThePrimaryDisplay()
    {
        var tablet = NewTablet(allDisplays: false);
        var (app, _, _) = await Open(tablet, OneMonitor);    // a one-screen box
        using var lifetime = app;
        DisplayEnumerator.Use(new FixedDisplays(TwoMonitors));
        try
        {
            Assert.False(DisplayMappingApplier.SpansSeveralDisplays(app.CurrentSettings!.Profiles[0], TwoMonitors));
            Assert.True(await app.FixOffScreenMappingAsync(tablet));
            Assert.Equal(1, DisplayMappingApplier.CurrentlyMapped(app.CurrentSettings!.Profiles[0], TwoMonitors)?.Number);
        }
        finally { DisplayEnumerator.Use(null); }
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
