using System.Collections.Generic;

namespace OpenTabletDriver.Desktop.Interop.Input
{
    /// <summary>Tablet App for Mac (#89): the tablet proximity events a tool change posts on macOS.</summary>
    public static class MacOSProximity
    {
        /// <summary>The events, in order, when the pen end in use changes from <paramref name="previous"/> (null:
        /// nothing seen yet) to <paramref name="eraser"/>: the old end leaves, then the new end enters. A real
        /// Wacom pen does this when it is turned over; GTK apps such as GIMP only switch to the eraser's tool
        /// after the pen has left.</summary>
        public static IReadOnlyList<(bool Eraser, bool Enter)> ToolChange(bool? previous, bool eraser)
        {
            if (previous == eraser)
                return System.Array.Empty<(bool, bool)>();
            return previous.HasValue
                ? new[] { (previous.Value, false), (eraser, true) }
                : new[] { (eraser, true) };
        }
    }
}
