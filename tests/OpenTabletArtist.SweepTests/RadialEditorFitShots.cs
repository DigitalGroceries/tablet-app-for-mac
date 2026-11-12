using System.IO;
using System.Linq;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletArtist.Controls;
using OpenTabletArtist.Helpers;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using Xunit;

namespace OpenTabletArtist.SweepTests;

/// <summary>TASKS #60: pictures of the radial menu editor in the main window at 1100x720 (Zak's size), 900x600 and a large window.
/// Output: $OTA_SHOTS/radial-editor-fit-*.png or /workspace.</summary>
public class RadialEditorFitShots
{
    static string Dir => System.Environment.GetEnvironmentVariable("OTA_SHOTS") ?? "/workspace";

    [AvaloniaTheory]
    [InlineData(1100, 720)]
    [InlineData(900, 600)]
    [InlineData(1500, 1000)]
    [InlineData(760, 420)]
    public void EditorInTheMainWindow(double w, double h)
    {
        Directory.CreateDirectory(Dir);
        _ = new ThemeViewModel();
        var mw = new MainWindow { Width = w, Height = h };
        mw.Show();
        InWindowSheet.MainOverride = () => mw;
        try
        {
            var vm = new RadialMenuEditorViewModel(null, "Pen button 2");
            vm.Size = 100;
            vm.SelectSlice(2);
            _ = InWindowSheet.ShowDialogAsync(new RadialMenuEditorDialog(vm), mw);
            for (int i = 0; i < 3; i++) { Dispatcher.UIThread.RunJobs(); mw.UpdateLayout(); }
            mw.CaptureRenderedFrame()!.Save($"{Dir}/radial-editor-fit-{(int)w}x{(int)h}.png");
        }
        finally { InWindowSheet.MainOverride = null; mw.Close(); }
    }
}
