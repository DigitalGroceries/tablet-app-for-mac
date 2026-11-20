using System;
using Avalonia;

namespace OpenTabletArtist.Helpers;

/// <summary>
/// V29 #164 (Zak 20 Nov, build 7 report 12:01: the app's window didn't move when dragged by its top edge with the pen, and only
/// about half the time with the mouse, while other apps' windows moved). The window has no Mac title bar of its own: a thin strip
/// asks macOS to drag it (Window.BeginMoveDrag). On macOS that request uses the last mouse press Avalonia recorded and silently
/// does nothing without a usable one (Avalonia PR #21827). So the Mac's drag is still asked for first, and when the window hasn't
/// moved although the pointer has, the app moves the window itself until the button is let go.
/// </summary>
public sealed class TitleBarDrag
{
    /// <summary>How far the pointer must move (pixels) before the app decides the Mac's drag didn't start (the Mac's own drag moves the window after about 3).</summary>
    public const int StartDistance = 8;

    /// <summary>The app takes over only this soon after the press (a real drag moves at once). Later moves with the button
    /// seemingly still held are a press whose release the Mac's own drag swallowed, and must not move the window.</summary>
    public static readonly TimeSpan TakeOverWithin = TimeSpan.FromMilliseconds(1000);

    private PixelPoint _startWindow, _startPointer;
    private DateTime _pressedAt;

    public bool Pressed { get; private set; }

    /// <summary>The app is moving the window (the Mac's drag didn't start).</summary>
    public bool ByApp { get; private set; }

    public void Press(PixelPoint window, PixelPoint pointer, DateTime now)
    {
        _startWindow = window;
        _startPointer = pointer;
        _pressedAt = now;
        Pressed = true;
        ByApp = false;
    }

    /// <summary>The pointer moved with the left button down: the window's new position when the app moves it, else null.</summary>
    public PixelPoint? Move(PixelPoint window, PixelPoint pointer, DateTime now)
    {
        if (!Pressed) return null;
        var dx = pointer.X - _startPointer.X;
        var dy = pointer.Y - _startPointer.Y;
        if (!ByApp)
        {
            if (window != _startWindow || now - _pressedAt > TakeOverWithin)
            {
                Pressed = false;   // the Mac's drag moved it, or this is a stale press: nothing for the app to do
                return null;
            }
            if (Math.Abs(dx) < StartDistance && Math.Abs(dy) < StartDistance) return null;
            ByApp = true;
        }
        return new PixelPoint(_startWindow.X + dx, _startWindow.Y + dy);
    }

    /// <summary>The button was let go (or the pointer lost): true when the app had been moving the window.</summary>
    public bool Release()
    {
        var byApp = ByApp;
        Pressed = false;
        ByApp = false;
        return byApp;
    }
}
