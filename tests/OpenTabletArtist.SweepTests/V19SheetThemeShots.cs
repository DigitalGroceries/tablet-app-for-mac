using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletArtist.Helpers;
using Xunit;

namespace OpenTabletArtist.SweepTests;

/// <summary>Version 19 BETA 3 (Zak: Add application showed white with pale text): in-window dialogs take the app
/// theme, also when the dialog window's own theme (macOS) is the opposite. Saves screenshots and checks contrast.</summary>
public class V19SheetThemeShots
{
    static string Dir => Environment.GetEnvironmentVariable("SHOTS_DIR") ?? "/workspace/otaE/v19b3/shots";

    static double Lum(Color c)
    {
        double F(byte v) { var x = v / 255.0; return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); }
        return 0.2126 * F(c.R) + 0.7152 * F(c.G) + 0.0722 * F(c.B);
    }
    static Color Over(Color fg, Color bg)
    {
        double a = fg.A / 255.0;
        return Color.FromRgb((byte)(fg.R * a + bg.R * (1 - a)), (byte)(fg.G * a + bg.G * (1 - a)), (byte)(fg.B * a + bg.B * (1 - a)));
    }
    static double Contrast(Color a, Color b) { var (x, y) = (Lum(a) + 0.05, Lum(b) + 0.05); return Math.Max(x, y) / Math.Min(x, y); }

    [AvaloniaTheory]
    [InlineData("dark", 0, "Light")]
    [InlineData("dark", 0, "Dark")]
    [InlineData("light", 8, "Dark")]
    [InlineData("light", 8, "Light")]
    public void AddApplication_FollowsTheAppTheme(string name, int step, string osTheme)
    {
        var app = Application.Current!;
        var before = Services.LightnessService.Saved;
        Services.LightnessService.Set(step);   // the app's own Lightness (dark at 0, light at 8)
        var appTheme = Services.LightnessService.For(step).Dark ? "Dark" : "Light";
        var mw = new MainWindow { Width = 1000, Height = 760 };
        mw.Show();
        InWindowSheet.MainOverride = () => mw;
        try
        {
            // The dialog as built before (its own window theme = macOS's, opposite to the app's).
            var title = "Add application";
            var dialog = new Window { Title = title, Width = 480, RequestedThemeVariant = osTheme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light,
                Content = new StackPanel { Margin = new Thickness(20), Spacing = 12, Children = {
                    new TextBlock { Text = "Choose a running application, or browse for one in the Applications folder.", TextWrapping = TextWrapping.Wrap },
                    new ListBox { Height = 160, ItemsSource = new[] { "Finder  (com.apple.finder)", "Preview  (com.apple.Preview)" } },
                    new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, Children = { new Button { Content = "Browse…" }, new Button { Content = "Cancel" }, new Button { Content = "Add" } } } } } };
            _ = InWindowSheet.ShowDialogAsync(dialog, mw);
            Dispatcher.UIThread.RunJobs(); mw.UpdateLayout(); Dispatcher.UIThread.RunJobs();

            System.IO.Directory.CreateDirectory(Dir);
            mw.CaptureRenderedFrame()!.Save($"{Dir}/add-application-{name}-app-{osTheme.ToLower()}-macos.png");

            var sheet = mw.GetVisualDescendants().OfType<Border>().First(b => b.Name == "Sheet");
            var bg = ((ISolidColorBrush)sheet.Background!).Color;
            var titleBlock = sheet.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == title);
            var body = sheet.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text!.StartsWith("Choose"));
            var ct = Contrast(Over(((ISolidColorBrush)titleBlock.Foreground!).Color, bg), bg);
            var cb = Contrast(Over(((ISolidColorBrush)body.Foreground!).Color, bg), bg);
            System.IO.File.AppendAllText($"{Dir}/contrast.txt", $"{name} (app {appTheme}, macOS {osTheme}): bg {bg} title {ct:F1}:1 body {cb:F1}:1\n");
            Assert.True(ct >= 4.5, $"title contrast {ct:F1}");
            Assert.True(cb >= 4.5, $"body contrast {cb:F1}");
            bool appDark = appTheme == "Dark";
            Assert.Equal(appDark, Lum(bg) < 0.2);
            dialog.Close(); Dispatcher.UIThread.RunJobs();
        }
        finally { InWindowSheet.MainOverride = null; mw.Close(); Services.LightnessService.Set(before); }
    }

    [AvaloniaTheory]
    [InlineData("dark", 0)]
    [InlineData("light", 8)]
    public void Manual_And_UpdatePrompt_FollowTheAppTheme(string name, int step)
    {
        var before = Services.LightnessService.Saved;
        Services.LightnessService.Set(step);
        var mw = new MainWindow { Width = 1000, Height = 760 };
        mw.Show();
        InWindowSheet.MainOverride = () => mw;
        try
        {
            System.IO.Directory.CreateDirectory(Dir);
            var manual = new Views.ManualWindow();
            _ = InWindowSheet.ShowDialogAsync(manual, mw);
            Dispatcher.UIThread.RunJobs(); mw.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            mw.CaptureRenderedFrame()!.Save($"{Dir}/manual-{name}.png");
            manual.Close(); Dispatcher.UIThread.RunJobs();
            _ = Views.UpdatePromptWindow.AskAsync("19 BETA 3");
            Dispatcher.UIThread.RunJobs(); mw.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            mw.CaptureRenderedFrame()!.Save($"{Dir}/update-prompt-{name}.png");
            var sheet = mw.GetVisualDescendants().OfType<Border>().First(b => b.Name == "Sheet");
            var bg = ((ISolidColorBrush)sheet.Background!).Color;
            foreach (var t in sheet.GetVisualDescendants().OfType<TextBlock>().Where(t => !string.IsNullOrEmpty(t.Text) && t.Foreground is ISolidColorBrush))
            {
                var c = Contrast(Over(((ISolidColorBrush)t.Foreground!).Color, bg), bg);
                Assert.True(c >= 4.5, $"'{t.Text}' contrast {c:F1}");
            }
        }
        finally { InWindowSheet.MainOverride = null; mw.Close(); Services.LightnessService.Set(before); }
    }
}
