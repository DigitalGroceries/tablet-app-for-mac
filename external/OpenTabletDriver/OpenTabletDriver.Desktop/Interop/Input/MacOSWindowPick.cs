using System;
using System.Collections.Generic;
using System.Numerics;

namespace OpenTabletDriver.Desktop.Interop.Input
{
    /// <summary>
    /// Version 25: rules for choosing the window under a pen tap (front to back), used to activate its app
    /// on macOS 27, where a posted click raises a window without activating its app (same symptom as
    /// RustDesk discussion #15583). Kept free of native calls so it can be unit tested.
    /// </summary>
    public static class MacOSWindowPick
    {
        public const string DockBundleId = "com.apple.dock";
        public const string WindowServerName = "Window Server";

        public readonly record struct Window(int OwnerPid, string? OwnerName, int Layer, float X, float Y, float Width, float Height, float Alpha = 1f)
        {
            public bool Contains(Vector2 p) => Width > 0 && Height > 0 && p.X >= X && p.Y >= Y && p.X < X + Width && p.Y < Y + Height;
        }

        /// <summary>
        /// The front-most window whose bounds contain <paramref name="point"/>. Skips the Dock's full-screen
        /// overlay (a Dock-owned window that covers the whole display under the point), fully transparent
        /// windows and windows with no size. The Dock bar itself is kept (tapping an icon there activates
        /// nothing of ours). Does NOT skip other layers (panels, popovers) or desktop elements: the desktop
        /// window is Finder's. With no <paramref name="displays"/>, every Dock-owned window is skipped.
        /// A Window Server window that covers the display under the point is skipped too (only when displays are known).
        /// </summary>
        public static Window? Pick(IReadOnlyList<Window> frontToBack, Vector2 point,
            IReadOnlyList<MacOSScreenEdge.Rect>? displays = null)
        {
            foreach (var w in frontToBack)
            {
                if (!w.Contains(point)) continue;
                if (w.Alpha <= 0) continue;
                if (string.Equals(w.OwnerName, "Dock", StringComparison.Ordinal) && IsOverlay(w, point, displays)) continue;
                // #73: on some macOS 27.2 Macs a Window Server window covers the whole display, so every tap
                // picked Window Server and no app was activated. Skip it like the Dock overlay. Smaller
                // Window Server windows (menu bar, cursor) still stop the search, so nothing behind them is activated.
                if (string.Equals(w.OwnerName, WindowServerName, StringComparison.Ordinal) && CoversDisplay(w, point, displays)) continue;
                return w;
            }
            return null;
        }

        private static bool CoversDisplay(Window w, Vector2 point, IReadOnlyList<MacOSScreenEdge.Rect>? displays) =>
            displays is { Count: > 0 } && IsOverlay(w, point, displays);

        private static bool IsOverlay(Window w, Vector2 point, IReadOnlyList<MacOSScreenEdge.Rect>? displays)
        {
            if (displays is not { Count: > 0 }) return true;
            foreach (var d in displays)
            {
                var inside = point.X >= d.Position.X && point.Y >= d.Position.Y
                    && point.X <= d.Position.X + d.Size.X && point.Y <= d.Position.Y + d.Size.Y;
                if (!inside) continue;
                return w.X <= d.Position.X + 1 && w.Y <= d.Position.Y + 1
                    && w.X + w.Width >= d.Position.X + d.Size.X - 1 && w.Y + w.Height >= d.Position.Y + d.Size.Y - 1;
            }
            return true;
        }
    }
}
