using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using Xunit;

namespace OpenTabletArtist.SweepTests;

/// <summary>TASKS #24: binding editor with Move pen to zoom selected and the Speed slider visible.
/// Output: $OTA_SHOTS/pen-zoom.png or /workspace/pen-zoom.png.</summary>
public class PenZoomShots
{
    static string Dir => System.Environment.GetEnvironmentVariable("OTA_SHOTS") ?? "/workspace";

    [AvaloniaFact]
    public void BindingEditor_PenZoomWithSpeedSlider()
    {
        System.IO.Directory.CreateDirectory(Dir);
        _ = new ThemeViewModel();
        var initial = new AuxBinding(AuxKind.Scroll, AuxCombo.Unbound, AuxKeyBinding.None,
            PenZoomBinding.ZoomValue, 50);
        var vm = new BindingEditorViewModel(initial, "Pen button 1");
        Assert.True(vm.ShowGestureSpeed);
        Assert.Equal(PenZoomBinding.ZoomValue, vm.SelectedScroll);
        var w = new BindingEditorDialog(vm);
        w.Show();
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        w.CaptureRenderedFrame()!.Save($"{Dir}/pen-zoom.png");
        w.Close();
    }
}
