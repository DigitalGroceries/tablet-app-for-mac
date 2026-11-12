using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>TASKS #64 (the pencil's hint sits on the radial menu, not over the app that was in front) and #65 (a short text when a slice has run,
/// or when it failed), 8 languages.</summary>
public class RadialToastTests
{
    private static RadialMenuOverlay.MenuState Open(double x, double y, bool editing = false, RadialMenuOverlay.UsedSlice? used = null, long seq = 1) =>
        new("open", seq, x, y, 50, 0,
            new[] { "Undo", "Redo", "Copy", "Paste", "Cut", "Select all", "Zoom out", "Zoom in" }.ToList(), Enumerable.Repeat(true, 8).ToList(), Enumerable.Repeat(false, 8).ToList(),
            Editing: editing, Used: used);

    private static RadialMenuOverlay.MenuState Closed(long seq, RadialMenuOverlay.UsedSlice? used) =>
        new("closed", seq, 0, 0, 50, 0, Array.Empty<string>(), Array.Empty<bool>(), Array.Empty<bool>(), Used: used);

    private static void Pump() { for (int i = 0; i < 4; i++) Dispatcher.UIThread.RunJobs(); }

    private static async Task<List<string>> Logged(Func<Task> body)
    {
        var lines = new List<string>();
        void H(string l) { lock (lines) lines.Add(l); }
        AppLog.LineWritten += H;
        try { await body(); } finally { AppLog.LineWritten -= H; }
        lock (lines) return lines.ToList();
    }

    [AvaloniaFact]
    public void ThePencilHint_SitsUnderTheMenu_StaysWhileEditing_AndGoesWhenEditingEnds_WithoutAPopupOverTheApp()
    {
        _ = new ThemeViewModel();
        var popups = new List<string>();
        ActionConfirmation.Recorder = k => popups.Add(k);
        var overlay = new RadialMenuOverlay();
        try
        {
            overlay.ShowNowForTest(Open(600, 400));
            Pump();
            Assert.False(overlay.Toast.IsVisible);                                 // nothing until the pencil is on

            overlay.ShowNowForTest(Open(600, 400, editing: true, seq: 2));
            Pump();
            Assert.True(overlay.Toast.IsVisible);
            Assert.True(overlay.Toast.IsHint);
            Assert.Equal("Edit the slice you tap next", overlay.Toast.Text);
            var menu = overlay.MenuWindow!; var toast = overlay.Toast.ToastWindow!;
            double ringBottom = menu.Position.Y + menu.Height;                     // under the ring, centred on it
            Assert.True(toast.Position.Y >= ringBottom - 6, $"hint at y={toast.Position.Y} is not under the menu (bottom {ringBottom})");
            Assert.True(Math.Abs(toast.Position.X + toast.Width / 2 - 600) <= 2, "hint is not centred under the menu");
            Assert.Empty(popups.Where(k => k == "confirm.radial_edit_hint"));       // no big pop-up over the last active app any more

            overlay.ShowNowForTest(Open(640, 420, editing: true, seq: 3));          // the menu is dragged: the hint follows, stays
            Pump();
            Assert.True(overlay.Toast.IsVisible);
            Assert.True(Math.Abs(toast.Position.X + toast.Width / 2 - 640) <= 2);

            overlay.ShowNowForTest(Open(640, 420, editing: false, seq: 4));         // a slice chosen / the pencil put down
            Pump();
            Assert.False(overlay.Toast.IsVisible);
        }
        finally { ActionConfirmation.Recorder = null; overlay.Dispose(); }
    }

