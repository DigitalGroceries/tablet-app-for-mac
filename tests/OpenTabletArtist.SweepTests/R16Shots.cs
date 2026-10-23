using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using Xunit;

namespace OpenTabletArtist.SweepTests;

/// <summary>Build R16 (the user's mock-up): dark inset Movement/Pen input frame, pen-button cards on the Eraser tab
/// (the same shared rows), flipped pen drawing with upright badges, redrawn cone + nib.</summary>
public class R16Shots
{
    private static (TabletDetailViewModel, TabletDetailView, Window) Open(string tab)
    {
        _ = new ThemeViewModel();
        var profile = new Profile { Tablet = "Wacom PTK-440", OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)) };
        var settings = new OpenTabletDriver.Desktop.Settings { Profiles = new ProfileCollection { profile } };
        var vm = new TabletDetailViewModel(profile, settings);
        vm.PenButton1Row = new PenSwitchRowViewModel(PenSwitchKind.PenButton, 1, null, false, (_, _, _) => Task.CompletedTask);
        vm.PenButton2Row = new PenSwitchRowViewModel(PenSwitchKind.PenButton, 2, null, false, (_, _, _) => Task.CompletedTask);
        var view = new TabletDetailView { DataContext = vm };
        var w = new Window { Content = view, Width = 1100, Height = 1100 }; w.Show();
        view.FindControl<RadioButton>(tab)!.IsChecked = true;
        Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        return (vm, view, w);
    }

    [AvaloniaFact]
    public void Screenshots()
    {
        System.IO.Directory.CreateDirectory("/workspace/otaE/R16/shots");
        foreach (var tab in new[] { "PenTab", "EraserTab" })
        {
            var (_, view, w) = Open(tab);
            w.CaptureRenderedFrame()!.Save($"/workspace/otaE/R16/shots/r16-tablet-{tab.Replace("Tab", "").ToLower()}.png");
            w.Close();
        }
        var pd = new PenDiagram { Width = 64, Height = 360 };
        var pw = new Window { Content = new Border { Background = new SolidColorBrush(Color.Parse("#3A3A3A")), Child = pd }, Width = 64, Height = 360 };
        pw.Show(); Dispatcher.UIThread.RunJobs();
        pw.CaptureRenderedFrame()!.Save("/workspace/otaE/R16/shots/r16-pen-diagram.png"); pw.Close();
    }
}
