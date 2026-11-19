using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.Configurations.Parsers.Wacom.IntuosBluetooth
{
    /// <summary>
    /// Tablet App for Mac: Wacom Intuos BT (CTL-4100WL, CTL-6100WL) over Bluetooth.
    /// Byte layout from the Linux Wacom driver (wacom_intuos_pro2_bt_pen, wacom_intuos_gen3_bt_pad):
    /// report 0x80/0x81, 4 pen frames of 8 bytes from byte 1, ExpressKeys in byte 44.
    /// #120: all valid frames are passed on, oldest first (a MultiFrameReport when there are several).
    /// </summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public class IntuosBluetoothReportParser : IReportParser<IDeviceReport>
    {
        public const int FrameLength = 8;
        public const int FrameCount = 4;
        public const int ButtonsByte = 44;

        public virtual IDeviceReport Parse(byte[] data)
        {
            if (data.Length <= ButtonsByte || (data[0] != 0x80 && data[0] != 0x81))
                return new DeviceReport(data);

            // #120: every valid frame, oldest first (Linux: wacom_intuos_pro2_bt_pen sends each frame). Before,
            // only the newest was kept, so up to 3 of every 4 pen samples were dropped over Bluetooth.
            var frames = new List<IDeviceReport>(FrameCount);
            for (int i = 0; i < FrameCount; i++)
            {
                int at = 1 + i * FrameLength;
                if ((data[at] & 0x80) == 0)
                    continue;

                // A valid frame without the proximity bit is the pen leaving (Linux: wacom_exit_report).
                // OutOfRangeReport releases held tip and pen-button bindings; the pad keys come again in the next
                // report. Several "leaving" frames in a row count once.
                if ((data[at] & 0x40) == 0)
                {
                    if (frames.Count == 0 || frames[^1] is not OutOfRangeReport)
                        frames.Add(new OutOfRangeReport(data));
                    continue;
                }

                frames.Add(new IntuosBluetoothReport(data, at));
            }

            return frames.Count switch
            {
                0 => new IntuosBluetoothAuxReport(data),
                1 => frames[0],
                _ => new MultiFrameReport(data, frames.ToArray())
            };
        }
    }
}
