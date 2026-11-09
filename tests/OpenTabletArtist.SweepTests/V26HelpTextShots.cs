using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using OpenTabletDriver.Desktop.Binding;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using Xunit;

namespace OpenTabletArtist.SweepTests;

/// <summary>Zak 9 Nov: plain-English About ExpressKeys / Touch ring texts. Screenshots at the default window
/// size (1100 × 720; the page sits in the main window's 24 px side padding). Output folder: $OTA_SHOTS or /workspace.</summary>
public class V26HelpTextShots
{
    static string Dir => System.Environment.GetEnvironmentVariable("OTA_SHOTS") ?? "/workspace";

    private static (TabletDetailView, Window) Open(string tab, double width, double height)
    {
        _ = new ThemeViewModel();
        var profile = new Profile { Tablet = "Wacom PTK-440", OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)) };
        profile.BindingSettings.TipButton = PenSwitchBinding.MakeAdaptiveBinding(PenSwitchKind.Tip);
        profile.BindingSettings.EraserButton = PenSwitchBinding.MakeAdaptiveBinding(PenSwitchKind.Eraser);
        profile.BindingSettings.AuxButtons = new PluginSettingStoreCollection();
        foreach (var k in new[] { "B", "E", "Space", "Control", "Shift", "Alt", "Z", "H" }) profile.BindingSettings.AuxButtons.Add(AuxKeyBinding.MakeKeyBinding(k));
        var settings = new OpenTabletDriver.Desktop.Settings { Profiles = new ProfileCollection { profile } };
        var vm = new TabletDetailViewModel(profile, settings);
        vm.PenButton1Row = new PenSwitchRowViewModel(PenSwitchKind.PenButton, 1, null, false, (_, _, _) => Task.CompletedTask);
        vm.PenButton2Row = new PenSwitchRowViewModel(PenSwitchKind.PenButton, 2, null, false, (_, _, _) => Task.CompletedTask);
        ButtonBinding Row(string l) => new(0, AuxBinding.Unbound, isOtherBinding: false, otherLabel: "", canEdit: true, applyBinding: null, label: l);
        var d = RingModeBinding.Defaults;
        var modes = new List<RingMode> { d[0] with { RotateSensitivity = 50 }, d[1] with { ScrollAmount = 50 }, d[2] with { SizeSensitivity = 50 }, d[3] with { HardnessSensitivity = 50 } };
        var ed = new RingModesEditor(0, modes, true, true, true, null, null, _ => Task.CompletedTask);
        ed.ShowPublishedMode(1);
        vm.Wheels = new List<WheelEditor> { new WheelEditor(0, "Wheel", false, Row("Clockwise"), Row("Anticlockwise"),
            new List<ButtonBinding> { Row("Centre button") }, 30, 30, 5, null) { RingModes = ed } };
        var view = new TabletDetailView { DataContext = vm };
        var w = new Window { Content = new Border { Padding = new Thickness(24, 16, 24, 18), Child = view }, Width = width, Height = height }; w.Show();
        Dispatcher.UIThread.RunJobs();
        var t = view.FindControl<RadioButton>(tab)!;
        if (!t.IsVisible) { var f = view.FindControl<RadioButton>("ToolFunctionsButton")!; f.IsChecked = true; f.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); }
        t.IsChecked = true;
        Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        return (view, w);
    }

    [AvaloniaFact]
    public void Screenshots()
    {
        System.IO.Directory.CreateDirectory(Dir);
        foreach (var (tab, name, h) in new[] { ("ButtonsTab", "about-expresskeys", 720.0), ("WheelsTab", "touchring-text", 720.0), ("WheelsTab", "touchring-text-tall", 1500.0) })
        foreach (var width in new[] { 1100.0, 800.0, 1500.0 })
        {
            var (_, w) = Open(tab, width, h);
            var file = width == 1100 && h == 720 ? $"{Dir}/{name}.png" : $"{Dir}/{name}-{(int)width}x{(int)h}.png";
            w.CaptureRenderedFrame()!.Save(file);
            if (tab == "WheelsTab" && width == 1100 && h == 720)
            {
                // the same default-size window, scrolled down to the mode notes
                var page = w.GetVisualDescendants().OfType<Control>().First(c => c.Name == "WheelsPage");
                var sv = page.GetVisualAncestors().OfType<ScrollViewer>().First();
                sv.Offset = new Vector(0, 140);
                Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                w.CaptureRenderedFrame()!.Save($"{Dir}/{name}-notes.png");
            }
            w.Close();
        }
    }
}
