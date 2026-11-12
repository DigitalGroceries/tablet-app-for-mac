using System;

namespace OpenTabletArtist.Domain;

/// <summary>What a mouse press on the on-screen radial menu landed on.</summary>
public enum RadialTarget
{
    /// <summary>The top half of the centre circle: close.</summary>
    Close,
    /// <summary>The bottom half of the centre circle (the open hand): drag to move the menu.</summary>
    Hand,
    Slice,
    Outside,
}

/// <summary>Using the radial menu with the Mac mouse (TASKS #54): where a press lands, and the click-or-drag rule.
/// Points, y downwards, relative to the menu's centre, as <see cref="RadialMenuGeometry"/>.</summary>
public static class RadialMenuMouse
{
    /// <summary>A press on the hand that moves less than this does nothing; more and the menu follows the pointer.</summary>
    public const double DragThreshold = 4;

    public static (RadialTarget Target, int Slice) HitTest(double dx, double dy, int size)
    {
        int s = RadialMenuGeometry.SliceAt(dx, dy, size);
        if (s == RadialMenuGeometry.Centre) return (RadialTarget.Close, -1);
        if (s == RadialMenuGeometry.Hand) return (RadialTarget.Hand, -1);
        if (s == RadialMenuGeometry.Outside) return (RadialTarget.Outside, -1);
        return (RadialTarget.Slice, s);
    }
}

/// <summary>One mouse press on the menu, from button down to button up. Only a press on the hand can become a drag, and
/// only after the pointer has moved more than <see cref="RadialMenuMouse.DragThreshold"/>; every other press is a click on
/// what was pressed (and a press on the hand that never moves is a click that does nothing). Positions are global
/// screen points.</summary>
public sealed class RadialMenuPress
{
    private readonly (double X, double Y) _startCursor;
    private readonly (double X, double Y) _startWindow;

    public RadialTarget Target { get; }
    public int Slice { get; }
    /// <summary>True once the press has turned into a drag.</summary>
    public bool Dragging { get; private set; }

    public RadialMenuPress((double X, double Y) cursor, (double X, double Y) windowOrigin, RadialTarget target, int slice)
    {
        _startCursor = cursor; _startWindow = windowOrigin; Target = target; Slice = slice;
    }

    /// <summary>The pointer moved: the window's new origin while dragging, otherwise null.</summary>
    public (double X, double Y)? Move((double X, double Y) cursor)
    {
        double dx = cursor.X - _startCursor.X, dy = cursor.Y - _startCursor.Y;
        if (!Dragging && Target == RadialTarget.Hand && Math.Sqrt(dx * dx + dy * dy) > RadialMenuMouse.DragThreshold) Dragging = true;
        return Dragging ? (_startWindow.X + dx, _startWindow.Y + dy) : null;
    }
}
