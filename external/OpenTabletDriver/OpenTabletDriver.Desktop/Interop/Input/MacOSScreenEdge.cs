using System;
using System.Collections.Generic;
using System.Numerics;

namespace OpenTabletDriver.Desktop.Interop.Input
{
    /// <summary>
    /// Version 25 (OTD issue #4726): macOS treats a display as [min, max - 1/64] while OTD maps the
    /// tablet to [min, max]. A pointer posted at exactly the bottom (or right) edge is outside the display,
    /// so the Dock and hot corners don't always trigger. Clamp to the display that contains the point,
    /// inset by 1/64 px at the far edges.
    /// </summary>
    public static class MacOSScreenEdge
    {
        public const float Inset = 1f / 64f;

        public readonly record struct Rect(Vector2 Position, Vector2 Size);

        /// <summary>Clamps <paramref name="pos"/> into the display that contains it (or the nearest one),
        /// falling back to <paramref name="virtualScreen"/> when there are no displays.</summary>
        public static Vector2 Clamp(Vector2 pos, IReadOnlyList<Rect>? displays, Rect virtualScreen)
        {
            var target = virtualScreen;
            if (displays is { Count: > 0 })
            {
                var best = float.MaxValue;
                foreach (var d in displays)
                {
                    var dist = DistanceSquared(pos, d);
                    if (dist < best) { best = dist; target = d; }
                    if (dist == 0) break;
                }
            }
            return ClampTo(pos, target);
        }

        public static Vector2 ClampTo(Vector2 pos, Rect r)
        {
            var min = r.Position;
            var max = r.Position + r.Size - new Vector2(Inset, Inset);
            if (max.X < min.X) max.X = min.X;
            if (max.Y < min.Y) max.Y = min.Y;
            return new Vector2(Math.Clamp(pos.X, min.X, max.X), Math.Clamp(pos.Y, min.Y, max.Y));
        }

        private static float DistanceSquared(Vector2 p, Rect r)
        {
            // A point on the far edge (max) belongs to this display, as OTD maps to [min, max].
            var dx = p.X < r.Position.X ? r.Position.X - p.X : p.X > r.Position.X + r.Size.X ? p.X - (r.Position.X + r.Size.X) : 0;
            var dy = p.Y < r.Position.Y ? r.Position.Y - p.Y : p.Y > r.Position.Y + r.Size.Y ? p.Y - (r.Position.Y + r.Size.Y) : 0;
            return dx * dx + dy * dy;
        }
    }
}
