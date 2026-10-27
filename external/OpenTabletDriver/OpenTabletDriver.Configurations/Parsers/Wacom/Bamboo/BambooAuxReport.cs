using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin.Tablet.Wheel;

namespace OpenTabletDriver.Configurations.Parsers.Wacom.Bamboo
{
    public struct BambooAuxReport : IAuxReport, IAbsoluteWheelReport
    {
        public BambooAuxReport(byte[] report)
        {
            Raw = report;

            var auxByte = report[7];
            AuxButtons =
            [
                auxByte.IsBitSet(3),
                auxByte.IsBitSet(4),
                auxByte.IsBitSet(5),
                auxByte.IsBitSet(6),
            ];

            // Tablet App for Mac (Version 16): the Bamboo Fun touch ring (CTE-450, CTE-650). The Linux
            // Wacom driver reads it as (byte 8 & 0x7F), touched whenever byte 8 isn't 0 (wacom_wac.c,
            // WACOM_MO), so don't rely on bit 7 alone.
            AnalogPositions = [report[8] != 0 ? (uint)(report[8] & 0x7f) : null];
        }

        public byte[] Raw { set; get; }
        public bool[] AuxButtons { set; get; }
        public uint?[] AnalogPositions { set; get; }
    }
}
