using System;
using Avalonia;

namespace OpenTabletArtist.Helpers;

/// <summary>
/// V29 #164 (Zak 20 Nov, build 7 report 12:01: the app's window didn't move when dragged by its top edge with the pen, and only
/// about half the time with the mouse, while other apps' windows moved). The window has no Mac title bar of its own: a thin strip
/// asks macOS to drag it (Window.BeginMoveDrag). On macOS that request uses the last mouse press Avalonia recorded and silently
/// does nothing without a usable one (Avalonia PR #21827). So the Mac's drag is still asked for first, and when the window hasn't
/// moved although the pointer has, the app moves the window itself until the button is let go.
/// <para>Grok's review: the pointer is measured from the spot that was grabbed, in the window's own coordinates converted with the
/// window's current place and scale (no mixing of a remembered screen point with a moved window, so no creep and no jump between a
/// Retina and a normal screen); the window is moved by that much each time; the app hands back when the Mac's drag shows up late.</para>
/// </summary>
public sealed class TitleBarDrag
{
    /// <summary>How far the pointer must move (pixels) before the app decides the Mac's drag didn't start (the Mac's own drag moves
    /// the window after about 3).</summary>
    public const int StartDistance = 8;

    /// <summary>How far the window may be from where the app last put it before the app decides the Mac's drag took over.</summary>
    public const int HandOverDistance = 2;

    /// <summary>The app takes over only this soon after the press (a real drag moves at once). Later moves with the button
    /// seemingly still held are a press whose release the Mac's own drag swallowed, and must not move the window.</summary>
    public static readonly TimeSpan TakeOverWithin = TimeSpan.FromMilliseconds(1000);

    private PixelPoint _startWindow, _lastSet;
    private DateTime _pressedAt;

    public bool Pressed { get; private set; }

    /// <summary>The app is moving the window (the Mac's drag didn't start).</summary>
    public bool ByApp { get; private set; }

    public void Press(PixelPoint window, DateTime now)
    {
        _startWindow = window;
        _lastSet = window;
        _pressedAt = now;
        Pressed = true;
        ByApp = false;
    }

    /// <summary>
    /// The pointer moved with the left button down. <paramref name="fromGrab"/> is how far the pointer is from the spot that was
    /// grabbed, both converted to the screen with the window where it is now. Returns the window's new position when the app moves
    /// it, else null (and stops when the Mac's drag moved the window, or this press is stale).
    /// </summary>
    public PixelPoint? Move(PixelPoint window, PixelVector fromGrab, DateTime now)
    {
        if (!Pressed) return null;
        if (!ByApp)
        {
            if (window != _startWindow || now - _pressedAt > TakeOverWithin)
            {
                Pressed = false;   // the Mac's drag moved it, or this is a stale press: nothing for the app to do
                return null;
            }
            if (Math.Abs(fromGrab.X) < StartDistance && Math.Abs(fromGrab.Y) < StartDistance) return null;
            ByApp = true;
        }
        else if (Math.Abs(window.X - _lastSet.X) > HandOverDistance || window.Y < _lastSet.Y - HandOverDistance)
        {
            // The Mac's drag started late and moved it too: hand over (macOS only pushes a window DOWN from under the menu bar,
            // so a window lower than where it was put is a clamp, not a drag).
            Pressed = false;
            ByApp = false;
            return null;
        }
        if (fromGrab.X == 0 && fromGrab.Y == 0) return null;
        _lastSet = new PixelPoint(window.X + fromGrab.X, window.Y + fromGrab.Y);
        return _lastSet;
    }

    /// <summary>The button was let go (or the pointer lost, or a new press): true when the app had been moving the window.</summary>
    public bool Release()
    {
        var byApp = ByApp;
        Pressed = false;
        ByApp = false;
        return byApp;
    }
}
