using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using Xunit;

namespace OpenTabletArtist.SweepTests;

/// <summary>TASKS #212, step-by-step button mapping: pictures of the "Set up my buttons" sheet at its three moments and
/// of the ExpressKeys page with a saved order (eight keys in two groups). Output: key-mapping-*.png.</summary>
public class KeyMappingShots
{
    static string Dir => System.Environment.GetEnvironmentVariable("OTA_SHOTS") ?? Sweep.ShotsRoot;

    static bool[] Down(int count, int key) { var s = new bool[count]; s[key] = true; return s; }
    static void Tap(KeyMappingViewModel vm, int count, int key) { vm.OnKeys(Down(count, key)); vm.OnKeys(new bool[count]); }

    [AvaloniaFact]
    public void The_sheet()
    {
        Directory.CreateDirectory(Dir);
        using var mac = UiPlatform.PretendMac();
        _ = new ThemeViewModel();

        var vm = new KeyMappingViewModel(8, new[] { 4, 4 });
        var sheet = new KeyMappingDialog(vm);
        sheet.Show();
        RadialSpotShots.Settle(sheet);
        sheet.CaptureRenderedFrame()!.Save($"{Dir}/key-mapping-sheet-1-start.png");

        Tap(vm, 8, 5); Tap(vm, 8, 4);
        vm.SkipCommand.Execute(null);
        Tap(vm, 8, 5);                                       // a key that was already done
        RadialSpotShots.Settle(sheet);
        sheet.CaptureRenderedFrame()!.Save($"{Dir}/key-mapping-sheet-2-already-done.png");

        Tap(vm, 8, 7); Tap(vm, 8, 0); Tap(vm, 8, 1); Tap(vm, 8, 2); Tap(vm, 8, 3);
        RadialSpotShots.Settle(sheet);
        Assert.True(vm.IsFinished);
        sheet.CaptureRenderedFrame()!.Save($"{Dir}/key-mapping-sheet-3-end.png");
        sheet.Close();
    }

    [AvaloniaFact]
    public void The_ExpressKeys_page_with_a_saved_order()
    {
        Directory.CreateDirectory(Dir);
        using var mac = UiPlatform.PretendMac();
        _ = new ThemeViewModel();
        const string tablet = "TestTablet-KeyMapping-Shots";
        try
        {
            KeyOrderStore.Save(tablet, new KeyOrderMap(new[] { 5, 4, 6, 7, 0, 1, 2, 3 }, groups: new[] { 4, 4 }));
            var p = new Profile
            {
                Tablet = tablet,
                OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)),
                AbsoluteModeSettings = new AbsoluteModeSettings
                {
                    Display = new AreaSettings { Width = 1496, Height = 967, X = 748, Y = 483 },
                    Tablet = new AreaSettings { Width = 223.5f, Height = 139.7f, X = 111.75f, Y = 69.85f },
                },
                BindingSettings = new BindingSettings(),
            };
            p.BindingSettings.AuxButtons = new PluginSettingStoreCollection();
            foreach (var k in new[] { "A", "B", "C", "D", "E", "F", "G", "H" }) p.BindingSettings.AuxButtons.Add(AuxKeyBinding.MakeKeyBinding(k)!);
            p.BindingSettings.WheelBindings = new List<WheelBindingSettings> { new() };
            var settings = new Settings { Profiles = new ProfileCollection { p } };
            var vm = new TabletDetailViewModel(p, settings, applyAction: _ => Task.FromResult(OtdInterop.SettingsApplyOutcome.Live),
                editBinding: (x, _) => Task.FromResult<AuxBinding?>(null));
            vm.ShowHeader = false;
            var view = new TabletDetailView { DataContext = vm };
            var win = new Window { Content = new Border { Child = view, Margin = new Thickness(24, 86, 24, 62) }, Width = 1300, Height = 1000 };
            win.Show();
            typeof(TabletDetailView).GetMethod("SelectTab", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(view, new object[] { TabletDetailTab.ExpressKeys });
            RadialSpotShots.Settle(win);
            RadialSpotShots.Settle(win);
            Assert.Equal(new[] { "F", "E", "G", "H", "A", "B", "C", "D" }, vm.AuxButtonsInPlace.Select(b => b.Summary));
            win.CaptureRenderedFrame()!.Save($"{Dir}/key-mapping-page-saved-order.png");
            win.Close();
        }
        finally { KeyOrderStore.Clear(tablet); }
    }
}
