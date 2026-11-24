using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using OpenTabletArtist.Controls;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Helpers;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using Xunit;
using static OpenTabletArtist.SweepTests.Sweep;

namespace OpenTabletArtist.SweepTests;

/// <summary>V30 build 5 pictures for Zak (24 Nov ~17:43: "In Tablet / Pen when you click on the radial menu, it opens a popup window to
/// set it up. I want the same for the colour pallet under the radial menu."). As his app (Zak ~15:50: "Dont create mockup pictures that
/// are different from the app"): the app window, Mac layout, a connected PTK-440 with factory settings, real mouse clicks.
/// 1 the Pen page, 2 the palette window after clicking the palette, 3 the colour picker after clicking one colour,
/// 4 the palette window with that colour changed, 5 the Pen page after Save.</summary>
public class V30PaletteShots
{
    static string Dir => Environment.GetEnvironmentVariable("OTA_SHOTS_DIR") ?? "/tmp/ota-v30-shots";

    static void Save(Window w, string name)
    {
        for (int i = 0; i < 8; i++) { Pump(); w.UpdateLayout(); }
        Directory.CreateDirectory(Dir);
        w.CaptureRenderedFrame()!.Save(Path.Combine(Dir, name + ".png"));
    }

    static Point Centre(Visual v, Visual root) => v.TranslatePoint(new Point(v.Bounds.Width / 2, v.Bounds.Height / 2), root)!.Value;

    static async Task Click(Window w, Point p)
    {
        w.MouseMove(p); await Settle(30);
        w.MouseDown(p, MouseButton.Left); await Settle(20);
        w.MouseUp(p, MouseButton.Left); await Settle(120);
    }

