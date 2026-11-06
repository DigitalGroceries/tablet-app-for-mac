using System;
using System.Diagnostics;
using System.Numerics;
using OpenTabletDriver.Desktop.Interop.Input.Keyboard;
using OpenTabletDriver.Native.OSX;
using OpenTabletDriver.Native.OSX.Input;
using OpenTabletDriver.Plugin.Platform.Pointer;

namespace OpenTabletDriver.Desktop.Interop.Input
{
    using static OSX;

    public abstract class MacOSVirtualMouse : IMouseButtonHandler, IMouseScrollHandler, ISynchronousPointer, ITiltHandler, IEraserHandler, IPressureHandler, IMouseDoubleClickHandler
    {
        private const int ProximityExpiresDurationInMs = 200;

        // The capabilityMask is essential for Adobe software to recognize tablet pressure sensitivity,
        // a feature specific to Wacom devices.
        private const WacomCapabilityMask TabletCapabilityMask = WacomCapabilityMask.absXBitMask |
                                                                  WacomCapabilityMask.absYBitMask |
                                                                  WacomCapabilityMask.buttonsBitMask |
                                                                  WacomCapabilityMask.pressureBitMask |
                                                                  WacomCapabilityMask.tiltXBitMask |
                                                                  WacomCapabilityMask.tiltYBitMask |
                                                                  WacomCapabilityMask.deviceIdBitMask;

        // https://www.wacomeng.com/mac/Developers%20Guide.htm#vendorPointerType
        private const long VendorPointerType = 0x802; // General Stylus

        // A non-zero deviceId is essential for Adobe software to map tablet events to their
        // corresponding capabilities. Randomly generated and hopefully do not conflict with another driver.
        private const long DeviceId = 5303613955435230461;

        private int _currButtonStates;
        private int _prevButtonStates;
        private float? _pendingX;
        private float? _pendingY;
        private int? _scrollDeltaX;
        private int? _scrollDeltaY;
        private float? _pressure;
        private Vector2? _tilt;
        private bool? _isEraser;
        private CGMouseButton _lastButton;
        private readonly DoubleClickState _doubleClick = new();
        private readonly Stopwatch _stopWatch;
        private readonly Stopwatch _doubleClickClock;
        private readonly IntPtr _eventSource;
        private IntPtr _mouseEvent;
        private readonly MacOSVirtualKeyboard _keyboard;

        /// <inheritdoc />
        public int DoubleClickMoveTolerance
        {
            get => _doubleClick.MoveTolerance;
            set => _doubleClick.MoveTolerance = DoubleClickState.ClampTolerance(value);
        }

        public MacOSVirtualMouse()
        {
            _doubleClick.IntervalMs = GetDoubleClickInterval() * 1000;
            _doubleClick.MoveTolerance = DoubleClickState.DefaultMoveTolerance;
            _stopWatch = new Stopwatch();
            _stopWatch.Start();
            _doubleClickClock = Stopwatch.StartNew();
            _eventSource = CGEventSourceCreate(CGEventSourceStatePrivate);
            _mouseEvent = CGEventCreate(_eventSource);
            _keyboard = DesktopInterop.VirtualKeyboard as MacOSVirtualKeyboard
                        ?? throw new InvalidOperationException("Could not get virtual keyboard");
        }

        /// <inheritdoc />
        public void PostDoubleClick()
        {
            if (!_pendingX.HasValue || !_pendingY.HasValue)
                QueuePendingPositionFromSystem();
            var x = _pendingX ?? 0;
            var y = _pendingY ?? 0;
            var pos = new CGPoint(x, y);
            PostClickEvent(CGEventType.kCGEventLeftMouseDown, pos, clickState: 1);
            PostClickEvent(CGEventType.kCGEventLeftMouseUp, pos, clickState: 1);
            PostClickEvent(CGEventType.kCGEventLeftMouseDown, pos, clickState: 2);
            PostClickEvent(CGEventType.kCGEventLeftMouseUp, pos, clickState: 2);
            // A synthesised double-click must not leave a pending tip double-tap sequence.
            _doubleClick.Reset();
        }

        private void PostClickEvent(CGEventType type, CGPoint pos, int clickState)
        {
            var evt = CGEventCreateMouseEvent(_eventSource, type, pos, CGMouseButton.kCGMouseButtonLeft);
            CGEventSetIntegerValueField(evt, CGEventField.mouseEventClickState, clickState);
            CGEventPost(CGEventTapLocation.kCGHIDEventTap, evt);
            CFRelease(evt);
        }

