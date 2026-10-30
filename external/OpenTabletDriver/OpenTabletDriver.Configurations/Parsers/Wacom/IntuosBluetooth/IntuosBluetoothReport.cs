using System.Numerics;
using System.Runtime.CompilerServices;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.Configurations.Parsers.Wacom.IntuosBluetooth
{
    public struct IntuosBluetoothReport : ITabletReport, IProximityReport, IEraserReport, IAuxReport
    {
        public IntuosBluetoothReport(byte[] report, int frame)
        {
            Raw = report;
            var flags = report[frame];
            Position = new Vector2
            {
                X = Unsafe.ReadUnaligned<ushort>(ref report[frame + 1]),
                Y = Unsafe.ReadUnaligned<ushort>(ref report[frame + 3])
            };
            Pressure = Unsafe.ReadUnaligned<ushort>(ref report[frame + 5]);
            Eraser = flags.IsBitSet(4);
            PenButtons =
            [
                flags.IsBitSet(1),
                flags.IsBitSet(2),
            ];
            NearProximity = flags.IsBitSet(5);
            HoverDistance = report[frame + 7];
            AuxButtons = IntuosBluetoothAuxReport.ReadButtons(report);
        }

        public byte[] Raw { set; get; }
        public Vector2 Position { set; get; }
        public uint Pressure { set; get; }
        public bool Eraser { set; get; }
        public bool[] PenButtons { set; get; }
        public bool NearProximity { set; get; }
        public uint HoverDistance { set; get; }
        public bool[] AuxButtons { set; get; }
    }
}
