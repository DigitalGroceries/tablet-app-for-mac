using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.Configurations.Parsers.Wacom.IntuosBluetooth
{
    public struct IntuosBluetoothAuxReport : IAuxReport
    {
        public IntuosBluetoothAuxReport(byte[] report)
        {
            Raw = report;
            AuxButtons = ReadButtons(report);
        }

        public static bool[] ReadButtons(byte[] report)
        {
            var keys = report[IntuosBluetoothReportParser.ButtonsByte];
            return
            [
                keys.IsBitSet(0),
                keys.IsBitSet(1),
                keys.IsBitSet(2),
                keys.IsBitSet(3),
            ];
        }

        public byte[] Raw { set; get; }
        public bool[] AuxButtons { set; get; }
    }
}
