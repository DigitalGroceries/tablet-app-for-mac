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

/// <summary>V31 (task list L7.14-L7.25): Digital Groceries' mockup of 25 Nov 2026 shows Tablet › Pen with sunken sliders and panels,
/// a Tilt switch, inset boxes and embossed pictures. These pictures are the real page drawn the way his mockup was made: the
/// factory theme at Lightness 3 (his window is #222222), a window of 1046 x 1438 points and 1.248 picture dots for each point.
/// How that was measured: the Tip Feel box is 250 points wide and 312 dots wide in the picture of the app as it was; his window
/// is 1306 dots wide and 1795 dots high. Laid 1 dot from the left and 37 dots from the top of his picture (the Mac's menu
/// bar), the two can be compared dot for dot. Written to $OTA_SHOTS_DIR.
/// One picture more for each other look, for his OK before the test build (L7.24).</summary>
public class V31DesignShots
{
    static string Dir => Environment.GetEnvironmentVariable("OTA_SHOTS_DIR") ?? "/tmp/ota-v31-shots";

    /// <summary>The Pen page of a connected PTK-440 with factory settings, in a window of the mockup's size.</summary>
    internal static async Task<(MainWindow Window, TabletDetailView View)> OpenPenPage(double width = 1046, double height = 1438, double scale = 1.248)
    {
        var w = new MainWindow { Width = width, Height = height };
        w.Show();
        if (scale != 1) { w.SetRenderScaling(scale); await Settle(60); w.Width = width; w.Height = height; }
        await Settle(150);
        var vm = (MainViewModel)w.DataContext!;
        var details = (System.Collections.IDictionary)typeof(MainViewModel)
            .GetField("_tabletDetails", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(vm)!;
        var ed = DigitalGroceriesTablet.Page();
        details[DigitalGroceriesTablet.Name] = ed;
        vm.TabletPage.SetTablets(new[] { (DigitalGroceriesTablet.Name, true) });
        vm.CurrentPage = vm.TabletPage;
        await Settle(150);
        foreach (var host in w.GetVisualDescendants().OfType<TransitioningContentControl>()) host.IsEnabled = true;
        foreach (var b in w.GetVisualDescendants().OfType<Button>().Where(x => x.Name is "SaveButton" or "SaveAsDefaultButton"))
            b.IsEnabled = true;
        await Settle(60);
        var view = w.GetVisualDescendants().OfType<TabletDetailView>().First();
        view.FindControl<RadioButton>("ToolPenButton")!.IsChecked = true; await Settle(80);
        view.FindControl<RadioButton>("PenTab")!.IsChecked = true; await Settle(150);
        return (w, view);
    }

    static void Save(Window w, string name)
    {
        for (int i = 0; i < 8; i++) { Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); }
        Directory.CreateDirectory(Dir);
        w.CaptureRenderedFrame()!.Save(Path.Combine(Dir, name + ".png"));
    }

    [AvaloniaFact]
    public async Task Pen_page_like_the_mockup_in_every_look()
    {
        using var mac = UiPlatform.PretendMac();
        try
        {
            // the look and Lightness of his mockup first, then each other look on the step it starts on
            foreach (var (id, name, step) in new[]
            {
                (ThemeService.Wacom, "design-graphite-L3", 2), (ThemeService.Wacom, "design-graphite-L1", 0),
                (ThemeService.Wacom, "design-graphite-L8-light", 7), (ThemeService.DarkSakura, "design-burgundy", -1),
                (ThemeService.Custom, "design-custom", -1), (ThemeService.Anime, "design-rose", -1),
            })
            {
                var tvm = V31ThemeShots.Choose(id);
                if (step >= 0) tvm.Lightness = step;
                var (w, _) = await OpenPenPage();
                Save(w, name);
                w.Close();
            }
        }
        finally { V31ThemeShots.Choose(ThemeService.Wacom); LightnessService.Set(0); }
    }
}