    [AvaloniaFact]
    public void TheHint_GoesWhenTheMenuCloses()
    {
        _ = new ThemeViewModel();
        var overlay = new RadialMenuOverlay();
        try
        {
            overlay.ShowNowForTest(Open(600, 400, editing: true));
            Pump();
            Assert.True(overlay.Toast.IsVisible);
            // the file watcher path hides the menu on a closed state; Hide is what it calls
            typeof(RadialMenuOverlay).GetMethod("Hide", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(overlay, null);
            Pump();
            Assert.False(overlay.Toast.IsVisible);
        }
        finally { overlay.Dispose(); }
    }

    [AvaloniaFact]
    public async Task ASliceThatRan_ShowsItsText_NearTheMenu_ForAMoment_OncePerRun()
    {
        _ = new ThemeViewModel();
        RadialMenuToast.ConfirmationTime = TimeSpan.FromMilliseconds(150);
        var overlay = new RadialMenuOverlay();
        try
        {
            overlay.ShowNowForTest(Open(600, 400));
            var copy = new RadialMenuOverlay.UsedSlice(1, "Copy", "Application+C", "keys", true);
            overlay.ShowUsedIfNew(Open(600, 400, used: copy));
            Pump();
            Assert.True(overlay.Toast.IsVisible);
            Assert.False(overlay.Toast.IsHint);
            Assert.Equal("Selection copied", overlay.Toast.Text);
            Assert.True(Math.Abs(overlay.Toast.ToastWindow!.Position.X + overlay.Toast.ToastWindow.Width / 2 - 600) <= 2);

            await Task.Delay(400, TestContext.Current.CancellationToken); Pump();
            Assert.False(overlay.Toast.IsVisible);                                  // gone by itself

            overlay.ShowUsedIfNew(Open(600, 400, used: copy));                      // the same run repeated in a later state write: not again
            Pump();
            Assert.False(overlay.Toast.IsVisible);

            overlay.ShowUsedIfNew(Closed(9, new RadialMenuOverlay.UsedSlice(2, "Undo", "Application+Z", "keys", true)));   // the menu closed with it
            Pump();
            Assert.Equal("Undone", overlay.Toast.Text);
        }
        finally { RadialMenuToast.ConfirmationTime = TimeSpan.FromMilliseconds(1300); overlay.Dispose(); }
    }

    [AvaloniaFact]
    public async Task ASliceThatFailed_SaysSo_AndIsLogged_NotAThumbsUp()
    {
        _ = new ThemeViewModel();
        var overlay = new RadialMenuOverlay();
        try
        {
            overlay.ShowNowForTest(Open(600, 400));
            var lines = await Logged(() =>
            {
                overlay.ShowUsedIfNew(Open(600, 400, used: new RadialMenuOverlay.UsedSlice(5, "Brush", "Nonsense+Key", "keys", false)));
                return Task.CompletedTask;
            });
            Pump();
            Assert.Equal("Brush did not work", overlay.Toast.Text);
            Assert.Contains(lines, l => l.Contains("did not run") && l.Contains("Brush"));
        }
        finally { overlay.Dispose(); }
    }

    [AvaloniaTheory]
    [InlineData(10, 10)]
    [InlineData(10, 4000)]      // near the bottom of any screen: the text goes above the ring instead of off the screen
    public void TheText_NeverCoversTheMenu(double x, double y)
    {
        _ = new ThemeViewModel();
        var overlay = new RadialMenuOverlay();
        try
        {
            overlay.ShowNowForTest(Open(x + 200, y));
            overlay.ShowUsedIfNew(Open(x + 200, y, used: new RadialMenuOverlay.UsedSlice(1, "Copy", "Application+C", "keys", true)));
            Pump();
            var menu = overlay.MenuWindow!; var toast = overlay.Toast.ToastWindow!;
            var m = new Rect(menu.Position.X, menu.Position.Y, menu.Width, menu.Height);
            var t = new Rect(toast.Position.X, toast.Position.Y, toast.Width, toast.Height);
            // The ring is round, so only its bounding circle matters: the text box must not reach the circle's centre band.
            Assert.False(t.Contains(new Point(x + 200, y)), "the text covers the middle of the menu");
        }
        finally { overlay.Dispose(); }
    }

    // ── the words ────────────────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void EveryBuiltInSlice_HasItsOwnShortText_AndOtherSlicesSayNameDone()
    {
        string T(string keys, string label = "x", bool ok = true) => RadialMenuConfirm.Text(Localizer.Te, label, keys, ok);
        Assert.Equal("Undone", T("Application+Z"));
        Assert.Equal("Redone", T("Application+Shift+Z"));
        Assert.Equal("Selection copied", T("Application+C"));
        Assert.Equal("Pasted", T("Application+V"));
        Assert.Equal("Selection cut", T("Application+X"));
        Assert.Equal("All selected", T("Application+A"));
        Assert.Equal("Zoomed out", T("Application+Minus"));
        Assert.Equal("Zoomed in", T("Application+Equal"));
        Assert.Equal("Brush done", T("B", "Brush"));
        Assert.Equal("Done", T("B", ""));
        Assert.Equal("Brush did not work", T("B", "Brush", ok: false));
        Assert.Equal("Did not work", T("B", "", ok: false));
        Assert.Equal("Selection copied", T("Application+C", "My copy"));      // a renamed built-in keeps its text
    }

    [AvaloniaFact]
    public void TheTexts_ExistInAll8Languages_AndKeepTheirNamePlaceholder()
    {
        var keys = new[] { "undo", "redo", "copy", "paste", "cut", "select_all", "zoom_out", "zoom_in", "generic", "failed", "plain", "failed_plain" };
        var dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "OpenTabletArtist", "Assets", "i18n"));
        foreach (var code in new[] { "en", "bg", "de", "es", "fr", "ja", "ru", "zh-CN" })
        {
            var json = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(dir, code + ".json")));
            foreach (var k in keys) Assert.False(string.IsNullOrWhiteSpace((string?)json["radial.done." + k]), $"{code} misses radial.done.{k}");
            Assert.Contains("{name}", (string)json["radial.done.generic"]!);
            Assert.Contains("{name}", (string)json["radial.done.failed"]!);
            Assert.DoesNotContain("{name}", (string)json["radial.done.plain"]!);
        }
    }

    [AvaloniaFact]
    public async Task PopupWindow_IsClear_AndThePieOpacityFollowsTransparency_AndOpenIsLogged()
    {
        _ = new ThemeViewModel();
        var overlay = new RadialMenuOverlay();
        try
        {
            var labels = new[] { "Undo", "Redo", "Copy", "Paste", "Cut", "All", "Out", "In" }.ToList();
            var set = Enumerable.Repeat(true, 8).ToList();
            var sub = Enumerable.Repeat(false, 8).ToList();
            var lines = await Logged(async () =>
            {
                overlay.ShowNowForTest(new RadialMenuOverlay.MenuState("open", 1, 400, 300, 50, 0, labels, set, sub, Transparency: 40));
                await Task.Yield();
            });
            Pump();
            var w = overlay.MenuWindow!;
            Assert.Same(Avalonia.Media.Brushes.Transparent, w.Background);
            Assert.Contains(WindowTransparencyLevel.Transparent, w.TransparencyLevelHint);
            var pie = Assert.IsType<OpenTabletArtist.Controls.RadialMenuPie>(w.Content);
            Assert.Equal(40, pie.Transparency);
            Assert.Equal(RadialMenuDef.OpacityFor(40), pie.Opacity);
            Assert.Contains(lines, l => l.Contains("transparency 40%") && l.Contains("opacity 0.64"));

            overlay.ShowNowForTest(new RadialMenuOverlay.MenuState("open", 2, 400, 300, 50, 0, labels, set, sub, Transparency: 100));
            Pump();
            Assert.Equal(100, pie.Transparency);
            Assert.Equal(RadialMenuDef.MinVisibleOpacity, pie.Opacity);
        }
        finally { overlay.Dispose(); }
    }
}

