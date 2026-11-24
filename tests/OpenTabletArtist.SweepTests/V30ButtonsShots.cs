using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
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

    static bool _light;
    static string _tag = "";
    static string Sfx => (_light ? "-light" : "") + _tag;
    static void Dark()
    {
        _ = new ThemeViewModel();
        ThemeService.Apply(ThemeService.Wacom);
        LightnessService.Set(_light ? 8 : 0);   // the app's own Lightness: light at 8, dark at 0 (as V19SheetThemeShots)
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
        Save(w, "home-keys-not-set-up" + Sfx);
        var b = view.GetVisualDescendants().OfType<Button>().First(x => x.Name == "KeysNotSetUpLink");
        Assert.True(b.IsEffectivelyVisible);
        Assert.True(b.Bounds.Height >= 50, $"the button is {b.Bounds.Height} high");
        // L6.2 (Zak 24 Nov): no ••• menu on the card any more; Forget is its own smaller red button under the big one
        Assert.DoesNotContain(view.GetVisualDescendants().OfType<OpenTabletArtist.Controls.IconButton>(),
            x => (AutomationProperties.GetName(x) ?? "") == "More actions");
        var forget = view.GetVisualDescendants().OfType<Button>().First(x => x.Name == "ForgetTabletButton");
        Assert.True(forget.IsEffectivelyVisible);
        Assert.Equal("Forget this tablet", forget.Content);
        Assert.True(forget.Bounds.Height < b.Bounds.Height, "Forget is smaller than the big button");
        Assert.True(forget.Bounds.Y > b.Bounds.Y, "Forget is under the big button");
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
        // The second look Zak asked to be bold yellow: a key with nothing on it that was just pressed (Button 3).
        vm.AuxButtons[2].JustUsed = true;
        Save(w, "expresskeys-not-set-up" + Sfx);
        var marks = view.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Name == "NotSetUpMark" && t.IsEffectivelyVisible).Select(t => t.Text).ToList();
        Assert.Contains("Not set up", marks);
        // L6.3 (Zak 24 Nov): the just-used line is a speech bubble with much bigger type, not the small mark
        Assert.DoesNotContain("You just used this. It does nothing yet.", marks);
        var bubble = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "JustUsedText" && t.IsEffectivelyVisible);
        Assert.Equal("You just used this. It does nothing yet.", bubble.Text);
        Assert.True(bubble.FontSize >= 22, $"bubble text {bubble.FontSize}");
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
            Save(mw, "report-steps-page-wont-open" + Sfx);
            vm.ShowEmailFallbackCommand.Execute(null);
            Save(mw, "report-steps-email-shown" + Sfx);
            vm.DoneCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
        }
        finally { InWindowSheet.MainOverride = null; mw.Close(); }
    }

    /// <summary>V30 build 4 (Zak 24 Nov 2026, his screenshot of build 3's Home: "we have 3 different designs for buttons?";
    /// "The huge white square doesnt even look like a clickable button"; "change the word CONNECTED to be in green"):
    /// the whole Home page as he sees it, dark and light, with one tablet that has 6 keys not set up.</summary>
    [AvaloniaFact]
    public void Home_whole_page_for_Zak()
    {
        try
        {
            foreach (var light in new[] { false, true })
            {
                _light = light;
                Dark();
                var mw = new MainWindow { Width = 1130, Height = 810 };
                mw.Show();
                for (int i = 0; i < 8; i++) { Dispatcher.UIThread.RunJobs(); mw.UpdateLayout(); }
                var dash = mw.GetVisualDescendants().OfType<DashboardView>().First();
                var page = (DashboardViewModel)dash.DataContext!;
                page.TabletsOverview.Tablets = new[]
                {
                    new TabletOverviewItemViewModel("Wacom PTK-440", true, "Detected", "CONNECTED", () => { }, () => Task.CompletedTask,
                        keysNotSetUp: 6, openExpressKeys: () => { }),
                }.ToList();
                page.TabletsOverview.HasTablets = true;
                Save(mw, "home-whole" + Sfx);
                mw.Close();
            }
        }
        finally { _light = false; LightnessService.Set(0); }
    }

    [AvaloniaFact]
    public void Manual_help_buttons()
    {
        Dark();
        var md = ManualWindow.Load("en");
        var start = md.IndexOf("## Something doesn't work?", StringComparison.Ordinal);
        var end = md.IndexOf("\n## ", start + 5, StringComparison.Ordinal);
        var page = ManualWindow.Build(md[start..end], _ => { });
        var w = new Window { Content = new ScrollViewer { Content = page, Padding = new Thickness(36, 28) }, Width = 860, Height = 1100 };
        w.Show();
        Save(w, "manual-help-buttons" + Sfx);
        Assert.Equal(5, page.Children.OfType<Button>().Count());
        w.Close();
    }

    [AvaloniaFact]
    public void All_buttons_switch_on()
    {
        // The fresh check of test build 2's pictures (23 Nov 2026): on the light look the On track of the "All buttons" switch was invisible.
        Dark();
        var sw = new ToggleSwitch { IsChecked = true, OnContent = "", OffContent = "", MinWidth = 0 };
        var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6, Margin = new Thickness(24),
            Children = { new TextBlock { Text = "All buttons  Off" , VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }, sw,
                         new TextBlock { Text = "On", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center } } };
        var w = new Window { Content = row, Width = 320, Height = 90 };
        w.Show();
        Save(w, "all-buttons-switch-on" + Sfx);
        w.Close();
    }

    /// <summary>ZAK-LISTS L6.5 (Zak 24 Nov 2026: "Ok lets see your mockup for 3D buttons. Make 3 versions to choose from?"): the same
    /// pages with the three raised-button strengths, dark and light.</summary>
    [AvaloniaFact]
    public async Task Raised_button_versions_for_Zak()
    {
        var before = LightnessService.RaisedVariant;
        try
        {
            // OTA_RAISED_ONLY=3 (or "3d" = version 3, dark only) narrows the run when only one version is being tuned.
            var only = Environment.GetEnvironmentVariable("OTA_RAISED_ONLY") ?? "";
            foreach (var v in new[] { 1, 2, 3 })
            {
                if (only.Length > 0 && !only.StartsWith(v.ToString(), StringComparison.Ordinal)) continue;
                LightnessService.RaisedVariant = v; _tag = $"-v{v}";
                foreach (var light in new[] { false, true })
                    if (!(only.EndsWith("d", StringComparison.Ordinal) && light))
                {
                    _light = light;
                    Home_keys_not_set_up_button(); ExpressKeys_card_not_set_up(); Manual_help_buttons(); await Report_page_wont_open_button();
                }
            }
        }
        finally { LightnessService.RaisedVariant = before; _light = false; _tag = ""; LightnessService.Set(0); ThemeService.Apply(ThemeService.Wacom); }
    }

    [AvaloniaFact]
    public async Task Light_theme_versions()
    {
        _light = true;
        try { Home_keys_not_set_up_button(); ExpressKeys_card_not_set_up(); Manual_help_buttons(); All_buttons_switch_on(); await Report_page_wont_open_button(); }
        finally { _light = false; LightnessService.Set(0); ThemeService.Apply(ThemeService.Wacom); }
    }
}
