using System;
using System.Globalization;

namespace OpenTabletArtist.Domain;

/// <summary>
/// V29 #140 (Zak 19 Nov 18:37: "any app slider that doesnt have a manual value editing should receive one so instead of dragging
/// and hoping to replicate the exact possition the user just uses the keyboard to type the value they need"). Reads what a user
/// typed into a slider's number box: "40", "40%", "40,5" or "40.5". The value is clamped to the slider's range and rounded to the
/// box's number of decimals. Anything that isn't a number returns false, and the box then shows the real value again.
/// </summary>
public static class TypedValue
{
    public static bool TryParse(string? text, double min, double max, int decimals, out double value)
    {
        value = min;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim().TrimEnd('%').Trim().Replace(',', '.');
        if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) || double.IsNaN(d) || double.IsInfinity(d))
            return false;
        value = Math.Round(Math.Clamp(d, min, max), Math.Max(0, decimals), MidpointRounding.AwayFromZero);
        return true;
    }

    /// <summary>A value as its box shows it (invariant culture, the box's number of decimals).</summary>
    public static string Format(double value, int decimals) =>
        value.ToString("F" + Math.Max(0, decimals), CultureInfo.InvariantCulture);
}

/// <summary>
/// V29 #130 (Zak 19 Nov 18:17: "Tip feel needs manual value field for soft and firm from 1 to 100"): the Tip Feel and Eraser Feel
/// sliders shown as 1 (softest) to 100 (firmest). 50 is the middle, the straight pressure curve; the slider's own range is
/// -0.9 (Soft) to +0.9 (Firm), so each half of the scale maps onto its half of the slider.
/// </summary>
public static class FeelScale
{
    public const int Min = 1, Middle = 50, Max = 100;
    public const double SliderEnd = 0.9;

    /// <summary>The 1-100 number for a Tip Feel slider value (-0.9 to 0.9).</summary>
    public static int ToDisplay(double feel)
    {
        var f = Math.Clamp(feel, -SliderEnd, SliderEnd);
        double v = f <= 0 ? Min + (f + SliderEnd) * (Middle - Min) / SliderEnd : Middle + f * (Max - Middle) / SliderEnd;
        return (int)Math.Round(v, MidpointRounding.AwayFromZero);
    }

    /// <summary>The Tip Feel slider value for a 1-100 number.</summary>
    public static double FromDisplay(int value)
    {
        int v = Math.Clamp(value, Min, Max);
        return v <= Middle ? -SliderEnd + (v - Min) * SliderEnd / (Middle - Min) : (v - Middle) * SliderEnd / (Max - Middle);
    }
}
