using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using Xunit;

namespace OpenTabletArtist.SweepTests;

/// <summary>TASKS #42: pictures of the radial menu on screen (with an empty slice and a submenu), the menu
/// editor and the binding editor's Radial menu tab. Output: $OTA_SHOTS/radial-*.png or /workspace.</summary>
public class RadialMenuShots
{
    static string Dir => Environment.GetEnvironmentVariable("OTA_SHOTS") ?? "/workspace";

    private static void Snap(Window w, string file)
    {
        w.Show();
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        w.CaptureRenderedFrame()!.Save($"{Dir}/{file}");
    }

    [AvaloniaFact]
    public void OnScreenMenu_WithAnEmptySliceAndASubmenu()
    {
        System.IO.Directory.CreateDirectory(Dir);
        _ = new ThemeViewModel();
        var labels = new List<string> { "Undo", "Redo", "Tools", "Paste", "", "Select all", "Zoom out", "Zoom in" };
        var set = new List<bool> { true, true, true, true, false, true, true, true };
        var sub = new List<bool> { false, false, true, false, false, false, false, false };
        var overlay = new RadialMenuOverlay();
        overlay.ShowNowForTest(new RadialMenuOverlay.MenuState("open", 1, 400, 300, 50, 0, labels, set, sub));
        Dispatcher.UIThread.RunJobs();
        var w = overlay.MenuWindow!;
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        w.CaptureRenderedFrame()!.Save($"{Dir}/radial-on-screen.png");
        Assert.Equal(2 * RadialMenuGeometry.OuterRadius(50) + 4, w.Width, 1);
        overlay.Dispose();
    }

    [AvaloniaFact]
    public void MenuEditor()
    {
        System.IO.Directory.CreateDirectory(Dir);
        _ = new ThemeViewModel();
        var vm = new RadialMenuEditorViewModel(null, "Pen button 1");
        vm.SelectSlice(2);
        var w = new RadialMenuEditorDialog(vm);
        Snap(w, "radial-editor.png");
        vm.ActionIndex = RadialMenuEditorViewModel.ActOpen;
        Dispatcher.UIThread.RunJobs();
        Snap(w, "radial-editor-open-app.png");
        w.Close();
    }

    [AvaloniaFact]
    public void BindingEditor_RadialTab()
    {
        System.IO.Directory.CreateDirectory(Dir);
        _ = new ThemeViewModel();
        var vm = new BindingEditorViewModel(AuxBinding.RadialMenu(null), "ExpressKey 1");
        Assert.True(vm.IsRadialTab);
        var w = new BindingEditorDialog(vm);
        Snap(w, "radial-binding-tab.png");
        w.Close();
    }
}
