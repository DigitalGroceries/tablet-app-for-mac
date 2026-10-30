using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin.Tablet.Wheel;

namespace OpenTabletDriver.Configurations.Parsers.Wacom.Intuos3
{
    public struct Intuos3AuxReport : IAuxReport, IAbsoluteWheelReport
    {
        public Intuos3AuxReport(byte[] report)
        {
            Raw = report;
            AuxButtons =
            [
                report[5].IsBitSet(0),
                report[5].IsBitSet(1),
                report[5].IsBitSet(2),
                report[5].IsBitSet(3),
                report[6].IsBitSet(0),
                report[6].IsBitSet(1),
                report[6].IsBitSet(2),
                report[6].IsBitSet(3),
            ];
            // Tablet App for Mac (Version 16): touch strips, see Intuos3TouchStrips.
            AnalogPositions = Intuos3TouchStrips.Read(report);
        }

        public bool[] AuxButtons { set; get; }
        public byte[] Raw { set; get; }
        public uint?[] AnalogPositions { set; get; }
    }
}
