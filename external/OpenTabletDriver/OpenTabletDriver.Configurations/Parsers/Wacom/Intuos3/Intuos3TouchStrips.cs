namespace OpenTabletDriver.Configurations.Parsers.Wacom.Intuos3
{
    /// <summary>
    /// Touch strips on Intuos3, Cintiq 21UX (DTZ-2100), Cintiq 12WX and Cintiq 22HD pad reports.
    /// <para/>Each strip is 13 bits: strip 1 (left) in bytes 1–2, strip 2 (right) in bytes 3–4, as the
    /// Linux Wacom driver reads them (wacom_wac.c, wacom_intuos_pad). The finger lights one bit, so a
    /// strip reading is that bit's position, 0 at the top to 12 at the bottom (the X.org Wacom driver
    /// takes log2 of the value and counts a falling position as "up": wcmCommon.c, getScrollDelta with
    /// AXIS_BITWISE | AXIS_INVERT). No finger reads as 0 and gives null.
    /// <para/>Strips are reported as absolute wheels with AbsoluteWheelMax 12, so they get the same
    /// bindings as a touch ring.
    /// <para/>Modified for Tablet App for Mac (Version 16): not part of OpenTabletDriver 0.6.8-rc1.
    /// </summary>
    public static class Intuos3TouchStrips
    {
        /// <summary>The highest strip position (13 positions, 0–12).</summary>
        public const uint StripMax = 12;

        public static uint?[] Read(byte[] report) =>
        [
            Position(((report[1] & 0x1F) << 8) | report[2]),
            Position(((report[3] & 0x1F) << 8) | report[4]),
        ];

        /// <summary>The position of the highest lit bit, or null when no bit is lit.</summary>
        public static uint? Position(int bits)
        {
            if (bits <= 0) return null;
            uint position = 0;
            while ((bits >>= 1) != 0) position++;
            return position > StripMax ? StripMax : position;
        }
    }
}
