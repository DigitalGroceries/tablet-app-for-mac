using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletArtist.Converters;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>Build R12: no all-caps labels, pen headings Pen tip / Eraser /
/// Button 1 / Button 2 at one size, neutral grey accent, selected tiles #D8D8D8 on #141414, nothing brighter
/// than #E4E4E4, Max hover reachable, full presets path, idempotent shell dispose.</summary>
public class BuildR12Tests
{
    [AvaloniaFact]
    public void TitleCase_FoldsAllCaps_AndPassesControlsThrough()
    {
        Assert.Equal("Revert to saved", TitleCaseConverter.Apply("REVERT TO SAVED"));
        var control = new Border();
        Assert.Same(control, TitleCaseConverter.Instance.Convert(control, typeof(object), null, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(PenSwitchKind.Tip, 0, "Pen tip")]
    [InlineData(PenSwitchKind.Eraser, 0, "Eraser")]
    [InlineData(PenSwitchKind.PenButton, 1, "Button 1")]
    [InlineData(PenSwitchKind.PenButton, 2, "Button 2")]
    public void PenCardHeadings(PenSwitchKind kind, int index, string expected)
    {
        var row = new PenSwitchRowViewModel(kind, index, null, false, (_, _, _) => System.Threading.Tasks.Task.CompletedTask);
        Assert.Equal(expected, row.SectionLabel);
    }

    [AvaloniaFact]
    public void PenHeadings_HaveNoHeroSize()
    {
        var src = System.IO.File.ReadAllText(Path("OpenTabletArtist/Views/TabletDetailView.axaml"));
        Assert.DoesNotContain("Classes=\"hero\"", src);
    }

    [AvaloniaFact]
    public void Accent_IsNeutralGrey()
    {
        var a = LightnessService.Accent;
        Assert.True(a.R == a.G && a.G == a.B, $"accent {a}");
    }

    [AvaloniaFact]
    public void FluentWhites_AreClampedTo_E4_InEveryTheme()
    {
        var theme = new ThemeViewModel();
        foreach (var opt in theme.ThemeOptions)
        {
            theme.SelectedTheme = opt;
            Dispatcher.UIThread.RunJobs();
            var app = Application.Current!;
            foreach (var k in new[] { "TextFillColorPrimaryBrush", "TextFillColorSecondaryBrush", "ComboBoxDropDownGlyphForeground",
                         "ToggleSwitchKnobFillOff", "CheckBoxForegroundUnchecked", "ButtonForeground", "RepeatButtonForeground" })
            {
                if (!app.TryGetResource(k, app.ActualThemeVariant, out var v)) continue;
                if (v is ISolidColorBrush b) Assert.False(NoNearWhite.IsTooBright(b.Color), $"{opt.Label}: {k} = {b.Color}");
            }
        }
        theme.SelectedTheme = theme.ThemeOptions.First(o => o.Id == ThemeService.Wacom);
    }

    [AvaloniaFact]
    public void SelectedTile_IsLightFillWithDarkText()
    {
        _ = new ThemeViewModel();
        var vm = new TabletDetailViewModel();
        var view = new TabletDetailView { DataContext = vm };
        var w = new Window { Content = view, Width = 1100, Height = 800 }; w.Show(); Dispatcher.UIThread.RunJobs();
        var tile = view.FindControl<RadioButton>("ToolPenButton")!;
        Assert.True(tile.IsChecked);
        var bd = tile.GetVisualDescendants().OfType<Border>().First(b => b.Name == "Bd");
        Assert.Equal(Color.FromRgb(0xD8, 0xD8, 0xD8), ((ISolidColorBrush)bd.Background!).Color);
        Assert.Equal(Color.FromRgb(0x14, 0x14, 0x14), ((ISolidColorBrush)tile.Foreground!).Color);
        w.Close();
    }

    [AvaloniaFact]
    public void HoverTab_IsReachableUnderMore()
    {
        var vm = new TabletDetailViewModel();
        var view = new TabletDetailView { DataContext = vm };
        var w = new Window { Content = view, Width = 1100, Height = 800 }; w.Show(); Dispatcher.UIThread.RunJobs();
        view.FindControl<RadioButton>("ToolMoreButton")!.IsChecked = true; Dispatcher.UIThread.RunJobs();
        Assert.True(view.FindControl<RadioButton>("HoverTab")!.IsVisible);
        w.Close();
    }

    [AvaloniaFact]
    public void MainViewModel_DisposeTwice_DoesNotThrow()
    {
        var w = new MainWindow(); w.Show(); Dispatcher.UIThread.RunJobs();
        var vm = (MainViewModel)w.DataContext!;
        w.AllowCloseForQuit(); w.Close(); Dispatcher.UIThread.RunJobs();
        vm.Dispose(); vm.Dispose();
    }

    [AvaloniaFact]
    public void Sources_NoCtrlS_NoUpperCaseConverterOnButtons()
    {
        Assert.Contains("(Cmd+S)", System.IO.File.ReadAllText(Path("OpenTabletArtist/Views/SettingsFooterView.axaml")));
        Assert.DoesNotContain("UpperCaseConverter", System.IO.File.ReadAllText(Path("OpenTabletArtist/Themes/Styles.axaml")));
        Assert.Contains("TextWrapping=\"Wrap\"", System.IO.File.ReadAllText(Path("OpenTabletArtist/Views/PresetsView.axaml")));
    }

    private static string Path(string rel)
    {
        var d = System.AppContext.BaseDirectory;
        while (d != null && !System.IO.File.Exists(System.IO.Path.Combine(d, "OpenTabletArtist.slnx"))) d = System.IO.Path.GetDirectoryName(d);
        return System.IO.Path.Combine(d!, rel);
    }
}
