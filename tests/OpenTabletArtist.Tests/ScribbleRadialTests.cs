using System.Collections.Generic;
using Avalonia.Input;
using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>TASKS #53: the undo / redo list behind Scribble's Undo and Redo, and which key shortcuts mean what there.</summary>
public class UndoHistoryTests
{
    private sealed class State { public string Name = ""; public long Bytes = 1; public bool Discarded; }

    private static State S(string name, long bytes = 1) => new() { Name = name, Bytes = bytes };

    private static UndoHistory<State> History(int steps = 10, long bytes = 1000) =>
        new(steps, bytes, s => s.Bytes, s => s.Discarded = true);

    [Fact]
    public void NothingToUndoOrRedo_AtTheStart()
    {
        var h = History();
        Assert.False(h.CanUndo);
        Assert.False(h.CanRedo);
        Assert.Null(h.Undo(S("now")));
        Assert.Null(h.Redo(S("now")));
    }

    [Fact]
    public void Undo_ReturnsTheStateBeforeTheStroke_AndRedoComesBack()
    {
        var h = History();
        var empty = S("empty");
        h.Record(empty);                       // stroke 1 is about to be drawn
        var one = S("one");
        h.Record(one);                         // stroke 2 is about to be drawn

        var two = S("two");                    // the canvas after stroke 2
        Assert.Same(one, h.Undo(two));
        Assert.True(h.CanRedo);

        var back = S("after undo");            // the canvas now (= "one")
        Assert.Same(two, h.Redo(back));
        Assert.False(h.CanRedo);
        Assert.True(h.CanUndo);
    }

    [Fact]
    public void UndoAll_ThenRedoAll_GoesThroughEveryStep()
    {
        var h = History();
        var s0 = S("0"); var s1 = S("1"); var s2 = S("2");
        h.Record(s0); h.Record(s1);            // two strokes drawn; the canvas is now s2
        var cur = s2;
        var u1 = h.Undo(cur)!; Assert.Same(s1, u1);
        var u2 = h.Undo(u1)!; Assert.Same(s0, u2);
        Assert.Null(h.Undo(u2));
        var r1 = h.Redo(u2)!; Assert.Same(u1, r1);
        var r2 = h.Redo(r1)!; Assert.Same(cur, r2);
        Assert.Null(h.Redo(r2));
    }

    [Fact]
    public void ANewStroke_ForgetsWhatCouldBeRedone()
    {
        var h = History();
        h.Record(S("a"));
        var undone = S("current");
        h.Undo(undone);
        Assert.True(h.CanRedo);
        h.Record(S("b"));                      // drew something new
        Assert.False(h.CanRedo);
        Assert.True(undone.Discarded);         // its memory is freed
    }

    [Fact]
    public void TooManySteps_DropTheOldest_AndFreeThem()
    {
        var h = History(steps: 3);
        var all = new List<State>();
        for (int i = 0; i < 5; i++) { var s = S(i.ToString()); all.Add(s); h.Record(s); }
        Assert.Equal(3, h.UndoCount);
        Assert.True(all[0].Discarded && all[1].Discarded);
        Assert.False(all[2].Discarded || all[3].Discarded || all[4].Discarded);
    }

    [Fact]
    public void TooManyBytes_DropTheOldest_ButAlwaysKeepTheNewest()
    {
        var h = History(steps: 100, bytes: 100);
        var a = S("a", 60); var b = S("b", 60); var huge = S("huge", 500);
        h.Record(a); h.Record(b);
        Assert.Equal(1, h.UndoCount);          // 120 > 100: a is gone
        Assert.True(a.Discarded);
        h.Record(huge);
        Assert.Equal(1, h.UndoCount);          // even a state over the budget is kept alone
        Assert.True(b.Discarded);
        Assert.False(huge.Discarded);
    }

    [Fact]
    public void Reset_ForgetsEverything_AndFreesIt()
    {
        var h = History();
        var a = S("a");
        h.Record(a);
        var cur = S("cur");
        h.Undo(cur);
        h.Reset();
        Assert.False(h.CanUndo);
        Assert.False(h.CanRedo);
        Assert.True(cur.Discarded);
    }
}

public class ScribbleShortcutsTests
{
    [Theory]
    [InlineData(Key.Z, KeyModifiers.Meta, ScribbleShortcut.Undo)]
    [InlineData(Key.Z, KeyModifiers.Meta | KeyModifiers.Shift, ScribbleShortcut.Redo)]
    [InlineData(Key.Y, KeyModifiers.Meta, ScribbleShortcut.Redo)]
    [InlineData(Key.C, KeyModifiers.Meta, ScribbleShortcut.Copy)]
    [InlineData(Key.V, KeyModifiers.Meta, ScribbleShortcut.Paste)]
    [InlineData(Key.X, KeyModifiers.Meta, ScribbleShortcut.Cut)]
    [InlineData(Key.A, KeyModifiers.Meta, ScribbleShortcut.SelectAll)]
    [InlineData(Key.OemPlus, KeyModifiers.Meta, ScribbleShortcut.ZoomIn)]
    [InlineData(Key.OemPlus, KeyModifiers.Meta | KeyModifiers.Shift, ScribbleShortcut.ZoomIn)]
    [InlineData(Key.OemMinus, KeyModifiers.Meta, ScribbleShortcut.ZoomOut)]
    public void OnAMac_TheCommandKeyIsTheMainKey(Key key, KeyModifiers mods, ScribbleShortcut expected) =>
        Assert.Equal(expected, ScribbleShortcuts.Classify(key, mods, mac: true));

