using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>Build R2: the lightness slider is on Settings › Theme and recolours the app live.</summary>
public class BuildR2LightnessUiTests
{
    [AvaloniaFact]
    public void Slider_IsOnTheThemePage_AndRecoloursLive()
    {
        var before = LightnessService.Saved;
        try
        {
            var view = new ThemeView { DataContext = new ThemeViewModel() };
            var w = new Window { Content = view, Width = 1000, Height = 800 };
            w.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(view.FindControl<Slider>("LightnessSlider"));
            foreach (var step in new[] { 0, LightnessService.Steps - 1 })
            {
                LightnessService.Set(step);
                Dispatcher.UIThread.RunJobs();
                if (!LightnessService.DrivesTheme(ThemeService.SavedChoice)) continue;
                Assert.True(Application.Current!.TryGetResource("BgBaseBrush", null, out var b));
                Assert.Equal(LightnessService.For(step).Base, ((ISolidColorBrush)b!).Color);
            }
            w.Close();
        }
        finally { LightnessService.Set(before); }
    }

    [AvaloniaFact]
    public void ThemePage_HasNoLookDescription_NeutralTooltip_AndDarkWacomSwatch()
    {
        var vm = new ThemeViewModel();
        var view = new ThemeView { DataContext = vm };
        var w = new Window { Content = view, Width = 1000, Height = 800 };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        var texts = Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(view).OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
        foreach (var o in vm.ThemeOptions) Assert.DoesNotContain(o.Description, texts);
        Assert.Equal("How light the app looks. Applies at once.",
            ToolTip.GetTip(view.FindControl<Slider>("LightnessSlider")!) as string);
        var sw = (ISolidColorBrush)vm.ThemeOptions.First(o => o.Id == ThemeService.Wacom).Swatch;
        Assert.Equal(LightnessService.For(LightnessService.Saved).Surface, sw.Color);
        Assert.True(LightnessService.Luminance(sw.Color) < 0.9);
        w.Close();
    }
}