        public void MouseDown(MouseButton button)
        {
            if (!_pendingX.HasValue)
                QueuePendingPositionFromSystem();
            SetButtonState(ref _currButtonStates, ToCGMouseButton(button), true);
        }

        public void MouseUp(MouseButton button)
        {
            if (!_pendingX.HasValue)
                QueuePendingPositionFromSystem();
            SetButtonState(ref _currButtonStates, ToCGMouseButton(button), false);
        }

        public void ScrollVertically(int amount)
        {
            _scrollDeltaX = -amount;
        }

        public void ScrollHorizontally(int amount)
        {
            _scrollDeltaY = -amount;
        }

        public void Flush()
        {
            if (_currButtonStates != _prevButtonStates)
            {
                // keys here just changed, no drag event should be sent
                ProcessKeyStates(_prevButtonStates, _currButtonStates);
                _prevButtonStates = _currButtonStates;
            }
            else if (DrainPendingPosition() is { } position)
            {
                // can send drag here
                var lastButtonSet = IsButtonSet(_currButtonStates, _lastButton);
                var cgEventType = ToDragCGEventType(_lastButton, lastButtonSet);
                CGEventSetType(_mouseEvent, cgEventType);
                SetPendingPosition(_mouseEvent, position.X, position.Y);
                ApplyTabletValues();
                PostEvent();
            }
            if (_scrollDeltaX.HasValue || _scrollDeltaY.HasValue)
            {
                var scrollEventRef = CGEventCreateScrollWheelEvent2(IntPtr.Zero, CGScrollEventUnit.kCGScrollEventUnitPixel, 2, _scrollDeltaX ?? 0, _scrollDeltaY ?? 0, 0);
                CGEventPost(CGEventTapLocation.kCGHIDEventTap, scrollEventRef);
                CFRelease(scrollEventRef);
                _scrollDeltaX = _scrollDeltaY = null;
            }
        }

        public void Reset()
        {
            // send a key up for all currently held keys
            if (_currButtonStates > 0)
            {
                ProcessKeyStates(_currButtonStates, 0);
                _prevButtonStates = 0;
                _currButtonStates = 0;
            }
        }

        public void SetEraser(bool isEraser)
        {
            if (_isEraser.HasValue && _isEraser.Value == isEraser)
                return;

            _isEraser = isEraser;

            // Immediately post a proximity event to notify applications of the tool change,
            // mirroring how Linux EvdevVirtualTablet sends the tool switch event right away.
            PostProximityEvent();

            // Reset the stopwatch so ApplyTabletValues doesn't redundantly re-send
            // a proximity event on the next frame.
            _stopWatch.Restart();
        }

        public void SetTilt(Vector2 tilt)
        {
            _tilt = tilt;
        }

        protected abstract void SetPendingPosition(IntPtr mouseEvent, float x, float y);
        protected abstract void ResetPendingPosition(IntPtr mouseEvent);

        // binding can be triggered by auxiliary buttons and cursor might be moved by other devices.
        // in such case we fetch the position from system.
        protected abstract void QueuePendingPositionFromSystem();

        protected void QueuePendingPosition(float x, float y)
        {
            _pendingX = x;
            _pendingY = y;
            // Hover between taps must not break a double-click; only movement while a button is held
            // (a drag) invalidates the click count.
            _doubleClick.OnPosition(new Vector2(x, y), buttonHeld: _currButtonStates != 0);
        }

        public void SetPressure(float percentage)
        {
            _pressure = percentage;
        }

        private void PostProximityEvent()
        {
            var pointerType = _isEraser ?? false
                ? NSPointingDeviceType.Eraser
                : NSPointingDeviceType.Pen;

            var proximityEvent = CGEventCreate(_eventSource);
            CGEventSetType(proximityEvent, CGEventType.kCGEventTabletProximity);
            CGEventSetIntegerValueField(proximityEvent, CGEventField.tabletProximityEventEnterProximity, 1);
            CGEventSetIntegerValueField(proximityEvent, CGEventField.tabletProximityEventPointerType, (long)pointerType);
            CGEventSetIntegerValueField(proximityEvent, CGEventField.tabletProximityEventCapabilityMask, (long)TabletCapabilityMask);
            CGEventSetIntegerValueField(proximityEvent, CGEventField.tabletProximityEventDeviceID, DeviceId);
            CGEventSetIntegerValueField(proximityEvent, CGEventField.tabletProximityEventVendorPointerType, VendorPointerType);

            CGEventPost(CGEventTapLocation.kCGHIDEventTap, proximityEvent);
            CFRelease(proximityEvent);
        }

