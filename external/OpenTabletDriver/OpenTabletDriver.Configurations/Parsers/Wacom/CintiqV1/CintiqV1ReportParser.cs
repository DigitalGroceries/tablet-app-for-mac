using System;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using OpenTabletDriver.Configurations.Parsers.Wacom.IntuosV1;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.Configurations.Parsers.Wacom.CintiqV1
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public class CintiqV1ReportParser : IReportParser<IDeviceReport>
    {
        public virtual IDeviceReport Parse(byte[] report)
        {
            return report[0] switch
            {
                0x02 => GetToolReport(report),
                0x10 => GetToolReport(report),
                // Tablet App for Mac: a short 0x0C packet (e.g. DTK-2200's 5-byte interface) has no button bytes.
                0x0C when report.Length >= 10 => new CintiqV1AuxReport(report),
                _ => new DeviceReport(report)
            };
        }

        private IDeviceReport GetToolReport(byte[] report)
        {
            if (report[0] == 0x10 && report[1] == 0x20)
                return new DeviceReport(report);
            if (report[1] == 0x80)
                return new OutOfRangeReport(report);
            if (report[1].IsBitSet(1) && report[1].IsBitSet(3))
                return new IntuosV1RotationReport(report, ref _prevPressure, ref _prevTilt, ref _prevRotation, ref _prevPenButtons);
            if (report[1].IsBitSet(5))
                return new IntuosV1TabletReport(report, ref _prevPressure, ref _prevTilt, ref _prevRotation, ref _prevPenButtons);
            else if (report[1] == 0xC2)
                return new IntuosV1ToolReport(report);

            return new DeviceReport(report);
        }

        private uint _prevPressure;
        private Vector2 _prevTilt;
        private int _prevRotation;
        private bool[] _prevPenButtons = Array.Empty<bool>();
    }
}
