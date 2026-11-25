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

    static async Task Settle(int ms = 60)
    {
        for (int i = 0; i < 6; i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(ms / 6 + 1); }
        Dispatcher.UIThread.RunJobs();
    }

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
}
