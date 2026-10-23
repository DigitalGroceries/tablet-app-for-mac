using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using Xunit;

namespace OpenTabletArtist.SweepTests;

public class R18Shots
{
    const string Dir = "/workspace/otaE/R18/shots";
    private static (TabletDetailViewModel, TabletDetailView, Window) Open(string tab, double width, double height = 1100, bool wheel = false)
    {
        _ = new ThemeViewModel();
        var profile = new Profile { Tablet = "Wacom PTK-440", OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)) };
        profile.BindingSettings.TipButton = PenSwitchBinding.MakeAdaptiveBinding(PenSwitchKind.Tip);
        profile.BindingSettings.EraserButton = PenSwitchBinding.MakeAdaptiveBinding(PenSwitchKind.Eraser);
        var settings = new OpenTabletDriver.Desktop.Settings { Profiles = new ProfileCollection { profile } };
        var vm = new TabletDetailViewModel(profile, settings);
        vm.PenButton1Row = new PenSwitchRowViewModel(PenSwitchKind.PenButton, 1, null, false, (_, _, _) => Task.CompletedTask);
        vm.PenButton2Row = new PenSwitchRowViewModel(PenSwitchKind.PenButton, 2, null, false, (_, _, _) => Task.CompletedTask);
        if (wheel)
        {
            ButtonBinding Row(string l) => new(0, AuxBinding.Unbound, isOtherBinding: false, otherLabel: "", canEdit: true, applyBinding: null, label: l);
            var ed = new RingModesEditor(0, RingModeBinding.Defaults, true, true, true, null, null, _ => Task.CompletedTask);
            ed.ShowPublishedMode(2);
            vm.Wheels = new List<WheelEditor> { new WheelEditor(0, "Wheel", false, Row("Clockwise"), Row("Anticlockwise"),
                new List<ButtonBinding> { Row("Centre button") }, 30, 30, 5, null) { RingModes = ed } };
        }
        var view = new TabletDetailView { DataContext = vm };
        var w = new Window { Content = view, Width = width, Height = height }; w.Show();
        Dispatcher.UIThread.RunJobs();
        var t = view.FindControl<RadioButton>(tab)!;
        if (!t.IsVisible) { var f = view.FindControl<RadioButton>("ToolFunctionsButton")!; f.IsChecked = true; f.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); }
        t.IsChecked = true;
        Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        return (vm, view, w);
    }

    static void Crop(Window w, Control c, string path, double pad = 8, int scale = 3)
    {
        var frame = w.CaptureRenderedFrame()!;
        var p = c.TranslatePoint(new Point(0, 0), w)!.Value;
        var r = new PixelRect((int)System.Math.Max(0, p.X - pad), (int)System.Math.Max(0, p.Y - pad), (int)(c.Bounds.Width + 2 * pad), (int)(c.Bounds.Height + 2 * pad));
        var full = $"{path}.full.png"; frame.Save(full);
        System.IO.File.WriteAllText(path + ".rect", $"{r.X} {r.Y} {r.Width} {r.Height} {scale}");
    }

    [AvaloniaFact]
    public void Screenshots()
    {
        System.IO.Directory.CreateDirectory(Dir);
        foreach (var tab in new[] { "PenTab", "EraserTab" })
        {
            var (_, _, w) = Open(tab, 1100);
            w.CaptureRenderedFrame()!.Save($"{Dir}/r18-tablet-{tab.Replace("Tab", "").ToLower()}.png"); w.Close();
        }
        // Tool row, Pen selected and unselected
        {
            var (_, view, w) = Open("PenTab", 1100);
            Crop(w, view.FindControl<Control>("ToolScroll")!, $"{Dir}/r18-toolrow-pen-selected");
            var f = view.FindControl<RadioButton>("ToolFunctionsButton")!; f.IsChecked = true; f.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs(); w.UpdateLayout();
            Crop(w, view.FindControl<Control>("ToolScroll")!, $"{Dir}/r18-toolrow-pen-unselected");
            w.Close();
        }
        foreach (var width in new[] { 730, 840, 1100, 1200 })
        {
            var (_, _, w) = Open("WheelsTab", width, 1000, wheel: true);
            w.CaptureRenderedFrame()!.Save($"{Dir}/r18-touchring-{width}.png");
            foreach (var c in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(w).OfType<Control>().Where(c => c.Name is "BigRingGauge" or "RingCentreButton" or "RingDiagram" or "AboutTouchRing"))
                System.IO.File.AppendAllText($"{Dir}/debug.txt", $"{width} {c.Name} {c.Bounds} vis={c.IsEffectivelyVisible} at={c.TranslatePoint(new Point(0,0), w)}\n");
            w.Close();
        }
    }
}
