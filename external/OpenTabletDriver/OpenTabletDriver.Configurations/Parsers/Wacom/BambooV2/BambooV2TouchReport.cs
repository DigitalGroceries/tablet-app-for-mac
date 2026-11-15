using System.Numerics;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin.Tablet.Touch;

namespace OpenTabletDriver.Configurations.Parsers.Wacom.BambooV2
{
    /// <summary>
    /// Tablet App for Mac, Version 28 (TASKS #77, Daniel's CTH-461): on a Bamboo Pen &amp; Touch (BAMBOO_PT, e.g.
    /// CTH-460/461/661) the 20-byte report 0x02 carries the four ExpressKeys AND up to two fingers. Format as in
    /// Linux drivers/hid/wacom_wac.c <c>wacom_bpt_touch</c>: keys = data[1] bits 0-3; for finger i (0, 1) the
    /// offset is 8*i when data[1] bit 7 is set, else 9*i; the finger touches when data[offset+3] bit 7 is set;
    /// x = big-endian data[offset+3..4] &amp; 0x7ff, y = data[offset+5..6] &amp; 0x7ff.
    /// Before this, the report was read as an Intuos aux report, whose touch-ring bytes are this report's finger
    /// bytes, so a finger could send false ring and ring-button events. This report has no wheel at all.
    /// </summary>
    public struct BambooV2TouchReport : IAuxReport, ITouchReport
    {
        public const int ReportLength = 20;
        public const int MaxPoints = 2;

        public BambooV2TouchReport(byte[] report)
        {
            Raw = report;
            var keys = report[1];
            AuxButtons = new[] { keys.IsBitSet(0), keys.IsBitSet(1), keys.IsBitSet(2), keys.IsBitSet(3) };
            Touches = new TouchPoint?[MaxPoints];
            var step = keys.IsBitSet(7) ? 8 : 9;
            for (var i = 0; i < MaxPoints; i++)
            {
                var offset = step * i;
                if (offset + 6 >= report.Length || (report[offset + 3] & 0x80) == 0) continue;
                var x = ((report[offset + 3] << 8) | report[offset + 4]) & 0x7ff;
                var y = ((report[offset + 5] << 8) | report[offset + 6]) & 0x7ff;
                Touches[i] = new TouchPoint { TouchID = (byte)i, Position = new Vector2(x, y) };
            }
        }

        public byte[] Raw { set; get; }
        public bool[] AuxButtons { set; get; }
        public TouchPoint?[] Touches { set; get; }
    }
}
