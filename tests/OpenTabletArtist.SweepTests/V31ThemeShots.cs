using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using Xunit;
using static OpenTabletArtist.SweepTests.Sweep;

namespace OpenTabletArtist.SweepTests;

/// <summary>V31 (ZAK-LISTS L7, Zak 25 Nov 2026, his "Changes Needed" folder: Burgundy, Custom and Rose had unreadable tab
/// words, buttons without their raised look, an invisible Save and washed-out switches): pictures of Home and Tablet › Pen from
/// the real app (a connected PTK-440 with factory settings, <see cref="ZakTablet"/>) in the factory theme and the three skins,
/// for his look before the test build (L7.24). Written to $OTA_SHOTS_DIR.</summary>
public class V31ThemeShots
{
    static string Dir => Environment.GetEnvironmentVariable("OTA_SHOTS_DIR") ?? "/tmp/ota-v31-shots";

    public static readonly (string Id, string Name)[] Themes =
    {
        (ThemeService.Wacom, "graphite"), (ThemeService.DarkSakura, "burgundy"),
        (ThemeService.Custom, "custom"), (ThemeService.Anime, "rose"),
    };

    static void Save(Window w, string name)
    {
        for (int i = 0; i < 8; i++) { Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); }
        Directory.CreateDirectory(Dir);
        w.CaptureRenderedFrame()!.Save(Path.Combine(Dir, name + ".png"));
    }

    /// <summary>Chooses a theme the way the Theme page does (so the skins' own live colours are applied too).</summary>
    public static ThemeViewModel Choose(string id)
    {
        var tvm = new ThemeViewModel();
        tvm.SelectedTheme = tvm.ThemeOptions.First(o => o.Id == id);
        ThemeService.Apply(id);
        Localizer.SetPreference("en");
        // petals fall at random places; with OTA_NO_PETALS=1 two runs give the same picture, pixel for pixel (to compare a change)
        if (Environment.GetEnvironmentVariable("OTA_NO_PETALS") == "1") tvm.PetalsEnabled = false;
        return tvm;
    }

    [AvaloniaFact]
    public async Task Home_and_pen_page_in_every_theme()
    {
        using var mac = UiPlatform.PretendMac();
        try
        {
            foreach (var (id, name) in Themes)
            {
                Choose(id);
                var w = new MainWindow { Width = 1030, Height = 1600 };
                w.Show(); await Settle(150);
                var vm = (MainViewModel)w.DataContext!;
                vm.Theme.SelectedTheme = vm.Theme.ThemeOptions.First(o => o.Id == id); await Settle(80);
                var dash = w.GetVisualDescendants().OfType<DashboardView>().First();
                var home = (DashboardViewModel)dash.DataContext!;
                home.TabletsOverview.Tablets = new[]
                {
                    new TabletOverviewItemViewModel("Wacom PTK-440", true, "Detected", "CONNECTED", () => { }, () => Task.CompletedTask,
                        keysNotSetUp: 6, openExpressKeys: () => { }),
                }.ToList();
                home.TabletsOverview.HasTablets = true;
                w.Height = 860; await Settle(80);
                Save(w, $"{name}-1-home");

                w.Height = 1600;
                var details = (System.Collections.IDictionary)typeof(MainViewModel)
                    .GetField("_tabletDetails", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(vm)!;
                var ed = ZakTablet.Page();
                ed.ShowHeader = false;
                details[ZakTablet.Name] = ed;
                vm.TabletPage.SetTablets(new[] { (ZakTablet.Name, true) });
                vm.CurrentPage = vm.TabletPage;
                await Settle(150);
                foreach (var host in w.GetVisualDescendants().OfType<TransitioningContentControl>()) host.IsEnabled = true;
                foreach (var b in w.GetVisualDescendants().OfType<Button>().Where(x => x.Name is "SaveButton" or "SaveAsDefaultButton"))
                    b.IsEnabled = true;
                await Settle(60);
                var view = w.GetVisualDescendants().OfType<TabletDetailView>().First();
                view.FindControl<RadioButton>("ToolPenButton")!.IsChecked = true; await Settle(80);
                view.FindControl<RadioButton>("PenTab")!.IsChecked = true; await Settle(150);
                Save(w, $"{name}-2-pen");

                vm.CurrentPage = vm.Settings; vm.Settings.SelectedTab = OpenTabletArtist.Domain.SettingsTab.Theme;
                w.Height = 900; await Settle(200);
                Save(w, $"{name}-3-settings");
                w.Close();
            }
        }
        finally { Choose(ThemeService.Wacom); LightnessService.Set(0); }
    }

    static Avalonia.Media.Color Res(string key)
    {
        var app = Application.Current!;
        Assert.True(app.TryGetResource(key, app.ActualThemeVariant, out var v), $"no resource {key}");
        return v switch
        {
            Avalonia.Media.ISolidColorBrush b => b.Color,
            Avalonia.Media.Color c => c,
            Avalonia.Media.IGradientBrush g => g.GradientStops[0].Color,
            _ => throw new Xunit.Sdk.XunitException($"{key} is a {v?.GetType().Name}"),
        };
    }

    static Avalonia.Media.Color[] Stops(string key)
    {
        var app = Application.Current!;
        Assert.True(app.TryGetResource(key, app.ActualThemeVariant, out var v), $"no resource {key}");
        return v is Avalonia.Media.IGradientBrush g ? g.GradientStops.Select(x => x.Color).ToArray() : new[] { Res(key) };
    }

    /// <summary>L7.12 (and Zak 25 Nov 19:3x on the Custom picture: "The On Off buttons are still invisible"): in EVERY theme at EVERY
    /// Lightness step, chosen the way the Theme page chooses it, the colours the app really uses must be readable: the knob of a
    /// switch that is On against its track, the words of a tab and of a button (Save too) against the raised fill, the chosen tab's
    /// word against its fill, and the page text against a panel.</summary>
    [AvaloniaFact]
    public void Every_theme_and_step_has_readable_switches_tabs_and_buttons()
    {
        var bad = new System.Collections.Generic.List<string>();
        try
        {
            foreach (var id in new[] { ThemeService.Wacom, ThemeService.Light, ThemeService.Dark, ThemeService.Anime, ThemeService.DarkSakura, ThemeService.Custom })
            {
                var tvm = Choose(id);
                Assert.True(tvm.LightnessApplies, $"{id}: the Lightness slider is greyed");   // L7.11
                for (int step = 0; step < LightnessService.Steps; step++)
                {
                    tvm.Lightness = step;
                    Dispatcher.UIThread.RunJobs();
                    Assert.Equal(step, LightnessService.Saved);
                    void Need(string what, Avalonia.Media.Color a, Avalonia.Media.Color b, double min)
                    {
                        double c = LightnessService.Contrast(a, b);
                        if (c < min) bad.Add($"{id} step {step + 1}: {what} {c:0.00}:1 ({a} on {b})");
                    }
                    Need("switch knob on its track", Res("ToggleSwitchKnobFillOn"), Res("SwitchOnFillBrush"), 3.0);
                    Need("switch track on a panel", Res("SwitchOnFillBrush"), Res("WacomPaneBrush"), 3.0);
                    // a word sits between a fifth and four fifths of the way down its button (LightnessService.WordBandTop / Bottom,
                    // measured on the real pages); the shade is lightest at the top of that band and darkest at its bottom
                    var shade = Stops("ButtonRaisedFillBrush");
                    foreach (var share in new[] { LightnessService.WordBandTop, 0.5, LightnessService.WordBandBottom })
                    {
                        var fill = LightnessService.ShadeAt(shade[0], shade[^1], share);
                        Need($"tab word on a raised tab ({share:0.0} down)", Res("TabOffTextBrush"), fill, 4.5);
                        Need($"Save's word on a raised button ({share:0.0} down)", Res("AccentButtonForegroundBrush"), fill, 4.5);
                        Need($"button word on a raised button ({share:0.0} down)", Res("ButtonForeground"), fill, 4.5);
                    }
                    Need("chosen tab's word on its fill", Res("TabOnTextBrush"), Res("TabOnFillBrush"), 4.5);
                    Need("chosen tab's fill against a raised tab", Res("TabOnFillBrush"), LightnessService.ShadeAt(shade[0], shade[^1], 0.5), 3.0);
                    Need("the hint in an empty box", Res("TextControlPlaceholderForeground"), Res("TextControlBackground"), 3.0);
                    Need("text on a panel", Res("TextPrimaryBrush"), Res("WacomPaneBrush"), 4.5);
                    Need("pen circle (selected) on a panel", Flat(Res("PenEndCircleSelectedBrush"), Res("WacomPaneBrush")), Res("WacomPaneBrush"), 3.0);
                }
                tvm.Lightness = LightnessService.DefaultStepFor(id);
            }
        }
        finally { Choose(ThemeService.Wacom); LightnessService.Set(0); }
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    static Avalonia.Media.Color Flat(Avalonia.Media.Color top, Avalonia.Media.Color under)
    {
        double a = top.A / 255.0;
        byte M(byte t, byte u) => (byte)Math.Round(t * a + u * (1 - a));
        return Avalonia.Media.Color.FromRgb(M(top.R, under.R), M(top.G, under.G), M(top.B, under.B));
    }

    /// <summary>Home (one tablet with six keys not set up), then Tablet › Pen of a connected PTK-440 with factory settings, then
    /// Settings › Theme: the three pages of Zak's "Changes Needed" screenshots. The page takes input as it does on his Mac.</summary>
    internal static async Task ForEachPage(MainWindow w, Func<string, Task> visit)
    {
        var vm = (MainViewModel)w.DataContext!;
        var dash = w.GetVisualDescendants().OfType<DashboardView>().First();
        var home = (DashboardViewModel)dash.DataContext!;
        home.TabletsOverview.Tablets = new[]
        {
            new TabletOverviewItemViewModel("Wacom PTK-440", true, "Detected", "CONNECTED", () => { }, () => Task.CompletedTask,
                keysNotSetUp: 6, openExpressKeys: () => { }),
        }.ToList();
        home.TabletsOverview.HasTablets = true;
        await Settle(80);
        void TakeInput()
        {
            foreach (var host in w.GetVisualDescendants().OfType<TransitioningContentControl>()) host.IsEnabled = true;
            foreach (var name in new[] { "SaveButton", "SaveAsDefaultButton", "RevertToDefaultButton", "FactoryButton", "RestoreFactoryButton" })
                foreach (var b in w.GetVisualDescendants().OfType<Button>().Where(x => x.Name == name)) b.IsEnabled = true;
            foreach (var p in w.GetVisualDescendants().OfType<StackPanel>().Where(x => x.Children.OfType<Button>().Any(b => b.Name == "SaveButton"))) p.IsEnabled = true;
        }
        TakeInput(); await Settle(40);
        double tall = w.Height;
        w.Height = Math.Min(tall, 860); await Settle(40);   // Home and Settings fit a smaller window (a picture of it is quicker)
        await visit("home");
        w.Height = tall;

        var details = (System.Collections.IDictionary)typeof(MainViewModel)
            .GetField("_tabletDetails", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(vm)!;
        var ed = ZakTablet.Page();
        ed.ShowHeader = false;
        details[ZakTablet.Name] = ed;
        vm.TabletPage.SetTablets(new[] { (ZakTablet.Name, true) });
        vm.CurrentPage = vm.TabletPage;
        await Settle(150);
        TakeInput(); await Settle(60);
        var view = w.GetVisualDescendants().OfType<TabletDetailView>().First();
        view.FindControl<RadioButton>("ToolPenButton")!.IsChecked = true; await Settle(80);
        view.FindControl<RadioButton>("PenTab")!.IsChecked = true; await Settle(150);
        await visit("pen");

        vm.CurrentPage = vm.Settings; vm.Settings.SelectedTab = OpenTabletArtist.Domain.SettingsTab.Theme;
        w.Height = Math.Min(tall, 900);
        await Settle(200);
        TakeInput(); await Settle(40);
        await visit("settings-theme");
        w.Height = tall;
    }

    /// <summary>L7.12, the words: Home, Tablet › Pen and Settings › Theme (the pages of Zak's "Changes Needed" screenshots) in the
    /// grey look, Burgundy, Rose and Custom at EVERY Lightness step, measured from the pixels really drawn
    /// (<see cref="ReadabilityAudit"/>): a word on a button or tab needs 4.5:1 against what is behind it, any other word 3:1.
    /// Burgundy's dark tab words on a dark window (1.2:1), Rose's "Save" and Custom's switch knob would each have failed here.
    /// The list of the weakest words of every look goes to words-every-theme.md in OTA_SHOTS_DIR.</summary>
    [AvaloniaFact]
    public async Task Words_can_be_read_in_every_theme_at_every_step()
    {
        using var mac = UiPlatform.PretendMac();
        var low = new System.Collections.Generic.List<string>();
        var lines = new System.Collections.Generic.List<string> { "# The weakest words of every look, measured from the pixels", "" };
        int measured = 0;
        try
        {
            foreach (var (id, name) in Themes)
            {
                Choose(id);
                var w = new MainWindow { Width = 1030, Height = 1600 };
                w.Show(); await Settle(150);
                var vm = (MainViewModel)w.DataContext!;
                vm.Theme.SelectedTheme = vm.Theme.ThemeOptions.First(o => o.Id == id); await Settle(60);
                await ForEachPage(w, slug =>
                {
                    for (int step = 0; step < LightnessService.Steps; step++)
                    {
                        vm.Theme.Lightness = step;
                        Pump(2); w.UpdateLayout(); Pump(1);
                        Assert.Equal(step, LightnessService.Saved);
                        var words = ReadabilityAudit.Measure(w, $"{name} Lightness {step + 1}, {slug}");
                        Assert.True(words.Count >= 20, $"{name} Lightness {step + 1}, {slug}: only {words.Count} words were found - is the page drawn?");
                        measured += words.Count;
                        low.AddRange(words.Where(x => !x.Ok).Select(x => x.ToString()));
                        var weakest = words.OrderBy(x => x.Contrast / x.Needed).First();
                        lines.Add($"- {name} Lightness {step + 1}, {slug}: {words.Count} words, weakest {weakest.Contrast:0.00}:1 (needs {weakest.Needed:0.0}) '{weakest.Text}'");
                    }
                    vm.Theme.Lightness = LightnessService.DefaultStepFor(id);
                    Pump(2);
                    return Task.CompletedTask;
                });
                w.Close();
            }
        }
        finally { Choose(ThemeService.Wacom); LightnessService.Set(0); }
        lines.Add(""); lines.Add($"{measured} words measured; {low.Count} under their line.");
        lines.AddRange(low.Select(x => "LOW " + x));
        Directory.CreateDirectory(Dir);
        File.WriteAllLines(Path.Combine(Dir, "words-every-theme.md"), lines);
        Assert.True(low.Count == 0, $"{low.Count} words cannot be read well enough:\n" + string.Join("\n", low.Take(60)));
    }
}
