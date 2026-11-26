using System;
using System.Collections.Generic;
using Avalonia;

namespace OpenTabletArtist.Helpers;

/// <summary>
/// V29 #164 (Digital Groceries 20 Nov, build 7 report 12:01: the app's window didn't move when dragged by its top edge with the pen, and only
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
    public PixelPoint? Move(PixelPoint window, PixelVector fromGrab, DateTime now, int? workTop = null)
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
        else if (Math.Abs(window.X - _lastSet.X) > HandOverDistance || window.Y < _lastSet.Y - HandOverDistance
                 || (window.Y > _lastSet.Y + HandOverDistance && workTop is { } top && Math.Abs(window.Y - top) > HandOverDistance))
        {
            // The Mac's drag started late and moved it too: hand over. macOS only pushes a window DOWN from under the menu bar, so a
            // window lower than where it was put is a clamp when it sits at that screen's usable top (#176, Grok: only then; a lower
            // window anywhere else is a late Mac drag going down).
            Pressed = false;
            ByApp = false;
            return null;
        }
        if (fromGrab.X == 0 && fromGrab.Y == 0) return null;
        _lastSet = new PixelPoint(window.X + fromGrab.X, window.Y + fromGrab.Y);
        return _lastSet;
    }

    /// <summary>One screen: its whole area and the part windows may use (below the menu bar), in the same units as the window's
    /// position (macOS global points).</summary>
    public readonly record struct ScreenArea(PixelRect Bounds, PixelRect Work);

    /// <summary>
    /// V29 #176 (Digital Groceries 20 Nov, build 9: "sometimes the app window gets stuck at the end of the first screen and does not get dragged to
    /// the next one", while a Finder window of the same size always crossed). A window the app moves with code still belongs to its
    /// screen, and macOS pushes it back below that screen's menu bar, so it can't creep across an edge the way the Mac's own drag
    /// does (Grok's review). So: on the same screen the window never goes above the usable area (no fight with macOS); once the
    /// pointer is on another screen, the window goes wholly onto that screen in one step.
    /// </summary>
    public static PixelPoint Place(PixelPoint target, PixelSize size, PixelPoint pointer, PixelRect current, IReadOnlyList<ScreenArea> screens)
    {
        if (screens.Count == 0) return target;
        int under = IndexOf(screens, pointer);
        if (under < 0) return target;
        var ps = screens[under];
        int on = MostOf(screens, current);
        if (on != under) return Inside(target, size, ps.Work);
        return target.Y < ps.Work.Y ? new PixelPoint(target.X, ps.Work.Y) : target;
    }

    /// <summary>After the app's drag: a window left across two screens goes wholly onto the screen holding the pointer
    /// (the Mac's own drag never leaves one straddling). Null when it is fine where it is.</summary>
    public static PixelPoint? SnapAfterDrag(PixelRect window, PixelPoint pointer, IReadOnlyList<ScreenArea> screens)
    {
        int touched = 0;
        foreach (var sc in screens) if (Area(sc.Bounds.Intersect(window)) > 0) touched++;
        int under = IndexOf(screens, pointer);
        if (under < 0 && touched >= 2) under = MostOf(screens, window);
        if (under < 0) return null;
        var work = screens[under].Work;
        bool tooHigh = window.Y < work.Y && work.Contains(new PixelPoint(window.X + window.Width / 2, work.Y));
        if (touched < 2 && !tooHigh) return null;
        var to = Inside(window.Position, window.Size, work);
        return to == window.Position ? null : to;
    }

    private static PixelPoint Inside(PixelPoint p, PixelSize size, PixelRect work)
    {
        int x = size.Width >= work.Width ? work.X : Math.Clamp(p.X, work.X, work.Right - size.Width);
        int y = size.Height >= work.Height ? work.Y : Math.Clamp(p.Y, work.Y, work.Bottom - size.Height);
        return new PixelPoint(x, y);
    }

    private static int IndexOf(IReadOnlyList<ScreenArea> screens, PixelPoint p)
    {
        for (int i = 0; i < screens.Count; i++) if (screens[i].Bounds.Contains(p)) return i;
        return -1;
    }

    private static int MostOf(IReadOnlyList<ScreenArea> screens, PixelRect r)
    {
        int best = -1; long most = 0;
        for (int i = 0; i < screens.Count; i++)
        {
            long a = Area(screens[i].Bounds.Intersect(r));
            if (a > most) { most = a; best = i; }
        }
        return best;
    }

    /// <summary>V29 #176 build 12: after a drag the app made, a window whose top is above the usable area (under the menu bar) of
    /// the screen holding most of it goes down to that usable top, as macOS itself places windows. Null when it is fine.</summary>
    public static PixelPoint? BelowMenuBar(PixelRect window, IReadOnlyList<ScreenArea> screens)
    {
        int i = MostOf(screens, window);
        if (i < 0) return null;
        var work = screens[i].Work;
        return window.Y < work.Y ? new PixelPoint(window.X, work.Y) : null;
    }

    /// <summary>The usable top of the screen holding most of <paramref name="window"/> (null when it is on none).</summary>
    public static int? WorkTopOf(PixelRect window, IReadOnlyList<ScreenArea> screens)
    {
        int i = MostOf(screens, window);
        return i < 0 ? null : screens[i].Work.Y;
    }

    private static long Area(PixelRect r) => r.Width <= 0 || r.Height <= 0 ? 0 : (long)r.Width * r.Height;

    /// <summary>The app put the window somewhere else than <see cref="Move"/> asked (kept on a screen, #176): the hand-over
    /// test compares with this place.</summary>
    public void PlacedAt(PixelPoint window) => _lastSet = window;

    /// <summary>The button was let go (or the pointer lost, or a new press): true when the app had been moving the window.</summary>
    public bool Release()
    {
        var byApp = ByApp;
        Pressed = false;
        ByApp = false;
        return byApp;
    }
}
