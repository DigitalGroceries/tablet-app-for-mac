using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OtdInterop;
using OtdInterop.Tests;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>V29 #170 (Digital Groceries 20 Nov ~10:05: "I have a menu in front of me = I should be able to control it the same no matter what app
/// is open"; no switch). His Photoshop and Affinity tables carried their own radial menu and palette, so the Pen page's sliders changed
/// only All's copy while the pen opened Photoshop's. Now the tablet has ONE menu in every table.</summary>
public class OneRadialMenuTests
{
    private const string Ps = "com.adobe.Photoshop";

    private static string Menu(string label, int size = 50, int transparency = 40)
    {
        var m = RadialMenuDefaults.Create(Localizer.Te);
        m.Slices[0].Label = label; m.Size = size; m.Transparency = transparency;
        return RadialMenuJson.Serialize(m);
    }

    private static BindingSettings Table(PluginSettingStore? button2)
    {
        var b = new BindingSettings { PenButtons = new PluginSettingStoreCollection(), AuxButtons = new PluginSettingStoreCollection() };
        b.PenButtons.Add(null!); b.PenButtons.Add(button2!);
        return b;
    }

    private static Settings Document(string tablet, string allMenu)
    {
        var profile = new Profile { Tablet = tablet, OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)) };
        profile.BindingSettings = Table(RadialMenuBinding.Make(allMenu));
        profile.AbsoluteModeSettings = new AbsoluteModeSettings
        {
            Display = new AreaSettings { Width = 1920, Height = 1080, X = 960, Y = 540 },
            Tablet = new AreaSettings { Width = 100, Height = 60, X = 50, Y = 30 },
        };
        return new Settings { Profiles = new ProfileCollection { profile } };
    }

    private static string Name() => "One radial menu " + Guid.NewGuid().ToString("N")[..8];

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

    private static RadialMenuDef PsMenu(string tablet) =>
        RadialMenuBinding.ReadMenu(PerAppBindingStore.Find(tablet, Ps)!.Bindings.PenButtons[1]);

    /// <summary>The page on a fake driver (so edits of All are adopted), with Photoshop's table holding <paramref name="psMenu"/>.</summary>
    private static async Task<(AppSession App, TabletDetailViewModel Vm, TabletDetailView View, Window Window)> Open(string tablet, string psMenu)
    {
        _ = new ThemeViewModel();
        PerAppBindingStore.Add(tablet, Ps, "Adobe Photoshop 2025", "", Table(RadialMenuBinding.Make(psMenu)));
        var daemon = new FakeDaemonTransport { Settings = Document(tablet, Menu("All")) };
        daemon.GetSettingsHandler = async () => { await Task.Yield(); return daemon.Settings; };
        var app = new AppSession(FakeSession.Over(daemon, new MemorySettingsFileStore { Saved = Document(tablet, Menu("All")) }), new FakeLifecycle());
        daemon.Reconnect();
        await PumpUntil(() => app.CurrentSettings is not null);
        var settings = app.CurrentSettings!;
        var vm = new TabletDetailViewModel(settings.Profiles[0], settings, applyAction: app.ApplySettingsAsync) { ShowHeader = false };
        var view = new TabletDetailView { DataContext = vm };
        var window = new Window { Content = new Border { Child = view, Margin = new Thickness(24, 86, 24, 62) }, Width = 1100, Height = 720 };
        window.Show();
        for (int i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
        return (app, vm, view, window);
    }

    [AvaloniaFact]
    public async Task OpeningThePage_GivesPhotoshopsOwnCopyAllsMenu_AndKeepsTheOldOneAside()
    {
        var tablet = Name();
        var (app, _, _, window) = await Open(tablet, Menu("Zoom in", size: 12, transparency: 30));
        try
        {
            Assert.Equal("All", PsMenu(tablet).Slices[0].Label);
            Assert.Equal(50, PsMenu(tablet).Size);
            Assert.Equal("Zoom in", RadialMenuJson.Parse(AppSettings.Get(RadialMenuShare.ReplacedKey(tablet, "app:" + Ps))).Slices[0].Label);
        }
        finally { window.Close(); app.Dispose(); }
    }

    [AvaloniaFact]
    public async Task TheSliders_OnAllsPage_ChangeTheMenuPhotoshopOpensToo()
    {
        var tablet = Name();
        var (app, vm, view, window) = await Open(tablet, Menu("All"));
        try
        {
            view.FindControl<Slider>("RadialTransparencySlider")!.Value = 12;
            await vm.FlushRadialSpotAsync();
            await PumpUntil(() => !app.SettingsBusy && app.SaveState != SettingsSaveState.Applying);
            Assert.Equal(12, RadialMenuBinding.ReadMenu(app.CurrentSettings!.Profiles[0].BindingSettings.PenButtons[1]).Transparency);
            Assert.Equal(12, PsMenu(tablet).Transparency);
        }
        finally { window.Close(); app.Dispose(); }
    }

    [AvaloniaFact]
    public async Task TheSliders_OnPhotoshopsTile_ChangeAllToo()
    {
        var tablet = Name();
        var (app, vm, view, window) = await Open(tablet, Menu("All"));
        try
        {
            vm.SelectedAppBundleId = Ps;
            Dispatcher.UIThread.RunJobs();
            view.FindControl<Slider>("RadialTransparencySlider")!.Value = 7;
            await vm.FlushRadialSpotAsync();
            await PumpUntil(() => !app.SettingsBusy && app.SaveState != SettingsSaveState.Applying);
            Assert.Equal(7, PsMenu(tablet).Transparency);
            Assert.Equal(7, RadialMenuBinding.ReadMenu(app.CurrentSettings!.Profiles[0].BindingSettings.PenButtons[1]).Transparency);
        }
        finally { window.Close(); app.Dispose(); }
    }

    [AvaloniaFact]
    public async Task AButtonNewlySetToOpenTheRadialMenu_OpensTheTabletsMenu_NotTheDefault()
    {
        var tablet = Name();
        var (app, vm, _, window) = await Open(tablet, Menu("All"));
        try
        {
            vm.SelectedAppBundleId = Ps;
            Dispatcher.UIThread.RunJobs();
            Assert.True(await vm.ApplyBindingMutationAsync(b => b.PenButtons[0] = RadialMenuBinding.Make()));
            Assert.Equal("All", RadialMenuBinding.ReadMenu(PerAppBindingStore.Find(tablet, Ps)!.Bindings.PenButtons[0]).Slices[0].Label);
            Assert.Equal("All", RadialMenuBinding.ReadMenu(app.CurrentSettings!.Profiles[0].BindingSettings.PenButtons[1]).Slices[0].Label);
        }
        finally { window.Close(); app.Dispose(); }
    }
}
