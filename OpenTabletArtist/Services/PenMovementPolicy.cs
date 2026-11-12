using System;

namespace OpenTabletArtist.Services;

/// <summary>TASKS #55: Position smoothing on the Pen page runs 0-10 (the driver value runs 0-0.25; 10 is the old 0.25).
/// The Movement choice (Normal (Absolute) / Relative (no speed-up)) is always visible and a saved Relative stays
/// Relative (Zak / Claude, 12 Nov 18:54: no hiding, no fallback).</summary>
public static class PenMovementPolicy
{
    /// <summary>The slider on the Pen page runs 0-10; the driver value runs 0-0.25. 10 is the old 0.25.</summary>
    public const double SmoothingUiMax = 10;

    public static double SmoothingToUi(double internalValue, double internalMax) =>
        Math.Clamp(Math.Round(internalValue / internalMax * SmoothingUiMax, 1), 0, SmoothingUiMax);

    public static double SmoothingFromUi(double ui, double internalMax) =>
        Math.Clamp(ui, 0, SmoothingUiMax) / SmoothingUiMax * internalMax;
}
