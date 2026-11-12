using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using OpenTabletArtist.Controls;
using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>TASKS #53: Scribble's Undo and Redo (what the radial menu's top two slices send as ⌘Z and ⌘⇧Z) act on strokes.</summary>
public class ScribbleUndoTests
{
    private static PenTestCanvas Shown(bool undo = true)
    {
        var canvas = new PenTestCanvas { IgnorePointer = true, EnableUndo = undo };
        new Window { Width = 400, Height = 300, Content = canvas }.Show();
        Dispatcher.UIThread.RunJobs();
        return canvas;
    }

    private static PenSample At(double x, double y, double pressure = 1) =>
        new(x, y, 0, 0, pressure, 0, 0, 0, IsDown: pressure > 0);

    /// <summary>A short stroke, then the pen lifts.</summary>
    private static void Stroke(PenTestCanvas c, double y)
    {
        c.AddSample(At(0.2, y));
        c.AddSample(At(0.5, y));
        c.AddSample(At(0.8, y));
        c.AddSample(At(0.8, y, pressure: 0));
    }

    private static int InkPixels(PenTestCanvas canvas)
    {
        var (bgra, _, _) = canvas.Snapshot()!.Value;
        var n = 0;
        for (var i = 0; i + 3 < bgra.Length; i += 4)
            if (bgra[i] < 50 && bgra[i + 1] < 50 && bgra[i + 2] < 50) n++;   // ink is #222222
        return n;
    }

    [AvaloniaFact]
    public void Undo_TakesBackTheLastStroke_Only()
    {
        var c = Shown();
        Stroke(c, 0.3);
        var one = InkPixels(c);
        Stroke(c, 0.7);
        var two = InkPixels(c);
        Assert.True(two > one && one > 0);

        Assert.True(c.Undo());
        Assert.Equal(one, InkPixels(c));
        Assert.True(c.Undo());
        Assert.Equal(0, InkPixels(c));
        Assert.False(c.Undo());
    }

    [AvaloniaFact]
    public void Redo_PutsTheStrokeBack_Exactly()
    {
        var c = Shown();
        Stroke(c, 0.3);
        Stroke(c, 0.7);
        var two = InkPixels(c);
        c.Undo();
        c.Undo();

        Assert.True(c.Redo());
        Assert.True(c.Redo());
        Assert.Equal(two, InkPixels(c));
        Assert.False(c.Redo());
    }

    [AvaloniaFact]
    public void ANewStroke_AfterAnUndo_EndsTheRedo()
    {
        var c = Shown();
        Stroke(c, 0.3);
        c.Undo();
        Stroke(c, 0.6);
        Assert.False(c.CanRedo);
        Assert.False(c.Redo());
    }

    [AvaloniaFact]
    public void Clear_CanBeUndone()
    {
        var c = Shown();
        Stroke(c, 0.4);
        var ink = InkPixels(c);
        c.Clear();
        Assert.Equal(0, InkPixels(c));
        Assert.True(c.Undo());
        Assert.Equal(ink, InkPixels(c));
    }

    [AvaloniaFact]
    public void HoveringAndAPenUp_AreNotSteps()
    {
        var c = Shown();
        c.AddSample(At(0.5, 0.5, pressure: 0));
        c.AddSample(At(0.6, 0.5, pressure: 0));
        Assert.False(c.CanUndo);
    }

    [AvaloniaFact]
    public void OneStroke_IsOneStep_NotOnePerSample()
    {
        var c = Shown();
        for (var i = 0; i < 20; i++) c.AddSample(At(0.1 + i * 0.03, 0.5));
        c.AddSample(At(0.7, 0.5, pressure: 0));
        Assert.True(c.Undo());
        Assert.Equal(0, InkPixels(c));
        Assert.False(c.CanUndo);
    }

    [AvaloniaFact]
    public void WithUndoOff_NothingIsKept()
    {
        var c = Shown(undo: false);
        Stroke(c, 0.4);
        Assert.False(c.CanUndo);
        Assert.False(c.Undo());
        Assert.True(InkPixels(c) > 0);
    }

    [AvaloniaFact]
    public void Undo_AlsoTakesBackTheRedDots()
    {
        var c = Shown();
        c.ShowReportDots = true;
        Stroke(c, 0.4);
        c.Undo();
        var (bgra, _, _) = c.Snapshot()!.Value;
        var red = 0;
        for (var i = 0; i + 3 < bgra.Length; i += 4)
            if (bgra[i + 2] > 200 && bgra[i + 1] < 60 && bgra[i] < 60) red++;
        Assert.Equal(0, red);
    }

    [AvaloniaFact]
    public void TheScribblePage_HandlesCommandZ_AndCommandShiftZ_AsUndoAndRedo()
    {
        var view = new OpenTabletArtist.Views.TestView();
        var window = new Window { Width = 900, Height = 700, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var canvas = (PenTestCanvas)view.FindControl<PenTestCanvas>("PaintCanvas")!;
        Assert.True(canvas.EnableUndo);

        Stroke(canvas, 0.4);
        var ink = InkPixels(canvas);
        Assert.True(ink > 0);

        var undo = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Z, KeyModifiers = KeyModifiers.Meta };
        // the page listens on the whole window, so the key is seen wherever the focus is
        window.RaiseEvent(undo);
        Assert.True(undo.Handled || !OperatingSystem.IsMacOS());   // ⌘ is the main key only on a Mac
        if (!OperatingSystem.IsMacOS()) return;
        Assert.Equal(0, InkPixels(canvas));

        var redo = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Z, KeyModifiers = KeyModifiers.Meta | KeyModifiers.Shift };
        window.RaiseEvent(redo);
        Assert.True(redo.Handled);
        Assert.Equal(ink, InkPixels(canvas));
    }

    [AvaloniaFact]
    public void ThePage_PerformsUndoAndRedo_AndRecordsWhatItSaid()
    {
        var view = new OpenTabletArtist.Views.TestView();
        new Window { Width = 900, Height = 700, Content = view }.Show();
        Dispatcher.UIThread.RunJobs();
        var canvas = view.FindControl<PenTestCanvas>("PaintCanvas")!;
        var said = new List<string>();
        OpenTabletArtist.Services.ActionConfirmation.Recorder = said.Add;
        try
        {
            view.Perform(ScribbleShortcut.Undo);                      // nothing drawn yet
            Stroke(canvas, 0.4);
            view.Perform(ScribbleShortcut.Undo);
            Assert.Equal(0, InkPixels(canvas));
            view.Perform(ScribbleShortcut.Redo);
            Assert.True(InkPixels(canvas) > 0);
            view.Perform(ScribbleShortcut.Redo);                      // nothing more to redo
            view.Perform(ScribbleShortcut.Paste);
            view.Perform(ScribbleShortcut.Cut);
            view.Perform(ScribbleShortcut.SelectAll);
            view.Perform(ScribbleShortcut.ZoomIn);
            view.Perform(ScribbleShortcut.ZoomOut);
        }
        finally { OpenTabletArtist.Services.ActionConfirmation.Recorder = null; }

        Assert.Equal(new[]
        {
            "confirm.scribble_nothing_undo", "confirm.scribble_undone", "confirm.scribble_redone", "confirm.scribble_nothing_redo",
            "confirm.scribble_paste", "confirm.scribble_cut", "confirm.scribble_select_all", "confirm.scribble_zoom", "confirm.scribble_zoom",
        }, said);
    }
}
