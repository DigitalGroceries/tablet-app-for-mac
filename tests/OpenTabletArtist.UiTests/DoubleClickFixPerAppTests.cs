using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OtdInterop;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>
/// TASKS #15 (Zak, 9 Nov, test build 3): the Double Click Fix slider must show and keep the selected
/// profile's own value. Build 3 wrote the app's value but never reloaded the slider on a profile switch and
/// read All's table on open, so All and every app always looked like one shared value.
/// </summary>
public class DoubleClickFixPerAppTests
{
    private const string Affinity = "com.seriflabs.affinityphoto2";

    private static string NewTablet() => "TestTablet-DcfPerApp-" + Guid.NewGuid().ToString("N")[..8];

    private static Settings SettingsFor(string tablet, int? allValue) =>
        new()
        {
            Profiles = new ProfileCollection
            {
                new Profile
                {
                    OutputMode = new OpenTabletDriver.Desktop.Reflection.PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)),
                    Tablet = tablet,
                    BindingSettings = new BindingSettings { DoubleClickFix = allValue },
                },
            },
        };

    private static TabletDetailViewModel Editor(Settings settings) =>
        new(settings.Profiles[0], settings, applyAction: _ => Task.FromResult(SettingsApplyOutcome.Live));

    private static async Task Settle()
    {
        for (int i = 0; i < 10; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task AnAppKeepsItsOwnValue_AcrossProfileSwitchAndRestart()
    {
        var tablet = NewTablet();
        var settings = SettingsFor(tablet, 50);
        PerAppBindingStore.Add(tablet, Affinity, "Affinity", "", settings.Profiles[0].BindingSettings);

        var vm = Editor(settings);
        Assert.Equal(50, vm.TipDoubleClickDistance);

        vm.SelectedAppBundleId = Affinity;
        vm.TipDoubleClickDistance = 80;
        await Settle();
        Assert.Equal(80, PerAppBindingStore.Find(tablet, Affinity)!.Bindings.DoubleClickFix);
        Assert.Equal(50, settings.Profiles[0].BindingSettings.DoubleClickFix);   // All untouched

        vm.SelectedAppBundleId = null;                    // back to All
        Assert.Equal(50, vm.TipDoubleClickDistance);
        vm.SelectedAppBundleId = Affinity;
        Assert.Equal(80, vm.TipDoubleClickDistance);

        // Switching must not have written anything back.
        Assert.Equal(80, PerAppBindingStore.Find(tablet, Affinity)!.Bindings.DoubleClickFix);
        Assert.Equal(50, settings.Profiles[0].BindingSettings.DoubleClickFix);

        // "Restart": a fresh editor over the persisted state.
        var again = Editor(settings);
        Assert.Equal(50, again.TipDoubleClickDistance);
        again.SelectedAppBundleId = Affinity;
        Assert.Equal(80, again.TipDoubleClickDistance);
    }

    [AvaloniaFact]
    public async Task AllKeepsItsOwnValue_WhenEditedAfterAnApp()
    {
        var tablet = NewTablet();
        var settings = SettingsFor(tablet, 50);
        PerAppBindingStore.Add(tablet, Affinity, "Affinity", "", settings.Profiles[0].BindingSettings);
        var vm = Editor(settings);

        vm.SelectedAppBundleId = Affinity;
        vm.TipDoubleClickDistance = 80;
        await Settle();
        vm.SelectedAppBundleId = null;
        vm.TipDoubleClickDistance = 20;
        await Settle();

        Assert.Equal(20, settings.Profiles[0].BindingSettings.DoubleClickFix);
        Assert.Equal(80, PerAppBindingStore.Find(tablet, Affinity)!.Bindings.DoubleClickFix);
        vm.SelectedAppBundleId = Affinity;
        Assert.Equal(80, vm.TipDoubleClickDistance);
    }

    [AvaloniaFact]
    public async Task AnAppWithoutItsOwnValue_ShowsAndFollowsAll()
    {
        var tablet = NewTablet();
        var settings = SettingsFor(tablet, 50);
        PerAppBindingStore.Add(tablet, Affinity, "Affinity", "", settings.Profiles[0].BindingSettings);
        var vm = Editor(settings);

        vm.TipDoubleClickDistance = 30;   // All
        await Settle();
        vm.SelectedAppBundleId = Affinity;
        Assert.Equal(30, vm.TipDoubleClickDistance);
        Assert.Null(PerAppBindingStore.Find(tablet, Affinity)!.Bindings.DoubleClickFix);
    }

    [AvaloniaFact]
    public async Task TheDefaultButton_GivesTheSelectedProfileFifty()
    {
        var tablet = NewTablet();
        var settings = SettingsFor(tablet, 20);
        PerAppBindingStore.Add(tablet, Affinity, "Affinity", "", settings.Profiles[0].BindingSettings);
        var vm = Editor(settings);

        vm.SelectedAppBundleId = Affinity;
        vm.TipDoubleClickDistance = 90;
        await Settle();
        await vm.ResetDoubleClickFixCommand.ExecuteAsync(null);
        await Settle();
        Assert.Equal(50, vm.TipDoubleClickDistance);
        Assert.Equal(50, PerAppBindingStore.Find(tablet, Affinity)!.Bindings.DoubleClickFix);
        Assert.Equal(20, settings.Profiles[0].BindingSettings.DoubleClickFix);   // All untouched

        vm.SelectedAppBundleId = null;
        Assert.Equal(20, vm.TipDoubleClickDistance);
        await vm.ResetDoubleClickFixCommand.ExecuteAsync(null);
        await Settle();
        Assert.Equal(50, settings.Profiles[0].BindingSettings.DoubleClickFix);
    }

    [AvaloniaFact]
    public async Task AnExtraPenWithoutItsOwnValue_ShowsTheSelectedAppsValue()
    {
        var tablet = NewTablet();
        var settings = SettingsFor(tablet, 50);
        PerAppBindingStore.Add(tablet, Affinity, "Affinity", "", settings.Profiles[0].BindingSettings);
        PerPenStore.Seen(tablet, new OpenTabletArtist.Domain.PenToolSignal(1, 0x802, false), settings.Profiles[0].BindingSettings, out _);
        PerPenStore.Seen(tablet, new OpenTabletArtist.Domain.PenToolSignal(2, 0x802, false), settings.Profiles[0].BindingSettings, out var pen);
        var vm = Editor(settings);

        vm.SelectedAppBundleId = Affinity;
        vm.TipDoubleClickDistance = 80;
        await Settle();
        vm.SelectedPenKey = pen!.Key;
        Assert.Equal(80, vm.TipDoubleClickDistance);   // the runtime keeps the app's value for this pen
        vm.SelectedAppBundleId = null;
        Assert.Equal(50, vm.TipDoubleClickDistance);   // ... and All's on All
    }
}
