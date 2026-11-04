using System;
using System.Collections.Generic;
using System.Numerics;

namespace OpenTabletArtist.Dynamics;

/// <summary>What the touch trackpad should do with the mouse button.</summary>
public enum TouchButtonAction
{
    /// <summary>Left button down (start of a drag).</summary>
    LeftDown,
    /// <summary>Left button up (end of a drag).</summary>
    LeftUp,
    /// <summary>A single left click.</summary>
    LeftClick,
    /// <summary>The second click of a double-click (down and up, click count 2), right after a LeftUp.</summary>
    LeftSecondClick,
    /// <summary>A right click (two-finger tap).</summary>
    RightClick,
}

/// <summary>
/// Discussion #31 (Jay, PTH-651, 2 Nov 2026): taps and drags for the finger-touch trackpad, the way a Mac trackpad
/// does them with "Tap to click":
/// <list type="bullet">
/// <item>one-finger tap = click (sent a moment later, so a second tap can still turn it into a drag or a double-click);</item>
/// <item>tap, then touch again and move = drag (the button stays down until the finger lifts);</item>
/// <item>two quick taps = double-click;</item>
/// <item>two-finger tap = right click.</item>
/// </list>
/// Pure logic with the time passed in, so it can be tested without a tablet. The filter posts the mouse events.
/// Safety: <see cref="Cancel"/> (pen near) and <see cref="Tick"/> (touch reports stopped) always let go of the button.
/// </summary>
public sealed class TouchTapGestures
{
    /// <summary>Longest touch that still counts as a tap.</summary>
    public static readonly TimeSpan TapMaxTime = TimeSpan.FromMilliseconds(250);
    /// <summary>How long after a tap a second touch turns it into a drag or a double-click.</summary>
    public static readonly TimeSpan SecondTapWindow = TimeSpan.FromMilliseconds(250);
    /// <summary>A touch with no report for this long is treated as lifted (some tablets send nothing on lift).</summary>
    public static readonly TimeSpan StaleContact = TimeSpan.FromMilliseconds(300);
    /// <summary>Most a finger may move (mm) for the touch to still count as a tap.</summary>
    public const float TapMaxMoveMm = 1.5f;

    private bool _contact, _dragging, _secondTouch;
    private DateTime _start, _lastReport;
    private Vector2 _segmentStart;
    private int _fingers, _maxFingers;
    private float _movedMm;
    private DateTime? _clickDueAfter;

    /// <summary>True while the left button is held for a drag.</summary>
    public bool Dragging => _dragging;
    /// <summary>True while a finger is on the tablet or a tap's click is still waiting.</summary>
    public bool Busy => _contact || _clickDueAfter != null;

    /// <summary>A touch report with <paramref name="fingers"/> fingers (0 = all lifted) centred at <paramref name="centreMm"/>.</summary>
    public List<TouchButtonAction> Touch(int fingers, Vector2 centreMm, DateTime now)
    {
        if (fingers <= 0) return Release(now);
        var acts = new List<TouchButtonAction>();
        if (!_contact)
        {
            _contact = true; _start = now; _segmentStart = centreMm; _fingers = _maxFingers = fingers; _movedMm = 0;
            _secondTouch = false;
            if (_clickDueAfter is { } due && now <= due && fingers == 1)
            {
                // Tap, then touch again: hold the button. Moving drags; a quick lift makes it a double-click.
                _clickDueAfter = null; _dragging = true; _secondTouch = true;
                acts.Add(TouchButtonAction.LeftDown);
            }
            else if (_clickDueAfter != null)
            {
                _clickDueAfter = null;
                acts.Add(TouchButtonAction.LeftClick);   // the earlier tap's click, before anything else
            }
        }
        else
        {
            if (fingers != _fingers) { _fingers = fingers; _segmentStart = centreMm; }   // fingers land at slightly different times
            if (fingers > _maxFingers) _maxFingers = fingers;
            _movedMm = MathF.Max(_movedMm, Vector2.Distance(centreMm, _segmentStart));
        }
        _lastReport = now;
        return acts;
    }

    /// <summary>All fingers lifted.</summary>
    public List<TouchButtonAction> Release(DateTime now)
    {
        var acts = new List<TouchButtonAction>();
        if (!_contact) return acts;
        _contact = false;
        var tap = now - _start <= TapMaxTime && _movedMm <= TapMaxMoveMm;
        if (_dragging)
        {
            _dragging = false;
            acts.Add(TouchButtonAction.LeftUp);
            if (_secondTouch && tap) acts.Add(TouchButtonAction.LeftSecondClick);   // two quick taps = double-click
            return acts;
        }
        if (!tap) return acts;
        if (_maxFingers == 1) _clickDueAfter = now + SecondTapWindow;
        else if (_maxFingers == 2) acts.Add(TouchButtonAction.RightClick);
        return acts;
    }

    /// <summary>Called on a timer: sends a waiting click once no second tap came, and lifts a touch whose reports stopped.</summary>
    public List<TouchButtonAction> Tick(DateTime now)
    {
        var acts = new List<TouchButtonAction>();
        if (_contact && now - _lastReport > StaleContact) acts.AddRange(Release(_lastReport));
        if (_clickDueAfter is { } due && now > due)
        {
            _clickDueAfter = null;
            acts.Add(TouchButtonAction.LeftClick);
        }
        return acts;
    }

    /// <summary>The pen came near: forget the touch, drop a waiting click, and let go of a held button.</summary>
    public List<TouchButtonAction> Cancel()
    {
        var acts = new List<TouchButtonAction>();
        if (_dragging) acts.Add(TouchButtonAction.LeftUp);
        _dragging = false; _contact = false; _clickDueAfter = null; _secondTouch = false;
        return acts;
    }
}
