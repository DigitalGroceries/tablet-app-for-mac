using System;
using System.Linq;
using System.Numerics;
using OpenTabletDriver.Native.OSX;
using OpenTabletDriver.Native.OSX.Input;
using OpenTabletDriver.Plugin.Platform.Pointer;

namespace OpenTabletDriver.Desktop.Interop.Input.Absolute
{
    using static OSX;

    public class MacOSAbsolutePointer : MacOSVirtualMouse, IAbsolutePointer
    {
        private Vector2 _offset;
        private Vector2? _lastPos;
        private Vector2? _delta;

        public MacOSAbsolutePointer()
        {
            var virtualScreen = DesktopInterop.VirtualScreen
                                ?? throw new InvalidOperationException("Could not get virtual screen");
            var primary = virtualScreen.Displays.First();
            _offset = primary.Position;
            // Display bounds in macOS global coordinates (the primary display's top left is 0,0).
            _displays = virtualScreen.Displays
                .Select(d => new MacOSScreenEdge.Rect(d.Position - _offset, new Vector2(d.Width, d.Height)))
                .ToArray();
            _virtualScreen = new MacOSScreenEdge.Rect(-_offset, new Vector2(virtualScreen.Width, virtualScreen.Height));
        }

        private readonly MacOSScreenEdge.Rect[] _displays;
        private readonly MacOSScreenEdge.Rect _virtualScreen;

        protected override System.Collections.Generic.IReadOnlyList<MacOSScreenEdge.Rect>? DisplayRects => _displays;

        protected override bool UseClickJitterGate => true;

        public void SetPosition(Vector2 pos)
        {
            // Version 25 (OTD #4726): keep the pointer 1/64 px inside the display so the Dock and hot corners trigger.
            var newPos = MacOSScreenEdge.Clamp(pos - _offset, _displays, _virtualScreen);
            _delta = newPos - _lastPos;
            _lastPos = newPos;

            QueuePendingPosition(newPos.X, newPos.Y);
        }

        protected override void SetPendingPosition(IntPtr mouseEvent, float x, float y)
        {
            CGEventSetLocation(mouseEvent, new CGPoint(x, y));
            if (_delta is not null)
            {
                CGEventSetDoubleValueField(mouseEvent, CGEventField.mouseEventDeltaX, _delta.Value.X);
                CGEventSetDoubleValueField(mouseEvent, CGEventField.mouseEventDeltaY, _delta.Value.Y);
            }
        }

        protected override void QueuePendingPositionFromSystem()
        {
            var eventRef = CGEventCreate(IntPtr.Zero);
            var pos = CGEventGetLocation(eventRef);
            CFRelease(eventRef);
            QueuePendingPosition((float)pos.x, (float)pos.y);
        }

        protected override void ResetPendingPosition(IntPtr mouseEvent)
        {
            _lastPos = null;
            _delta = null;
        }
    }
}
