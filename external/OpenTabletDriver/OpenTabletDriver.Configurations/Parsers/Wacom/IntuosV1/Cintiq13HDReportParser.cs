using System.Diagnostics.CodeAnalysis;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.Configurations.Parsers.Wacom.IntuosV1
{
    /// <summary>
    /// Cintiq 13HD (DTK-1300) and Cintiq 13HD Touch (DTH-1300): the <see cref="IntuosV1ReportParser"/>
    /// reports, with the centre (Home) button of the Rocker Ring added as a ninth ExpressKey.
    /// The Linux Wacom driver reads this pad as nine buttons, (byte 4 &lt;&lt; 1) | (byte 3 &amp; 1)
    /// (wacom_wac.c, wacom_intuos_pad, WACOM_13HD). The centre button is added last, so the first
    /// eight keys keep their numbers.
    /// <para/>Modified for Tablet App for Mac (Version 16): not part of OpenTabletDriver 0.6.8-rc1.
    /// </summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public class Cintiq13HDReportParser : IntuosV1ReportParser
    {
        public override IDeviceReport Parse(byte[] report)
        {
            return report[0] == 0x03 ? new Cintiq13HDAuxReport(report) : base.Parse(report);
        }
    }

    /// <summary>The Cintiq 13HD pad report: <see cref="IntuosV1AuxReport"/>'s eight keys, then the
    /// centre button.</summary>
    public struct Cintiq13HDAuxReport : IAuxReport
    {
        public Cintiq13HDAuxReport(byte[] report)
        {
            Raw = report;
            var keys = new IntuosV1AuxReport(report).AuxButtons;
            var all = new bool[keys.Length + 1];
            keys.CopyTo(all, 0);
            all[keys.Length] = report[3].IsBitSet(0);
            AuxButtons = all;
        }

        public byte[] Raw { set; get; }
        public bool[] AuxButtons { set; get; }
    }
}
