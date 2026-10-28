using System.Diagnostics.CodeAnalysis;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.Configurations.Parsers.Wacom.IntuosBluetooth
{
    /// <summary>
    /// Tablet App for Mac: Wacom Intuos BT (CTL-4100WL, CTL-6100WL) over Bluetooth.
    /// Byte layout from the Linux Wacom driver (wacom_intuos_pro2_bt_pen, wacom_intuos_gen3_bt_pad):
    /// report 0x80/0x81, 4 pen frames of 8 bytes from byte 1, ExpressKeys in byte 44.
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

            int newest = -1;
            for (int i = 0; i < FrameCount; i++)
                if ((data[1 + i * FrameLength] & 0x80) != 0)
                    newest = i;

            if (newest < 0)
                return new IntuosBluetoothAuxReport(data);

            // A valid frame without the proximity bit is the pen leaving (Linux: wacom_exit_report).
            // OutOfRangeReport releases held tip and pen-button bindings; the pad keys come again in the next report.
            if ((data[1 + newest * FrameLength] & 0x40) == 0)
                return new OutOfRangeReport(data);

            return new IntuosBluetoothReport(data, 1 + newest * FrameLength);
        }
    }
}