    [AvaloniaFact]
    public async Task Palette_window_for_Zak()
    {
        foreach (var light in new[] { false, true })
        {
            using var mac = UiPlatform.PretendMac();
            Hook();
            _ = new ThemeViewModel();
            ThemeService.Apply(ThemeService.Wacom);
            LightnessService.Set(light ? 8 : 0);
            Localizer.SetPreference("en");
            var sfx = light ? "-light" : "";

            var w = new MainWindow { Width = 1130, Height = 1000 };
            w.Show(); await Settle(150);
            InWindowSheet.MainOverride = () => w;   // windows open inside the app window, as in the app
            try
            {
                var vm = (MainViewModel)w.DataContext!;
                var details = (System.Collections.IDictionary)typeof(MainViewModel)
                    .GetField("_tabletDetails", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(vm)!;
                var ed = ZakTablet.Page();
                ed.ShowHeader = false;
                details[ZakTablet.Name] = ed;
                vm.TabletPage.SetTablets(new[] { (ZakTablet.Name, true) });
                vm.CurrentPage = vm.TabletPage;
                await Settle(150);
                // no driver in this test window: the page takes input as it does on Zak's Mac (see ButtonLookAuditTests)
                foreach (var host in w.GetVisualDescendants().OfType<TransitioningContentControl>()) host.IsEnabled = true;
                foreach (var b in w.GetVisualDescendants().OfType<Button>().Where(x => x.Name is "SaveButton" or "SaveAsDefaultButton" or "RevertToDefaultButton"))
                    b.IsEnabled = true;
                await Settle(60);
                var view = w.GetVisualDescendants().OfType<TabletDetailView>().First();
                view.FindControl<RadioButton>("ToolPenButton")!.IsChecked = true; await Settle(80);
                var penTab = view.FindControl<RadioButton>("PenTab")!;
                penTab.IsChecked = true; await Settle(100);

                var paletteButton = view.FindControl<Button>("PaletteSpotButton")!;
                var wheelOnPage = view.FindControl<ColourPaletteWheel>("PaletteSpotWheel")!;
                // scroll the page so the whole palette shows, as Zak would to reach it
                var sv = wheelOnPage.GetVisualAncestors().OfType<ScrollViewer>().First();
                var bottom = wheelOnPage.TranslatePoint(new Point(0, wheelOnPage.Bounds.Height), sv)!.Value.Y;
                if (bottom > sv.Viewport.Height - 12) sv.Offset = new Vector(sv.Offset.X, sv.Offset.Y + bottom - sv.Viewport.Height + 24);
                await Settle(100);
                w.MouseMove(Centre(wheelOnPage, w) + new Point(0, -120)); await Settle(60);   // the pointer over the palette (hover)
                Save(w, "palette-1-pen-page" + sfx);

                await Click(w, Centre(wheelOnPage, w) + new Point(0, -120));
                var dialog = w.GetVisualDescendants().OfType<ColourPaletteWheel>().FirstOrDefault(x => x != wheelOnPage && x.IsEffectivelyVisible);
                Assert.NotNull(dialog);                                                       // the palette window is open
                Assert.Contains(w.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Colour Palette Settings" && t.IsEffectivelyVisible);
                Save(w, "palette-2-window" + sfx);

                // click one colour: the outer ring, top (the same hit test as the window's)
                var big = dialog!;
                Point? spot = null;
                for (double y = 0; y < big.Bounds.Height && spot == null; y += 2)
                    if (ColourPalette.SwatchAt(0, y - big.Bounds.Height / 2, big.MenuSize) is int s && s >= 0 && s < ColourPalette.Count)
                        spot = new Point(big.Bounds.Width / 2, y + 6);
                Assert.NotNull(spot);
                int swatch = ColourPalette.SwatchAt(spot!.Value.X - big.Bounds.Width / 2, spot.Value.Y - big.Bounds.Height / 2, big.MenuSize);
                await Click(w, big.TranslatePoint(spot.Value, w)!.Value);
                var picker = w.GetVisualDescendants().OfType<ColorPicker>().LastOrDefault(x => x.IsEffectivelyVisible);
                Assert.NotNull(picker);                                                       // the palette pencil's colour picker
                picker!.Color = Color.Parse("#FF2D9B");
                await Settle(60);
                await Click(w, Centre(picker.FindControl<Button>("Trigger")!, w));          // open its wheel, swatches and hex code
                Save(w, "palette-3-colour-picker" + sfx);
                // V30 build 6 (Zak's build 5 screenshot 21:22): the open drop-down must not cover Cancel or Save
                var flyoutRoot = w.GetVisualDescendants().OfType<Avalonia.Controls.FlyoutPresenter>().FirstOrDefault(f => f.IsEffectivelyVisible);
                Assert.NotNull(flyoutRoot);
                var fr = new Rect(flyoutRoot!.TranslatePoint(new Point(0, 0), w)!.Value, flyoutRoot.Bounds.Size);
                foreach (var b in picker.GetVisualAncestors().OfType<Grid>().First().GetVisualDescendants().OfType<Button>().Where(b => b.Content is "Cancel" or "Save"))
                    Assert.False(fr.Intersects(new Rect(b.TranslatePoint(new Point(0, 0), w)!.Value, b.Bounds.Size)), $"the drop-down {fr} covers {b.Content}");
                picker.FindControl<Button>("Trigger")!.Flyout?.Hide(); await Settle(60);

                // the picker's own Save: on the colour box's row (V30 build 6)
                var pickerSave = picker.GetVisualAncestors().OfType<Grid>().First()
                    .GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "Save");
                var pickerCancel = picker.GetVisualAncestors().OfType<Grid>().First()
                    .GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "Cancel");
                pickerSave.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Settle(120);
                Assert.Equal("#FF2D9B", ColourPalette.HexFor(big.Menu!, swatch));          // the window shows the new colour
                w.MouseMove(new Point(4, 4)); await Settle(40);
                Save(w, "palette-4-colour-changed" + sfx);

                var save = w.GetVisualDescendants().OfType<Button>().Last(b => b.Content as string == "Save" && b.IsEffectivelyVisible);
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                for (int i = 0; i < 40 && ColourPalette.HexFor(wheelOnPage.Menu!, swatch) != "#FF2D9B"; i++) await Settle(50);
                Assert.Equal("#FF2D9B", ColourPalette.HexFor(wheelOnPage.Menu!, swatch));   // and the page after Save
                Save(w, "palette-5-pen-page-after-save" + sfx);
            }
            finally
            {
                InWindowSheet.MainOverride = null;
                w.AllowCloseForQuit(); w.Close();
            }
        }
    }
}
