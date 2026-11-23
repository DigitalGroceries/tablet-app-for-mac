using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Helpers;
using OpenTabletArtist.Services;
using OpenTabletArtist.Tests;
using OpenTabletArtist.UiTests;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OtdInterop;
using OtdInterop.Tests;
using Xunit;

namespace OpenTabletArtist.SweepTests;

/// <summary>V30 (Zak 23 Nov 19:1x, test build 1: the Home line was small dark text he "would have never seen let alone try to
/// click it"; "That deserves a huge button"): pictures of the Home button, an ExpressKeys card with nothing on it, and the
/// report's "The page doesn't open?" button, dark theme, for a look before the next test build. Written to $OTA_SHOTS_DIR.</summary>
public class V30ButtonsShots
{
    static string Dir => Environment.GetEnvironmentVariable("OTA_SHOTS_DIR") ?? "/tmp/ota-v30-shots";

    static void Dark()
    {
        _ = new ThemeViewModel();
        ThemeService.Apply(ThemeService.Wacom);
        Localizer.SetPreference("en");
    }

    static void Save(Window w, string name)
    {
        for (int i = 0; i < 8; i++) { Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); }
        Directory.CreateDirectory(Dir);
        w.CaptureRenderedFrame()!.Save(Path.Combine(Dir, name + ".png"));
    }

    [AvaloniaFact]
    public void Home_keys_not_set_up_button()
    {
        Dark();
        var session = new AppSession(FakeSession.Over(new FakeDaemonTransport(), new MemorySettingsFileStore()), new FakeLifecycle());
        var health = new HealthService(session, new FakeDeviceData(), new DriverConflictMonitor());
        var overview = new TabletsOverviewViewModel();
        var page = new DashboardViewModel(session, new DaemonStatusViewModel(session), new FakeDialogService(),
            navigateToTablet: (_, _) => { }, health, overview);
        overview.Tablets = new[]
        {
            new TabletOverviewItemViewModel("Wacom PTK-440", true, "Detected", "CONNECTED", () => { }, () => Task.CompletedTask,
                keysNotSetUp: 6, openExpressKeys: () => { }),
        }.ToList();
        overview.HasTablets = true;
        var view = new DashboardView { DataContext = page };
        var w = new Window { Content = view, Width = 1000, Height = 700 };
        w.Show();
        Save(w, "home-keys-not-set-up");
        var b = view.GetVisualDescendants().OfType<Button>().First(x => x.Name == "KeysNotSetUpLink");
        Assert.True(b.IsEffectivelyVisible);
        Assert.True(b.Bounds.Height >= 50, $"the button is {b.Bounds.Height} high");
        w.Close();
    }

    [AvaloniaFact]
    public void ExpressKeys_card_not_set_up()
    {
        Dark();
        var profile = new Profile { OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)), Tablet = "Shot-PTK-440" };
        profile.BindingSettings.AuxButtons = new PluginSettingStoreCollection();
        foreach (var k in new string?[] { "B", null, null, "E", null, null }) profile.BindingSettings.AuxButtons.Add(k == null ? null! : AuxKeyBinding.MakeKeyBinding(k)!);
        var s = new Settings { Profiles = new ProfileCollection { profile } };
        var vm = new TabletDetailViewModel(profile, s, applyAction: _ => Task.FromResult(SettingsApplyOutcome.Live));
        var view = new TabletDetailView { DataContext = vm };
        var w = new Window { Content = view, Width = 1200, Height = 900 };
        w.Show();
        if (view.FindControl<ToggleButton>("ToolFunctionsButton") is { } f) f.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        view.FindControl<RadioButton>("ButtonsTab")!.IsChecked = true;
        Save(w, "expresskeys-not-set-up");
        w.Close();
    }

    [AvaloniaFact]
    public async Task Report_page_wont_open_button()
    {
        Dark();
        var mw = new MainWindow { Width = 1100, Height = 900 };
        mw.Show();
        Dispatcher.UIThread.RunJobs();
        InWindowSheet.MainOverride = () => mw;
        try
        {
            var vm = new ReportViewModel(() => (new System.Collections.Generic.List<string>(), Array.Empty<string>(), null), null, "shot")
                { Email = "k@example.com" };
            ReportWindow.Open(vm);
            Dispatcher.UIThread.RunJobs();
            var target = Path.Combine(Path.GetTempPath(), "Desktop", "Tablet App for Mac report.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            vm.PickSaveFile = _ => Task.FromResult<string?>(target);
            await vm.SendFileCommand.ExecuteAsync(null);
            Save(mw, "report-steps-page-wont-open");
            vm.ShowEmailFallbackCommand.Execute(null);
            Save(mw, "report-steps-email-shown");
            vm.DoneCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
        }
        finally { InWindowSheet.MainOverride = null; mw.Close(); }
    }
}