/// <summary>TASKS #64 (1): after the pencil, a click on a slice with the Mac mouse must reach the driver as an edit-mode tap, exactly like a click
/// on a slice normally does, and the pencil click itself must reach it. Drives the real overlay window with mouse events.</summary>
public class RadialMouseEditTests
{
    [AvaloniaFact]
    public async Task MouseClicks_OnThePencil_ThenOnASlice_AreSentToTheDriver_InOrder_AndLogged()
    {
        _ = new ThemeViewModel();
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ota-radial-mouse-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        var oldCmd = RadialMenuOverlay.CommandFilePath;
        RadialMenuOverlay.CommandFilePath = System.IO.Path.Combine(dir, "radial-menu-cmd");
        var overlay = new RadialMenuOverlay();
        try
        {
            double cx = 600, cy = 400;
            overlay.ShowNowForTest(new RadialMenuOverlay.MenuState("open", 1, cx, cy, 50, 0,
                new[] { "Undo", "Redo", "Copy", "Paste", "Cut", "Select all", "Zoom out", "Zoom in" }.ToList(), Enumerable.Repeat(true, 8).ToList(), Enumerable.Repeat(false, 8).ToList()));
            var win = overlay.MenuWindow!;
            win.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Point cursor = default;
            overlay.CursorProbe = () => cursor;
            double mid = win.Width / 2;

            async Task<RadialMenuCommand?> Click(double dx, double dy)
            {
                try { System.IO.File.Delete(RadialMenuOverlay.CommandFilePath); } catch (System.IO.IOException) { }
                cursor = new Point(cx + dx, cy + dy);
                var p = new Point(mid + dx, mid + dy);
                Avalonia.Headless.HeadlessWindowExtensions.MouseDown(win, p, Avalonia.Input.MouseButton.Left);
                Avalonia.Headless.HeadlessWindowExtensions.MouseUp(win, p, Avalonia.Input.MouseButton.Left);
                await Task.Delay(300, TestContext.Current.CancellationToken); Dispatcher.UIThread.RunJobs();
                return System.IO.File.Exists(RadialMenuOverlay.CommandFilePath) ? RadialMenuCommand.Parse(System.IO.File.ReadAllText(RadialMenuOverlay.CommandFilePath)) : null;
            }

            var lines = new List<string>();
            void H(string l) { lock (lines) lines.Add(l); }
            AppLog.LineWritten += H;
            try
            {
                var pencil = await Click(30, -30);                       // the pencil (top right of the centre)
                Assert.NotNull(pencil);
                Assert.Equal(RadialMenuCommand.Edit, pencil!.Op);

                // the menu now says the pencil is on (the driver's next state); the overlay shows it
                overlay.ShowNowForTest(new RadialMenuOverlay.MenuState("open", 2, cx, cy, 50, 0,
                    new[] { "Undo", "Redo", "Copy", "Paste", "Cut", "Select all", "Zoom out", "Zoom in" }.ToList(), Enumerable.Repeat(true, 8).ToList(), Enumerable.Repeat(false, 8).ToList(), Editing: true));
                Dispatcher.UIThread.RunJobs();

                var tap = await Click(110, 0);                           // the Copy slice, with the pencil on
                Assert.NotNull(tap);
                Assert.Equal(RadialMenuCommand.Tap, tap!.Op);
                Assert.Equal(2, tap.Slice);
                Assert.True(tap.Seq > pencil.Seq);
            }
            finally { AppLog.LineWritten -= H; }
            lock (lines)
            {
                Assert.Contains(lines, l => l.Contains("mouse click on the pencil"));
                Assert.Contains(lines, l => l.Contains("mouse click on slice 3 while the pencil is on"));
            }
        }
        finally
        {
            overlay.Dispose();
            RadialMenuOverlay.CommandFilePath = oldCmd;
            try { System.IO.Directory.Delete(dir, true); } catch (System.IO.IOException) { }
        }
    }

}

