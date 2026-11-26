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

    /// <summary>The sheet's drawn tablet for a few families (proven layouts from the manuals, and the plain column):
    /// key-mapping-layout-*.png, the second key lit.</summary>
    [AvaloniaFact]
    public void The_sheet_per_tablet_family()
    {
        Directory.CreateDirectory(Dir);
        using var mac = UiPlatform.PretendMac();
        _ = new ThemeViewModel();
        foreach (var (file, tablet, count, turned) in new[]
                 {
                     ("ptk-440", "Wacom PTK-440", 6, false),
                     ("ptk-440-turned", "Wacom PTK-440", 6, true),
                     ("ctl-4100wl", "Wacom CTL-4100WL", 5, false),
                     ("ptz-930-intuos3", "Wacom PTZ-930", 8, false),
                     ("cte-450-bamboo-fun", "Wacom CTE-450", 4, false),
                     ("pth-660-plain-column", "Wacom PTH-660", 8, false),
                 })
        {
            var vm = new KeyMappingViewModel(count, null, turned, KeyLayouts.For(tablet, count));
            Tap(vm, count, 0);
            var sheet = new KeyMappingDialog(vm);
            sheet.Show();
            RadialSpotShots.Settle(sheet);
            Assert.Equal(count, sheet.KeySpots.Count);
            sheet.CaptureRenderedFrame()!.Save($"{Dir}/key-mapping-layout-{file}.png");
            sheet.Close();
        }
    }

    /// <summary>TASKS #212 part 4: the steps after the keys - the drawn ring lit (Intuos4 S), a strip beside the words
    /// (Intuos3), the touch step (Intuos Pro 2013 M) and an end screen that names a strip counting the other way.
    /// Output: key-mapping-part4-*.png.</summary>
    [AvaloniaFact]
    public void The_ring_strip_and_touch_steps()
    {
        Directory.CreateDirectory(Dir);
        using var mac = UiPlatform.PretendMac();
        _ = new ThemeViewModel();

        void Shot(KeyMappingViewModel vm, string file)
        {
            var sheet = new KeyMappingDialog(vm);
            sheet.Show();
            RadialSpotShots.Settle(sheet);
            sheet.CaptureRenderedFrame()!.Save($"{Dir}/key-mapping-part4-{file}.png");
            sheet.Close();
        }

        var ring = new KeyMappingViewModel(6, null, false, KeyLayouts.For("Wacom PTK-440", 6),
            ControlCheck.For(new[] { (false, "Touch Ring") }, false, "Touch"));
        for (int k = 0; k < 6; k++) Tap(ring, 6, k);
        Assert.True(ring.IsRingLit);
        Shot(ring, "ring-ptk-440");

        var strips = new KeyMappingViewModel(8, null, false, KeyLayouts.For("Wacom PTZ-930", 8),
            ControlCheck.For(new[] { (true, "Left Touch Strip"), (true, "Right Touch Strip") }, false, "Touch"));
        for (int k = 0; k < 8; k++) Tap(strips, 8, k);
        Shot(strips, "strip-ptz-930");

        var touch = new KeyMappingViewModel(8, null, false, KeyLayouts.For("Wacom PTH-651", 8),
            ControlCheck.For(new[] { (false, "Touch Ring") }, true, "Touch"));
        for (int k = 0; k < 8; k++) Tap(touch, 8, k);
        touch.OnWheelPosition(0, 10);
        Shot(touch, "touch-pth-651");

        strips.OnWheelPosition(0, 10); strips.OnWheelPosition(0, 6);        // left strip: as the app expects
        strips.OnWheelPosition(1, 2); strips.OnWheelPosition(1, 7);         // right strip: the other way round
        Assert.True(strips.IsFinished);
        Shot(strips, "end-ptz-930");
    }

    /// <summary>TASKS #212 part 5: the last screen's "Give the keys Wacom's own functions?" - Intuos4 M (8 keys, four
    /// the app can do, the radial menu one of them), Intuos4 S with a skipped key and "Wacom's functions" picked,
    /// Intuos3 (two groups of the same four), Intuos 2013 (Control not in the list). Output: key-mapping-part5-*.png.</summary>
    [AvaloniaFact]
    public void The_Wacom_functions_offer()
    {
        Directory.CreateDirectory(Dir);
        using var mac = UiPlatform.PretendMac();
        _ = new ThemeViewModel();
        foreach (var (file, tablet, count, skip, use) in new[]
                 {
                     ("offer-ptk-640", "Wacom PTK-640", 8, -1, false),
                     ("offer-ptk-440-skipped-chosen", "Wacom PTK-440", 6, 3, true),
                     ("offer-ptz-930", "Wacom PTZ-930", 8, -1, false),
                     ("offer-cth-480", "Wacom CTH-480", 4, -1, false),
                 })
        {
            var vm = new KeyMappingViewModel(count, null, false, KeyLayouts.For(tablet, count),
                factory: WacomFactoryKeys.For(tablet, count, false));
            for (int k = 0; k < count; k++)
                if (k == skip) vm.SkipCommand.Execute(null); else Tap(vm, count, k);
            Assert.True(vm.OffersWacomFunctions, tablet);
            var sheet = new KeyMappingDialog(vm);
            sheet.Show();
            if (use) sheet.UseWacomButton.IsChecked = true;
            RadialSpotShots.Settle(sheet);
            Assert.True(sheet.WacomChoice.IsVisible);
            sheet.CaptureRenderedFrame()!.Save($"{Dir}/key-mapping-part5-{file}.png");
            sheet.Close();
        }
    }

    /// <summary>Part 9: the sheet in long languages - while it runs (with a remark) and on its last screen with the
    /// Wacom offer - to see that no text is cut and the buttons fit.</summary>
    [AvaloniaTheory]
    [InlineData("de")]
    [InlineData("ru")]
    [InlineData("bg")]
    [InlineData("fr")]
    [InlineData("ja")]
    public void The_sheet_in_a_long_language(string code)
    {
        Directory.CreateDirectory(Dir);
        using var mac = UiPlatform.PretendMac();
        _ = new ThemeViewModel();
        Localizer.SetPreference(code);
        try
        {
            const string tablet = "Wacom PTK-640";
            var vm = new KeyMappingViewModel(8, null, false, KeyLayouts.For(tablet, 8), factory: WacomFactoryKeys.For(tablet, 8, false));
            var sheet = new KeyMappingDialog(vm);
            sheet.Show();
            Tap(vm, 8, 0); Tap(vm, 8, 0);                    // the "already did" remark
            RadialSpotShots.Settle(sheet);
            sheet.CaptureRenderedFrame()!.Save($"{Dir}/key-mapping-lang-{code}-running.png");
            for (int k = 1; k < 8; k++) Tap(vm, 8, k);
            RadialSpotShots.Settle(sheet);
            sheet.CaptureRenderedFrame()!.Save($"{Dir}/key-mapping-lang-{code}-end.png");
            sheet.Close();
        }
        finally { Localizer.SetPreference("en"); }
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
