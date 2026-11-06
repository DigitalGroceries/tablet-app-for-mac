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
        // Version 25 (Double Click Fix): physical pixel range for an All-displays desktop
        // (~3360 px over ~157 mm). UI shows 0..100 mapped onto this range; Default is 100 px (UI 50).
        public const int DefaultMoveTolerance = 100;
        public const int MinMoveTolerance = 8;
        public const int MaxMoveTolerance = 192;
        public const int UiMin = 0;
        public const int UiMax = 100;
        public const int UiDefault = 50;

        private Vector2 _lastMouseDownPosition;
        private Vector2 _dragOrigin;
        private bool _movedWhileHeld;
        private int _clickState;
        private long _lastDownTimestampMs;
        private bool _hasLastDown;

        public int MoveTolerance { get; set; } = DefaultMoveTolerance;

        /// <summary>System double-click interval in milliseconds.</summary>
        public double IntervalMs { get; set; } = 500;

        /// <summary>
        /// When true, hold-still / deadzone is skipped for the current stroke (tip-down was over our own
        /// app window). Finder and other apps still get the Double Click Fix.
        /// </summary>
        public bool BypassHoldStill { get; set; }

        public int ClickState => _clickState;

        /// <summary>Where the current tip-down started (hold-still pin for this stroke).</summary>
        public Vector2 LastDownPosition => _lastMouseDownPosition;

        /// <summary>
        /// Version 25: while a button is held and the pen has not yet moved beyond
        /// <see cref="MoveTolerance"/> from the down point, the pointer stays pinned there so pen-tip
        /// jitter does not reach macOS as a drag. Once the pen moves farther it is a real drag: further
        /// movement is followed from the hold point (deadzone, no catch-up teleport to the raw pen).
        /// </summary>
        public Vector2 FilterPosition(Vector2 position, bool buttonHeld)
        {
            if (!buttonHeld || !_hasLastDown || BypassHoldStill)
                return position;

            if (!_movedWhileHeld)
            {
                if (Vector2.Distance(_lastMouseDownPosition, position) <= MoveTolerance)
                    return _lastMouseDownPosition;

                _movedWhileHeld = true;
                _dragOrigin = position;
                // Stay on the hold point for this sample — no jump to the raw pen.
                return _lastMouseDownPosition;
            }

            return _lastMouseDownPosition + (position - _dragOrigin);
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
        /// On mouse-up after a sub-threshold wobble, keep the pointer on the hold point so the click
        /// lands on the target (no snap to the raw pen). After a real drag, use the raw position.
        /// </summary>
        public Vector2 PositionForMouseUp(Vector2 raw) =>
            _hasLastDown && !_movedWhileHeld && !BypassHoldStill ? _lastMouseDownPosition : raw;

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
            {
                if (!_movedWhileHeld)
                    _dragOrigin = position;
                _movedWhileHeld = true;
            }
        }

        /// <summary>
        /// Resolves the click count for a mouse-down at <paramref name="position"/> at
        /// <paramref name="timestampMs"/>, then stores that down as the new reference.
        /// Two nearby taps still count as a double-click without moving the pointer to the first pixel.
        /// </summary>
        public int OnMouseDown(Vector2 position, long timestampMs)
        {
            LastKnownPosition = position;
            var timedOut = _hasLastDown && (timestampMs - _lastDownTimestampMs) > IntervalMs;
            var tooFar = _hasLastDown && Vector2.Distance(_lastMouseDownPosition, position) > MoveTolerance;
            var invalidated = !_hasLastDown || _clickState == 0 || _movedWhileHeld || timedOut || tooFar;

            if (invalidated)
                _clickState = 1;
            else
                _clickState++;

            // Always pin hold-still to THIS tap (never relocate to the previous tap's pixel).
            _lastMouseDownPosition = position;
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
            BypassHoldStill = false;
        }

        public void Reset()
        {
            _clickState = 0;
            _movedWhileHeld = false;
            _hasLastDown = false;
            BypassHoldStill = false;
        }

        public static int ClampTolerance(int pixels) =>
            Math.Clamp(pixels <= 0 ? DefaultMoveTolerance : pixels, MinMoveTolerance, MaxMoveTolerance);

        /// <summary>UI 0..100 → physical pixels 8..192 (linear). Display 50 → 100 px (Default).</summary>
        public static int DisplayToPixels(int display)
        {
            var d = Math.Clamp(display, UiMin, UiMax);
            return (int)Math.Round(MinMoveTolerance + d / (double)UiMax * (MaxMoveTolerance - MinMoveTolerance));
        }

        /// <summary>Physical pixels 8..192 → UI 0..100. Keeps older saved pixel values working.</summary>
        public static int PixelsToDisplay(int pixels)
        {
            var p = ClampTolerance(pixels);
            return (int)Math.Round((p - MinMoveTolerance) / (double)(MaxMoveTolerance - MinMoveTolerance) * UiMax);
        }
    }
}