    [Theory]
    [InlineData(Key.Z, KeyModifiers.Control, ScribbleShortcut.Undo)]
    [InlineData(Key.Z, KeyModifiers.Control | KeyModifiers.Shift, ScribbleShortcut.Redo)]
    [InlineData(Key.Y, KeyModifiers.Control, ScribbleShortcut.Redo)]
    public void ElsewhereTheControlKeyIs(Key key, KeyModifiers mods, ScribbleShortcut expected) =>
        Assert.Equal(expected, ScribbleShortcuts.Classify(key, mods, mac: false));

    [Theory]
    [InlineData(Key.Z, KeyModifiers.None)]                                       // a plain letter
    [InlineData(Key.Z, KeyModifiers.Shift)]
    [InlineData(Key.Z, KeyModifiers.Control)]                                    // Control+Z isn't undo on a Mac
    [InlineData(Key.Z, KeyModifiers.Meta | KeyModifiers.Alt)]                    // Option makes it another command
    [InlineData(Key.Z, KeyModifiers.Meta | KeyModifiers.Control)]
    [InlineData(Key.Y, KeyModifiers.Meta | KeyModifiers.Shift)]
    [InlineData(Key.C, KeyModifiers.Meta | KeyModifiers.Shift)]
    [InlineData(Key.S, KeyModifiers.Meta)]                                       // not a Scribble shortcut
    [InlineData(Key.Delete, KeyModifiers.Meta)]
    public void OtherKeys_AreNotScribbleShortcuts(Key key, KeyModifiers mods) =>
        Assert.Equal(ScribbleShortcut.None, ScribbleShortcuts.Classify(key, mods, mac: true));
}

/// <summary>TASKS #54: where a mouse press lands on the on-screen radial menu, and click versus drag.</summary>
public class RadialMenuMouseTests
{
    private const int Size = 50;   // 160 pt outer radius, 41.6 pt centre

    [Fact]
    public void Centre_IsTheX()
    {
        var (t, slice, edge) = RadialMenuMouse.HitTest(0, 0, Size);
        Assert.Equal((RadialTarget.Centre, -1, false), (t, slice, edge));
    }

    [Fact]
    public void BeyondTheRing_IsOutside() =>
        Assert.Equal(RadialTarget.Outside, RadialMenuMouse.HitTest(0, -500, Size).Target);

    [Theory]
    [InlineData(0, -100, 0)]      // top
    [InlineData(100, 0, 2)]       // right
    [InlineData(0, 100, 4)]       // bottom
    [InlineData(-100, 0, 6)]      // left
    [InlineData(71, -71, 1)]      // top right
    public void InsideTheRing_IsTheSliceUnderThePointer(double dx, double dy, int expected)
    {
        var (t, slice, edge) = RadialMenuMouse.HitTest(dx, dy, Size);
        Assert.Equal((RadialTarget.Slice, expected, false), (t, slice, edge));
    }

    [Fact]
    public void TheOuterRim_CanBeGrabbed_TheRestCannot()
    {
        double outer = RadialMenuGeometry.OuterRadius(Size);
        Assert.True(RadialMenuMouse.HitTest(0, -(outer - 5), Size).OnEdge);
        Assert.False(RadialMenuMouse.HitTest(0, -(outer - RadialMenuMouse.EdgeBand - 5), Size).OnEdge);
    }

    [Fact]
    public void ASliceClick_DoesNotMove_AndAPressThatStaysPutIsAClick()
    {
        var press = new RadialMenuPress((100, 100), (0, 0), RadialTarget.Slice, 2, onEdge: false);
        Assert.Null(press.Move((101, 101)));
        Assert.False(press.Dragging);
    }

    [Fact]
    public void PressedInTheMiddleOfASlice_NeverDrags_HoweverFarItMoves()
    {
        var press = new RadialMenuPress((100, 100), (0, 0), RadialTarget.Slice, 2, onEdge: false);
        Assert.Null(press.Move((300, 300)));
        Assert.False(press.Dragging);
    }

    [Fact]
    public void PressedOnTheRim_BecomesADragAfterAFewPoints_AndTheWindowFollows()
    {
        var press = new RadialMenuPress((100, 100), (50, 60), RadialTarget.Slice, 2, onEdge: true);
        Assert.Null(press.Move((102, 101)));                      // under the threshold: still a click
        var o = press.Move((130, 90));
        Assert.True(press.Dragging);
        Assert.Equal((80.0, 50.0), o);                            // origin + the cursor's movement
        Assert.Equal((70.0, 70.0), press.Move((120, 110)));       // keeps following, even back past the start
    }

    [Fact]
    public void OnceDragging_SmallMovesAreStillDrags()
    {
        var press = new RadialMenuPress((0, 0), (10, 10), RadialTarget.Slice, 0, onEdge: true);
        press.Move((50, 0));
        Assert.Equal((10.0, 10.0), press.Move((0, 0)));
        Assert.True(press.Dragging);
    }
}
