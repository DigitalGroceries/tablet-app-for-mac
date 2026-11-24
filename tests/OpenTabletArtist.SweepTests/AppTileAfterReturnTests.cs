using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using Xunit;
using static OpenTabletArtist.SweepTests.Sweep;

namespace OpenTabletArtist.SweepTests;

/// <summary>V30 build 5 test (Zak 24 Nov 2026 21:18-21:30): on Tablet › Pen with "All" lit, pen Button 1 showed "Radial menu" -
/// Photoshop's setting, not All's. "I did not move anythin. All I did was to open the app pages and take the screenshots", then
/// "Photoshop" and two screenshots (Photoshop tile: Button 1 radial menu; All tile: Button 1 right click). His path: Functions,
/// choose Adobe Photoshop, Settings › Theme, back to Tablet › Pen. Coming back builds the page again; the Application row lit
/// "All" (its default) while the page still showed the chosen application's table. The lit tile must always be the table shown.</summary>
public class AppTileAfterReturnTests
{
    const string Ps = "com.adobe.Photoshop";

    [AvaloniaFact]
    public async Task Leaving_the_Tablet_page_and_coming_back_lights_the_application_whose_settings_are_shown()
    {
        using var mac = UiPlatform.PretendMac();
        Hook();
        _ = new ThemeViewModel();
        Localizer.SetPreference("en");
        var w = new MainWindow { Width = 1130, Height = 1000 };
        w.Show(); await Settle(150);
        try
        {
            var vm = (MainViewModel)w.DataContext!;
            var details = (System.Collections.IDictionary)typeof(MainViewModel)
                .GetField("_tabletDetails", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(vm)!;
            var ed = ZakTablet.Page();
            ed.ShowHeader = false;
            // Zak's Photoshop table: pen button 1 opens the radial menu (and the palette, pressed twice); All keeps the factory settings
            var ps = Newtonsoft.Json.JsonConvert.DeserializeObject<BindingSettings>(Newtonsoft.Json.JsonConvert.SerializeObject(ed.EditorAllBindings()))!;
            ps.PenButtons![0] = RadialMenuBinding.Make();
            ps.PenButtons[1] = RadialMenuBinding.WithPalette(RadialMenuBinding.Make(), false);
            PerAppBindingStore.Remove(ZakTablet.Name, Ps);
            PerAppBindingStore.Add(ZakTablet.Name, Ps, "Adobe Photoshop", "", ps);
            ed.LoadAppProfiles();
            details[ZakTablet.Name] = ed;
            vm.TabletPage.SetTablets(new[] { (ZakTablet.Name, true) });
            vm.CurrentPage = vm.TabletPage;
            await Settle(150);
            foreach (var host in w.GetVisualDescendants().OfType<TransitioningContentControl>()) host.IsEnabled = true;
            await Settle(60);

            var view = w.GetVisualDescendants().OfType<TabletDetailView>().First();
            view.FindControl<RadioButton>("ToolFunctionsButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Settle(80);
            var psTile = view.GetVisualDescendants().OfType<RadioButton>().First(r => r.Tag as string == Ps);
            psTile.IsChecked = true;
            psTile.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Settle(80);   // Zak chose Adobe Photoshop
            Assert.Equal(Ps, ed.SelectedAppBundleId);

            vm.Settings.SelectedTab = SettingsTab.Theme; vm.NavigateCommand.Execute(vm.Settings); await Settle(120);   // Settings › Theme
            vm.CurrentPage = vm.TabletPage; await Settle(150);                                                              // back to Tablet
            foreach (var host in w.GetVisualDescendants().OfType<TransitioningContentControl>()) host.IsEnabled = true;
            var back = w.GetVisualDescendants().OfType<TabletDetailView>().First();
            back.FindControl<RadioButton>("ToolPenButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Settle(100);

            // the page shows the table of ed.SelectedAppBundleId; the lit tile must say the same
            var all = back.FindControl<RadioButton>("AppAllTile")!;
            var psBack = back.GetVisualDescendants().OfType<RadioButton>().First(r => r.Tag as string == Ps);
            // a picture of it for Zak (his path, his tablet), dark look as his app
            for (int i = 0; i < 8; i++) { Pump(); w.UpdateLayout(); }
            var dir = Environment.GetEnvironmentVariable("OTA_SHOTS_DIR") ?? "/tmp/ota-v30-shots";
            System.IO.Directory.CreateDirectory(dir);
            Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(w)!.Save(System.IO.Path.Combine(dir, "app-tile-after-return.png"));
            bool showsPs = ed.SelectedAppBundleId == Ps;
            Assert.True(showsPs == (psBack.IsChecked == true) && showsPs == (all.IsChecked != true),
                $"page shows {(showsPs ? "Photoshop" : "All")}, but lit: All={all.IsChecked}, Photoshop={psBack.IsChecked}");
        }
        finally
        {
            PerAppBindingStore.Remove(ZakTablet.Name, Ps);
            w.AllowCloseForQuit(); w.Close();
        }
    }
}