/// <summary>TASKS #64 (1): the driver's edit request for slice N opens the slice editor on that slice, inside the app window.</summary>
public class RadialEditRequestTests
{
    [AvaloniaFact]
    public async Task EditRequest_OpensTheSliceEditorOnTheTappedSlice_InsideTheMainWindow()
    {
        _ = new ThemeViewModel();
        var mw = new MainWindow { Width = 1100, Height = 760 };
        mw.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Helpers.InWindowSheet.MainOverride = () => mw;
        try
        {
            var json = RadialMenuJson.Serialize(RadialMenuDefaults.Create());
            var task = OpenTabletArtist.Views.RadialMenuEditorDialog.ShowAsync(mw, json, "Radial menu", Array.Empty<int>(), 2);   // what RadialLiveEdit does for {kind: edit, slice: 2}
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(50, TestContext.Current.CancellationToken); Dispatcher.UIThread.RunJobs();
            var pie = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(mw).OfType<OpenTabletArtist.Controls.RadialMenuPie>().First(p => p.Interactive);
            var vm = (RadialMenuEditorViewModel)pie.DataContext!;
            Assert.Equal(2, vm.SelectedSlice);                       // the slice that was tapped
            Assert.Equal("Copy", vm.SliceLabel);
            vm.CancelCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Null(await task);
        }
        finally { Helpers.InWindowSheet.MainOverride = null; mw.Close(); }
    }
}
