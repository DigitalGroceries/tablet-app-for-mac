using System;
using Avalonia;

namespace OpenTabletArtist.Helpers;

/// <summary>
/// V29 #176 (Zak 20 Nov, builds 9-11 on macOS 27.2.1: the app's window "doesnt work half the time" and "gets stuck and jittery in
/// between the two screens", while Finder's windows always move). macOS 27.2 has a regression: -[NSWindow performWindowDragWithEvent:]
/// is intermittent for apps that draw their own title bar (it fails or starts seconds late), while Finder and Safari are fine.
/// Reported by Warp (warpdotdev/warp#16508, Apple FB25207761) with A/B data: moving the window by hand from the drag events worked
/// every time. So on macOS 27.2 and later the app never asks macOS for its drag: the window's new place is where it was at the
/// press plus how far the pointer has moved since (both in global points, read from macOS), nothing else.
/// </summary>
public sealed class ManualWindowDrag
{
    /// <summary>How far (points) the pointer must move before the window follows: a click on the strip moves nothing.</summary>
    public const int StartDistance = 3;

    private PixelPoint _origin, _cursorAtPress;

    public bool Pressed { get; private set; }
    public bool Moving { get; private set; }

    /// <summary>Where the app last put the window (null before the first move).</summary>
    public PixelPoint? LastSet { get; private set; }

    public void Press(PixelPoint windowOrigin, PixelPoint cursor)
    {
        _origin = windowOrigin;
        _cursorAtPress = cursor;
        Pressed = true;
        Moving = false;
        LastSet = null;
    }

    /// <summary>The pointer is at <paramref name="cursor"/> with the button down: the window's new top-left, or null when nothing moves.</summary>
    public PixelPoint? Move(PixelPoint cursor)
    {
        if (!Pressed) return null;
        int dx = cursor.X - _cursorAtPress.X, dy = cursor.Y - _cursorAtPress.Y;
        if (!Moving && Math.Abs(dx) < StartDistance && Math.Abs(dy) < StartDistance) return null;
        Moving = true;
        var to = new PixelPoint(_origin.X + dx, _origin.Y + dy);
        if (LastSet == to) return null;
        LastSet = to;
        return to;
    }

    /// <summary>The button was let go: true when the window had been moved.</summary>
    public bool Release()
    {
        bool moved = Moving;
        Pressed = false;
        Moving = false;
        return moved;
    }
}