        private Vector2? DrainPendingPosition()
        {
            if (_pendingX.HasValue && _pendingY.HasValue)
            {
                var vector2 = new Vector2(_pendingX.Value, _pendingY.Value);
                _pendingX = null;
                _pendingY = null;
                return vector2;
            }

            return null;
        }

        private void ProcessKeyStates(int prevButtonStates, int currButtonStates)
        {
            for (int i = 0; i < 5; i++)
            {
                var button = (CGMouseButton)i;
                var currState = IsButtonSet(currButtonStates, button);
                var prevState = IsButtonSet(prevButtonStates, button);

                if (currState != prevState)
                {
                    int clickState;
                    if (currState)
                    {
                        // No pending position (e.g. an earlier button in this report drained it): use the
                        // last known position, not Vector2.Zero, so the click count isn't reset.
                        var down = _doubleClick.DownPosition(_pendingX.HasValue && _pendingY.HasValue
                            ? new Vector2(_pendingX.Value, _pendingY.Value)
                            : null);
                        clickState = _doubleClick.OnMouseDown(down, _doubleClickClock.ElapsedMilliseconds);
                    }
                    else
                    {
                        // Post the same click count as the matching down, then update invalidation state.
                        clickState = _doubleClick.ClickState;
                        _doubleClick.OnMouseUp(_doubleClickClock.ElapsedMilliseconds);
                    }

                    // prepare the mouse event, we reset it here in case
                    // it's a relative event
                    ResetPendingPosition(_mouseEvent);

                    // propagate pending position to mouseEvent
                    var cgEventType = ToNoDragCGEventType(button, currState);
                    CGEventSetType(_mouseEvent, cgEventType);
                    if (DrainPendingPosition() is { } position)
                    {
                        SetPendingPosition(_mouseEvent, position.X, position.Y);
                    }
                    CGEventSetIntegerValueField(_mouseEvent, CGEventField.mouseEventButtonNumber, i);
                    CGEventSetIntegerValueField(_mouseEvent, CGEventField.mouseEventClickState, clickState); // clickState should be set to 1 (or more) during up, down, and drag events
                    ApplyTabletValues();
                    PostEvent();
                    _lastButton = button;
                }
            }

            if (currButtonStates == 0)
            {
                // no buttons are pressed, reset button to 0
                CGEventSetIntegerValueField(_mouseEvent, CGEventField.mouseEventButtonNumber, 0);
            }
        }

        private static CGMouseButton ToCGMouseButton(MouseButton button)
        {
            return button switch
            {
                MouseButton.Left => CGMouseButton.kCGMouseButtonLeft,
                MouseButton.Right => CGMouseButton.kCGMouseButtonRight,
                MouseButton.Middle => CGMouseButton.kCGMouseButtonCenter,
                MouseButton.Backward => CGMouseButton.kCGMouseButtonBackward,
                MouseButton.Forward => CGMouseButton.kCGMouseButtonForward,
                _ => throw new ArgumentException("Invalid mouse button", nameof(button))
            };
        }

        private static CGEventType ToNoDragCGEventType(CGMouseButton button, bool state)
        {
            return (button, state) switch
            {
                (CGMouseButton.kCGMouseButtonLeft, true) => CGEventType.kCGEventLeftMouseDown,
                (CGMouseButton.kCGMouseButtonLeft, false) => CGEventType.kCGEventLeftMouseUp,
                (CGMouseButton.kCGMouseButtonRight, true) => CGEventType.kCGEventRightMouseDown,
                (CGMouseButton.kCGMouseButtonRight, false) => CGEventType.kCGEventRightMouseUp,
                (_, true) => CGEventType.kCGEventOtherMouseDown,
                (_, false) => CGEventType.kCGEventOtherMouseUp,
            };
        }

        private static CGEventType ToDragCGEventType(CGMouseButton button, bool state)
        {
            return (button, state) switch
            {
                (CGMouseButton.kCGMouseButtonLeft, true) => CGEventType.kCGEventLeftMouseDragged,
                (CGMouseButton.kCGMouseButtonRight, true) => CGEventType.kCGEventRightMouseDragged,
                (_, true) => CGEventType.kCGEventOtherMouseDragged,
                (_, false) => CGEventType.kCGEventMouseMoved,
            };
        }

        private static bool IsButtonSet(int buttonStates, CGMouseButton button)
        {
            return (buttonStates & (1 << (int)button)) != 0;
        }

