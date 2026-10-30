using System.Diagnostics.CodeAnalysis;
using OpenTabletDriver.Configurations.Parsers.Wacom.Intuos3;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin.Tablet.Wheel;

namespace OpenTabletDriver.Configurations.Parsers.Wacom.CintiqV1
{
    /// <summary>
    /// Cintiq 21UX (DTK-2100), Cintiq 22HD (DTK-2200) and Cintiq 22HD Touch (DTH-2200): the
    /// <see cref="CintiqV1ReportParser"/> reports plus the two touch strips, read the way the Linux
    /// Wacom driver reads them for WACOM_21UX2 and WACOM_22HD (wacom_wac.c, wacom_intuos_pad) —
    /// see <see cref="Intuos3TouchStrips"/>.
    /// <para/>Modified for Tablet App for Mac (Version 16): not part of OpenTabletDriver 0.6.8-rc1.
    /// </summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public class CintiqV1TouchStripReportParser : CintiqV1ReportParser
    {
        public override IDeviceReport Parse(byte[] report)
        {
            return report[0] == 0x0C ? new CintiqV1TouchStripAuxReport(report) : base.Parse(report);
        }
    }

    /// <summary>The pad report of these Cintiqs: the same buttons as <see cref="CintiqV1AuxReport"/> plus
    /// the left and right touch strips.</summary>
    public struct CintiqV1TouchStripAuxReport : IAuxReport, IAbsoluteWheelReport
    {
        public CintiqV1TouchStripAuxReport(byte[] report)
        {
            Raw = report;
            AuxButtons = new CintiqV1AuxReport(report).AuxButtons;
            AnalogPositions = Intuos3TouchStrips.Read(report);
        }

        public byte[] Raw { set; get; }
        public bool[] AuxButtons { set; get; }
        public uint?[] AnalogPositions { set; get; }
    }
}
