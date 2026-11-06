using System;
using OpenTabletDriver.Native.Windows;
using OpenTabletDriver.Native.Windows.Input;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Platform.Pointer;

namespace OpenTabletDriver.Desktop.Interop.Input
{
    using static OpenTabletDriver.Native.Windows.Windows;

    [PluginIgnore]
    public abstract class WindowsVirtualMouse : IMouseButtonHandler, IMouseScrollHandler, ISynchronousPointer, IMouseDoubleClickHandler
    {
        private bool _dirty;

        /// <inheritdoc />
        public int DoubleClickMoveTolerance { get; set; } = DoubleClickState.DefaultMoveTolerance;

        protected INPUT[] inputs =
        [
            new INPUT
            {
                type = INPUT_TYPE.MOUSE_INPUT,
                U = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        time = 0,
                        dwExtraInfo = UIntPtr.Zero
                    }
                }
            },
        ];

        protected void MouseEvent(MOUSEEVENTF arg, uint dwData = 0)
        {
            SetDirty();

            inputs[0].U.mi.dwFlags |= arg;
            inputs[0].U.mi.mouseData |= dwData;
        }

        public void MouseDown(MouseButton button)
        {
            switch (button)
            {
                case MouseButton.Left:
                    MouseEvent(MOUSEEVENTF.LEFTDOWN);
                    return;
                case MouseButton.Middle:
                    MouseEvent(MOUSEEVENTF.MIDDLEDOWN);
                    return;
                case MouseButton.Right:
                    MouseEvent(MOUSEEVENTF.RIGHTDOWN);
                    return;
                case MouseButton.Backward:
                    MouseEvent(MOUSEEVENTF.XDOWN, (uint)XBUTTON.XBUTTON1);
                    return;
                case MouseButton.Forward:
                    MouseEvent(MOUSEEVENTF.XDOWN, (uint)XBUTTON.XBUTTON2);
                    return;
            }
        }

        public void MouseUp(MouseButton button)
        {
            switch (button)
            {
                case MouseButton.Left:
                    MouseEvent(MOUSEEVENTF.LEFTUP);
                    return;
                case MouseButton.Middle:
                    MouseEvent(MOUSEEVENTF.MIDDLEUP);
                    return;
                case MouseButton.Right:
                    MouseEvent(MOUSEEVENTF.RIGHTUP);
                    return;
                case MouseButton.Backward:
                    MouseEvent(MOUSEEVENTF.XUP, (uint)XBUTTON.XBUTTON1);
                    return;
                case MouseButton.Forward:
                    MouseEvent(MOUSEEVENTF.XUP, (uint)XBUTTON.XBUTTON2);
                    return;
            }
        }

        public void ScrollVertically(int amount) => MouseEvent(MOUSEEVENTF.WHEEL, (uint)amount);

        public void ScrollHorizontally(int amount) => MouseEvent(MOUSEEVENTF.HWHEEL, (uint)amount);

        protected void SetDirty()
        {
            _dirty = true;
        }

        public void Flush()
        {
            if (_dirty)
            {
                var result = SendInput(1, inputs, INPUT.Size);
                if (result != 1)
                    Log.Write("WindowsMouse", $"SendInput failed: {result}/1 events inserted", LogLevel.Error);
                inputs[0].U.mi.dwFlags = 0;
                inputs[0].U.mi.mouseData = 0;
                inputs[0].U.mi.dx = 0;
                inputs[0].U.mi.dy = 0;
                _dirty = false;
            }
        }

        public void Reset()
        {
        }

        /// <inheritdoc />
        /// <remarks>Best-effort on Windows: two left-click pairs at the current cursor. Windows has no
        /// CGEvent-style click-count field; apps that do their own hit-testing still see two clicks.</remarks>
        public void PostDoubleClick()
        {
            Flush();
            void Send(MOUSEEVENTF flag)
            {
                inputs[0].U.mi.dwFlags = flag;
                inputs[0].U.mi.mouseData = 0;
                inputs[0].U.mi.dx = 0;
                inputs[0].U.mi.dy = 0;
                var result = SendInput(1, inputs, INPUT.Size);
                if (result != 1)
                    Log.Write("WindowsMouse", $"SendInput failed during double-click: {result}/1", LogLevel.Error);
                inputs[0].U.mi.dwFlags = 0;
            }
            Send(MOUSEEVENTF.LEFTDOWN);
            Send(MOUSEEVENTF.LEFTUP);
            Send(MOUSEEVENTF.LEFTDOWN);
            Send(MOUSEEVENTF.LEFTUP);
        }
    }
}