        private static void SetButtonState(ref int buttonStates, CGMouseButton button, bool state)
        {
            if (state)
                buttonStates |= 1 << (int)button;
            else
                buttonStates &= ~(1 << (int)button);
        }

        private void ApplyTabletValues()
        {
            // Send proximity events if there are no reports for a while.
            if (_currButtonStates == 0 && _prevButtonStates == 0)
            {
                var elapsed = _stopWatch.ElapsedMilliseconds;
                _stopWatch.Restart();
                if (elapsed > ProximityExpiresDurationInMs)
                {
                    var pointerType = _isEraser ?? false
                        ? NSPointingDeviceType.Eraser
                        : NSPointingDeviceType.Pen;

                    PostProximityEvent();

                    CGEventSetIntegerValueField(_mouseEvent, CGEventField.mouseEventSubtype, (long)CGMouseEventSubtype.TabletProximity);
                    CGEventSetIntegerValueField(_mouseEvent, CGEventField.tabletProximityEventEnterProximity, 1);
                    CGEventSetIntegerValueField(_mouseEvent, CGEventField.tabletProximityEventPointerType, (long)pointerType);
                    CGEventSetIntegerValueField(_mouseEvent, CGEventField.tabletProximityEventCapabilityMask, (long)TabletCapabilityMask);
                    CGEventSetIntegerValueField(_mouseEvent, CGEventField.tabletProximityEventDeviceID, DeviceId);
                    CGEventSetIntegerValueField(_mouseEvent, CGEventField.tabletProximityEventVendorPointerType, VendorPointerType);

                    return;
                }
            }

            // Qt < 5.9 interprets kCGTabletEventPointButtons as left, right, middle mouse click.
            // see https://codereview.qt-project.org/c/qt/qtbase/+/180717
            int buttons = 0;
            if (IsButtonSet(_currButtonStates, CGMouseButton.kCGMouseButtonLeft))
                buttons |= 1;
            else if (IsButtonSet(_currButtonStates, CGMouseButton.kCGMouseButtonRight))
                buttons |= 2;
            else if (IsButtonSet(_currButtonStates, CGMouseButton.kCGMouseButtonCenter))
                buttons |= 4;

            CGEventSetDoubleValueField(_mouseEvent, CGEventField.mouseEventPressure, _pressure ?? 1.0);

            CGEventSetIntegerValueField(_mouseEvent, CGEventField.mouseEventSubtype, (long)CGMouseEventSubtype.TabletPoint);
            // The fields of tabletEvent can only be set after the subtype is defined.
            CGEventSetIntegerValueField(_mouseEvent, CGEventField.tabletEventPointButtons, buttons);
            CGEventSetIntegerValueField(_mouseEvent, CGEventField.tabletEventDeviceID, DeviceId);
            CGEventSetDoubleValueField(_mouseEvent, CGEventField.tabletEventPointPressure, _pressure ?? 1.0);

            if (_tilt != null)
            {
                CGEventSetDoubleValueField(_mouseEvent, CGEventField.tabletEventTiltX, _tilt.Value.X / 90.0);
                // TiltY is inverted on MacOS
                // see https://github.com/chromium/chromium/blob/62f1a92b04c1172431a64d581be9e64742c81576/content/browser/renderer_host/input/web_input_event_builders_mac.mm#L431
                CGEventSetDoubleValueField(_mouseEvent, CGEventField.tabletEventTiltY, -_tilt.Value.Y / 90.0);
            }

            // This uses an undocumented flag that tells the system to automatically include the event flags in the event.
            // It's a better approach than fetching the active event flags ourselves, as doing so can introduce a race condition
            // (e.g., if a modifier key is released after we check its status but before the event is posted).
            // However, this flag has not effects for synthetic keyboard events, so we manually set the flags if there are modifiers bindings.

            if (_keyboard.getCurrentFlags() != 0)
                CGEventSetFlags(_mouseEvent, _keyboard.getCurrentFlags());
            else
                CGEventSetFlags(_mouseEvent, ~0U);
        }

        private void PostEvent()
        {
            CGEventPost(CGEventTapLocation.kCGHIDEventTap, _mouseEvent);
            // Fields in a CGEvent are stored in a union determined by the event type,
            // and they cannot be safely reused.
            CFRelease(_mouseEvent);
            _mouseEvent = CGEventCreate(_eventSource);
        }

        ~MacOSVirtualMouse()
        {
            if (_eventSource != IntPtr.Zero)
            {
                CFRelease(_eventSource);
            }
            if (_mouseEvent != IntPtr.Zero)
            {
                CFRelease(_mouseEvent);
            }
        }
    }
}
