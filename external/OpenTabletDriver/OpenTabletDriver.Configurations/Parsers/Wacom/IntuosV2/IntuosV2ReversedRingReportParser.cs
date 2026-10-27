using System.Diagnostics.CodeAnalysis;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.Configurations.Parsers.Wacom.IntuosV2
{
    /// <summary>
    /// Intuos Pro (2017) PTH-460, PTH-660 and PTH-860: the Touch Ring counts anticlockwise and its
    /// zero is at the top, the opposite of every other Wacom ring (zero at the left, counting
    /// clockwise). This turns the reading into the usual form, the same correction the Linux Wacom
    /// driver makes for these models (wacom_wac.c: WACOM_HID_WD_TOUCHRING for USB products
    /// 0x357, 0x358 and 0x392, and wacom_intuos_pro2_bt_pad for Bluetooth): mirror the value, then
    /// turn it on by 3/16 of a circle.
    /// <para/>Modified for Tablet App for Mac (Version 16): not part of OpenTabletDriver 0.6.8-rc1.
    /// </summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public class IntuosV2ReversedRingReportParser : IntuosV2ReportParser
    {
        /// <summary>The ring's highest reading (72 positions, 0–71).</summary>
        public const uint RingMax = 71;

        private const uint RingSteps = RingMax + 1;

        /// <summary>3/16 of a circle in ring steps, rounded down as the Linux driver does (3 × 72 / 16).</summary>
        public const uint ZeroOffset = 3 * RingSteps / 16;

        public override IDeviceReport Parse(byte[] data)
        {
            var report = base.Parse(data);
            if (report is IntuosV2AuxReport aux)
            {
                aux.AnalogPositions = Normalise(aux.AnalogPositions);
                return aux;
            }
            return report;
        }

        /// <summary>Mirror each touched position and move its zero to the left. Untouched (null) and
        /// out-of-range readings are passed on unchanged.</summary>
        public static uint?[] Normalise(uint?[] positions)
        {
            var result = new uint?[positions.Length];
            for (int i = 0; i < positions.Length; i++)
                result[i] = positions[i] is uint p && p <= RingMax
                    ? (RingMax - p + ZeroOffset) % RingSteps
                    : positions[i];
            return result;
        }
    }

    /// <summary>
    /// <see cref="IntuosV2ReversedRingReportParser"/> for the Wacom driver's interface, whose reports
    /// carry one extra leading byte (same as <see cref="WacomDriverIntuosV2ReportParser"/>).
    /// <para/>Modified for Tablet App for Mac (Version 16).
    /// </summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public class WacomDriverIntuosV2ReversedRingReportParser : IntuosV2ReversedRingReportParser
    {
        public override IDeviceReport Parse(byte[] data)
        {
            return base.Parse(data[1..^0]);
        }
    }
}
