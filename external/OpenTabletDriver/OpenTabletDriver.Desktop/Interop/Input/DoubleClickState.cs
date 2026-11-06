using System;
using System.Numerics;

namespace OpenTabletDriver.Desktop.Interop.Input
{
    /// <summary>
    /// Counts successive mouse-downs as a double-click without letting hover movement between taps
    /// invalidate the sequence. Only movement while a button is held (a drag), a second down farther
    /// than <see cref="MoveTolerance"/>, or an interval timeout starts a new single click.
    /// </summary>
    public sealed class DoubleClickState
    {
        // Version 25 (Double Click Fix): the old 8..64 scale was too tight for a tablet mapped across a
        // large All-displays desktop (~3360 px over ~157 mm, about 21 px per mm: 64 px is only ~3 mm).
        // Default 64 px is ~3 mm of tip travel there; Large 192 px is ~9 mm.
        public const int DefaultMoveTolerance = 64;
        public const int MinMoveTolerance = 8;
        public const int MaxMoveTolerance = 192;

        private Vector2 _lastMouseDownPosition;
        private bool _movedWhileHeld;
        private int _clickState;
        private long _lastDownTimestampMs;
        private bool _hasLastDown;

        public int MoveTolerance { get; set; } = DefaultMoveTolerance;

        /// <summary>System double-click interval in milliseconds.</summary>
        public double IntervalMs { get; set; } = 500;

        public int ClickState => _clickState;

        /// <summary>Where the current click sequence started (the position posted for every down in it).</summary>
        public Vector2 LastDownPosition => _lastMouseDownPosition;

        /// <summary>
        /// Version 25 (Double Click Fix): while a button is held and the pen has not yet moved beyond
        /// <see cref="MoveTolerance"/> from the down point, the pointer stays pinned there, so pen-tip jitter
        /// does not reach macOS as a drag (Finder used to slide folders instead of opening them).
        /// Once the pen moves farther it is a real drag and the raw position is used.
        /// </summary>
        public Vector2 FilterPosition(Vector2 position, bool buttonHeld)
        {
            if (buttonHeld && _hasLastDown && !_movedWhileHeld
                && Vector2.Distance(_lastMouseDownPosition, position) <= MoveTolerance)
                return _lastMouseDownPosition;
            return position;
        }

        public bool MovedWhileHeld => _movedWhileHeld;

        /// <summary>The last pointer position seen (by <see cref="OnPosition"/> or a mouse-down), or null before any.</summary>
        public Vector2? LastKnownPosition { get; private set; }

        /// <summary>
        /// Where a mouse-down happened: the pending position when there is one, otherwise the last known
        /// position (e.g. two buttons change in one report and the first already drained the position).
        /// Never <see cref="Vector2.Zero"/> by default, which would count as "too far" and reset to 1.
        /// </summary>
        public Vector2 DownPosition(Vector2? pending) => pending ?? LastKnownPosition ?? _lastMouseDownPosition;

        /// <summary>
        /// Records a pointer position. When <paramref name="buttonHeld"/> is true, movement beyond the
        /// tolerance marks a drag and breaks the next double-click. Hover (button not held) is ignored.
        /// </summary>
        public void OnPosition(Vector2 position, bool buttonHeld)
        {
            LastKnownPosition = position;
            if (!buttonHeld || !_hasLastDown)
                return;

            if (Vector2.Distance(_lastMouseDownPosition, position) > MoveTolerance)
                _movedWhileHeld = true;
        }

        /// <summary>
        /// Resolves the click count for a mouse-down at <paramref name="position"/> at
        /// <paramref name="timestampMs"/>, then stores that down as the new reference.
        /// </summary>
        public int OnMouseDown(Vector2 position, long timestampMs)
        {
            LastKnownPosition = position;
            var timedOut = _hasLastDown && (timestampMs - _lastDownTimestampMs) > IntervalMs;
            var tooFar = _hasLastDown && Vector2.Distance(_lastMouseDownPosition, position) > MoveTolerance;
            var invalidated = !_hasLastDown || _clickState == 0 || _movedWhileHeld || timedOut || tooFar;

            if (invalidated)
            {
                _clickState = 1;
                _lastMouseDownPosition = position;
            }
            else
            {
                // Keep the first down's position so every click in the sequence lands on the same pixel.
                _clickState++;
            }

            _lastDownTimestampMs = timestampMs;
            _hasLastDown = true;
            _movedWhileHeld = false;
            return _clickState;
        }

        /// <summary>
        /// Updates state on mouse-up. When the sequence was invalidated by a drag or timeout, the
        /// click count resets so the next down starts at 1.
        /// </summary>
        public void OnMouseUp(long timestampMs)
        {
            var timedOut = _hasLastDown && (timestampMs - _lastDownTimestampMs) > IntervalMs;
            if (_movedWhileHeld || timedOut)
                _clickState = 0;
        }

        public void Reset()
        {
            _clickState = 0;
            _movedWhileHeld = false;
            _hasLastDown = false;
        }

        public static int ClampTolerance(int pixels) =>
            Math.Clamp(pixels <= 0 ? DefaultMoveTolerance : pixels, MinMoveTolerance, MaxMoveTolerance);
    }
}
