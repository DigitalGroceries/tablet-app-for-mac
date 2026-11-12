using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletArtist.Controls;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Helpers;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>TASKS #60 (Zak, 12 Nov): in the radial menu editor at his window size (1100x720) the pie was cut off at the bottom
/// (the lowest slice half hidden behind the Menu size / transparency row). The editor is a sheet inside the main window, so its
/// height is the window height minus a margin: the pie now scales down to the free space (with a minimum, below which the
/// area scrolls), and the size / transparency row never overlaps it.</summary>
public class RadialEditorFitTests
{
    private static RadialMenuEditorDialog? _dialog;

    internal static (MainWindow Main, RadialMenuEditorViewModel Vm, RadialMenuPie Pie, Control Footer, ScrollViewer Area) Open(double w, double h, int size = 100)
    {
        _ = new ThemeViewModel();
        var mw = new MainWindow { Width = w, Height = h };
        mw.Show();
        InWindowSheet.MainOverride = () => mw;
        var vm = new RadialMenuEditorViewModel(null, "Pen button 1");
        vm.Size = size;                 // the biggest ring is the worst case
        var dialog = _dialog = new RadialMenuEditorDialog(vm);
        _ = InWindowSheet.ShowDialogAsync(dialog, mw);
        Settle(mw);
        var pie = mw.GetVisualDescendants().OfType<RadialMenuPie>().First(p => p.Interactive);
        var footer = mw.GetVisualDescendants().OfType<Control>().First(c => c.Name == RadialMenuEditorDialog.FooterName);
        var area = mw.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Name == RadialMenuEditorDialog.AreaName);
        return (mw, vm, pie, footer, area);
    }

    internal static void Settle(Window w)
    {
        for (int i = 0; i < 3; i++) { Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); }
    }

    internal static Rect In(Visual v, Visual root) => new Rect(v.TranslatePoint(new Point(0, 0), root) ?? default, v.Bounds.Size);

    internal static void Close(MainWindow mw)
    {
        _dialog?.Close();          // takes the sheet off InWindowSheet's stack, so other tests start clean
        Settle(mw);
        _dialog = null;
        InWindowSheet.MainOverride = null;
        mw.Close();
    }

    [AvaloniaTheory]
    [InlineData(1100, 720)]
    [InlineData(900, 600)]
    [InlineData(1500, 1000)]
    public void ThePie_FitsAboveTheControlsRow_AndInsideTheArea(double w, double h)
    {
        var (mw, _, pie, footer, area) = Open(w, h);
        try
        {
            var pieRect = In(pie, mw);
            var footerRect = In(footer, mw);
            var areaRect = In(area, mw);
            Assert.False(pieRect.Intersects(footerRect), $"pie {pieRect} overlaps the controls row {footerRect} at {w}x{h}");
            Assert.True(pieRect.Bottom <= footerRect.Top, $"pie bottom {pieRect.Bottom} is not above the row top {footerRect.Top}");
            // fully inside the visible area: nothing is cut off, no need to scroll at these sizes
            Assert.True(pieRect.Top >= areaRect.Top - 0.5 && pieRect.Bottom <= areaRect.Bottom + 0.5, $"pie {pieRect} sticks out of the area {areaRect} at {w}x{h}");
            Assert.True(pieRect.Left >= areaRect.Left - 0.5 && pieRect.Right <= areaRect.Right + 0.5, $"pie {pieRect} sticks out sideways of {areaRect} at {w}x{h}");
            Assert.Equal(pie.Bounds.Width, pie.Bounds.Height, 1);
        }
        finally { Close(mw); }
    }

    [AvaloniaFact]
    public void AtAGenerousWindow_ThePieKeepsItsRealSize()
    {
        var (mw, vm, pie, _, _) = Open(1500, 1000, size: 100);
        try { Assert.Equal(vm.PreviewDiameter, pie.Bounds.Width, 1); }
        finally { Close(mw); }
    }

    [AvaloniaFact]
    public void AtAnotherSize_ThePieStillFitsWhileTheSliderMoves()
    {
        var (mw, vm, pie, footer, area) = Open(1100, 720);
        try
        {
            foreach (var s in new[] { 1, 50, 100 })
            {
                vm.Size = s; Settle(mw);
                var p = In(pie, mw);
                Assert.True(p.Bottom <= In(footer, mw).Top, $"size {s}: pie {p} overlaps the row");
                Assert.True(p.Bottom <= In(area, mw).Bottom + 0.5);
            }
        }
        finally { Close(mw); }
    }

    [AvaloniaFact]
    public void InAVerySmallWindow_ThePieStopsShrinking_AndTheAreaScrolls_NeverOverlappingTheRow()
    {
        var (mw, _, pie, footer, area) = Open(760, 420);
        try
        {
            Assert.True(pie.Bounds.Width >= RadialMenuEditorDialog.MinPieDiameter - 0.5, $"pie shrank to {pie.Bounds.Width}");
            Assert.True(area.Extent.Height > area.Viewport.Height + 1, "the area should scroll here");
            // the row stays below the scrolling area
            Assert.True(In(area, mw).Bottom <= In(footer, mw).Top + 0.5);
            // scroll to the end: the whole pie is visible then
            area.ScrollToEnd(); Settle(mw);
            var p = In(pie, mw); var a = In(area, mw);
            Assert.True(p.Bottom <= a.Bottom + 0.5, $"after scrolling, pie {p} is still cut off by {a}");
        }
        finally { Close(mw); }
    }

    [AvaloniaFact]
    public void InASubmenu_WithTheBackButton_ThePieStillFitsAt900x600()
    {
        var (mw, vm, pie, footer, area) = Open(900, 600);
        try
        {
            vm.SelectSlice(1);
            vm.ActionIndex = RadialMenuEditorViewModel.ActSubmenu;
            vm.EditSubmenuCommand.Execute(null);
            Settle(mw);
            Assert.True(vm.InSubmenu);
            var back = mw.GetVisualDescendants().OfType<Button>().First(b => b.Command == vm.BackCommand);
            Assert.True(back.IsVisible);
            var p = In(pie, mw); var b = In(back, mw);
            Assert.False(p.Intersects(b), $"pie {p} overlaps the Back button {b}");
            Assert.True(p.Bottom <= In(area, mw).Bottom + 0.5 && p.Bottom <= In(footer, mw).Top);
        }
        finally { Close(mw); }
    }
}
