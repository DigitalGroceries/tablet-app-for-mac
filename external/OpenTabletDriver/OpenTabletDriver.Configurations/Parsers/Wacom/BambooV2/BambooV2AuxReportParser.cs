using System.Diagnostics.CodeAnalysis;
using OpenTabletDriver.Configurations.Parsers.Wacom.IntuosV2;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.Configurations.Parsers.Wacom.BambooV2
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public class BambooV2AuxReportParser : IReportParser<IDeviceReport>
    {
        public virtual IDeviceReport Parse(byte[] data)
        {
            return data[0] switch
            {
                // Tablet App for Mac V28 (#77): the 20-byte 0x02 report of a Bamboo Pen & Touch is keys + fingers.
                0x02 when data.Length == BambooV2TouchReport.ReportLength => new BambooV2TouchReport(data),
                0x02 => new IntuosV2AuxReport(data),
                _ => new DeviceReport(data),
            };
        }
    }
}
