using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Newtonsoft.Json.Linq;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OtdInterop;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>Build R14 (the user's mock-up): live pressure %, movement + pen input panel on Tablet › Pen, dark value
/// boxes on the button cards, no lone Default / footnote, no PEN page, one home for each overlap.</summary>
public class BuildR14Tests
{
    private sealed class Feed : IDaemonDebugSession
    {
        public event Action<JObject>? DeviceReport;
        public Task SetTabletDebugAsync(bool enabled) => Task.CompletedTask;
        public void Raw(JObject data) => DeviceReport?.Invoke(new JObject
        {
            ["Tablet"] = new JObject { ["Properties"] = new JObject { ["Name"] = "Wacom PTK-440", ["Specifications"] = new JObject
            {
                ["Digitizer"] = new JObject { ["Width"] = 152.0, ["Height"] = 95.0, ["MaxX"] = 15200, ["MaxY"] = 9500 },
                ["Pen"] = new JObject { ["MaxPressure"] = 1000 },
            } } },
            ["Data"] = data,
        });
        public void Pen(double p) => Raw(new JObject { ["Position"] = new JObject { ["X"] = 7600, ["Y"] = 4750 }, ["Pressure"] = p });
    }

    private static (Feed, TabletDetailViewModel, TabletDetailView, Window) Open(string tab)
    {
        _ = new ThemeViewModel();
        var feed = new Feed();
        var profile = new Profile { Tablet = "Wacom PTK-440", OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)) }; TestPens.Arm(profile);
        var settings = new OpenTabletDriver.Desktop.Settings { Profiles = new ProfileCollection { profile } };
        var vm = new TabletDetailViewModel(profile, settings, penInput: feed);
        var view = new TabletDetailView { DataContext = vm };
        var w = new Window { Content = view, Width = 1100, Height = 1000 }; w.Show();
        view.FindControl<RadioButton>(tab)!.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        return (feed, vm, view, w);
    }

    private static TextBlock VisiblePercent(TabletDetailView v)
    {
        var label = v.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Current pressure" && t.IsEffectivelyVisible);
        return ((Grid)label.Parent!).Children.OfType<TextBlock>().Single(t => t != label);
    }

    [AvaloniaTheory]
    [InlineData("PenTab")]
    [InlineData("EraserTab")]
    public void CurrentPressure_ShowsLivePercent_ForTipAndEraser(string tab)
    {
        var (feed, vm, view, w) = Open(tab);
        Assert.Equal("0%", VisiblePercent(view).Text);
        feed.Raw(new JObject { ["Tool"] = 0 }); feed.Pen(620); Dispatcher.UIThread.RunJobs();
        Assert.Equal(tab == "PenTab" ? "62%" : "0%", VisiblePercent(view).Text);   // R15: tip reports move only the tip bar
        Assert.Equal(tab == "PenTab" ? "raw 0.6200" : "—", ToolTip.GetTip(VisiblePercent(view)));
        feed.Raw(new JObject { ["Tool"] = 1 }); feed.Pen(250); Dispatcher.UIThread.RunJobs();
        Assert.Equal(tab == "EraserTab" ? "25%" : "0%", VisiblePercent(view).Text);
        Assert.Equal("Eraser", vm.LiveEndText);
        feed.Pen(0); Dispatcher.UIThread.RunJobs();
        Assert.Equal("0%", VisiblePercent(view).Text);
        w.Close();
    }

    [AvaloniaFact]
    public void TabletPen_HasBasicsPanel_InsideTheFrame_NoDefaultNoFootnote()
    {
        var (_, _, view, w) = Open("PenTab");
        var box = view.FindControl<Border>("PenBasicsBox")!;
        Assert.True(box.IsEffectivelyVisible);
        var frame = box.GetVisualAncestors().OfType<Border>().First(b => b.Classes.Contains("wacomPane"));
        var tl = box.TranslatePoint(new Avalonia.Point(0, 0), frame)!.Value;
        Assert.True(tl.X >= 0 && tl.X + box.Bounds.Width <= frame.Bounds.Width + 0.5, $"panel {tl.X}+{box.Bounds.Width} in {frame.Bounds.Width}");
        var heads = box.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("basicsHeading") && t.IsEffectivelyVisible).ToList();
        Assert.Equal(new[] { "Movement", "Pen input" }, heads.Select(h => h.Text).ToArray());   // Pressing: Windows only
        Assert.All(heads, h => Assert.Equal(17, h.FontSize));
        var texts = view.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "").ToList();
        Assert.DoesNotContain(texts, t => t.StartsWith("More pen options"));
        Assert.DoesNotContain(view.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && b.Content as string == "Default" && b.FindAncestorOfType<PenSwitchCard>() is null);
        view.FindControl<RadioButton>("EraserTab")!.IsChecked = true; Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(view.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && b.Content as string == "Default" && b.FindAncestorOfType<PenSwitchCard>() is null);
        // R18: the user wants full Eraser parity, so the Eraser tab now mirrors the (shared) feel slider.
        w.Close();
    }

    [AvaloniaFact]
    public void ButtonValueBoxes_AreDark_Rounded_EqualWidth_Aligned()
    {
        var row1 = new PenSwitchRowViewModel(OpenTabletArtist.Domain.PenSwitchKind.PenButton, 1, null, false, (_, _, _) => Task.CompletedTask);
        var row2 = new PenSwitchRowViewModel(OpenTabletArtist.Domain.PenSwitchKind.PenButton, 2, null, false, (_, _, _) => Task.CompletedTask);
        var c1 = new PenSwitchCard { DataContext = row1 }; var c2 = new PenSwitchCard { DataContext = row2 };
        var w = new Window { Content = new StackPanel { Children = { c2, c1 } }, Width = 600, Height = 400 }; w.Show(); Dispatcher.UIThread.RunJobs();
        var boxes = new[] { c1, c2 }.Select(c => c.FindControl<Border>("ValueBox")!).ToList();
        foreach (var b in boxes)
        {
            var fill = ((ISolidColorBrush)b.Background!).Color;
            Assert.True(fill.R <= 0x20 && fill.R == fill.G && fill.G == fill.B, $"fill {fill}");
            Assert.Equal(6, b.CornerRadius.TopLeft);
            var fg = ((ISolidColorBrush)((TextBlock)b.Child!).Foreground!).Color;
            Assert.True(fg.R <= 0xE4 && fg.R >= 0xC0, $"text {fg}");
        }
        Assert.Equal(boxes[0].Bounds.Width, boxes[1].Bounds.Width);
        Assert.Equal(boxes[0].TranslatePoint(new Avalonia.Point(0, 0), w)!.Value.X, boxes[1].TranslatePoint(new Avalonia.Point(0, 0), w)!.Value.X);
        w.Close();
    }

    [AvaloniaTheory]
    [InlineData(TabletDetailTab.PenBehavior)]
    [InlineData(TabletDetailTab.PenInputs)]
    [InlineData(TabletDetailTab.PenTilt)]
    public void PenDeepLinks_LandOnTabletPen(TabletDetailTab tab)
    {
        _ = new ThemeViewModel();
        var vm = new TabletDetailViewModel();
        vm.RequestTab(tab);
        var view = new TabletDetailView { DataContext = vm };
        var w = new Window { Content = view, Width = 1100, Height = 900 }; w.Show(); Dispatcher.UIThread.RunJobs();
        Assert.True(view.FindControl<RadioButton>("PenTab")!.IsChecked);
        Assert.True(view.FindControl<Border>("PenBasicsBox")!.IsEffectivelyVisible);
        w.Close();
    }

    [AvaloniaFact]
    public void NoPenPage_AndOneHomeForTheOldOverlaps()
    {
        var root = Path("OpenTabletArtist");
        Assert.False(System.IO.File.Exists(System.IO.Path.Combine(root, "Views/PenDetailView.axaml")));
        Assert.False(System.IO.File.Exists(System.IO.Path.Combine(root, "ViewModels/PenPageViewModel.cs")));
        var main = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "ViewModels/MainViewModel.cs"));
        Assert.DoesNotContain("NavLeafViewModel(\"PEN\"", main);
        var tablet = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "Views/TabletDetailView.axaml"));
        Assert.DoesNotContain("Using a custom tablet config", tablet);      // home: the Home health card
        Assert.DoesNotContain("ActiveAreaFacts", tablet);                   // home: Mapping
        // R18: Eraser Feel is back as a mirror of the shared curve (Eraser parity).
    }

    private static string Path(string rel)
    {
        var d = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "OpenTabletArtist"))) d = d.Parent;
        return System.IO.Path.Combine(d!.FullName, rel);
    }
}
