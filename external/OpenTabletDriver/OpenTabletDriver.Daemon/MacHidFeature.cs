using System;
using System.Runtime.InteropServices;

namespace OpenTabletDriver.Daemon
{
    /// <summary>
    /// Tablet App for Mac (#7, Intuos4 OLED key labels): sends feature reports straight through IOKit
    /// (<c>IOHIDDeviceSetReport</c>), for reports longer than the device's declared feature report length.
    /// HidSharp cuts such a report short to that length, which would turn the 259-byte image chunks into
    /// garbage; IOKit sends the bytes as given, like the Linux driver does. Used only when the HidSharp
    /// path can't carry the report.
    /// </summary>
    public static class MacHidFeature
    {
        private const string IOKit = "/System/Library/Frameworks/IOKit.framework/IOKit";
        private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        private const int kIOHIDReportTypeFeature = 2;

        [DllImport(IOKit)] private static extern uint IORegistryEntryFromPath(uint mainPort, string path);
        [DllImport(IOKit)] private static extern int IOObjectRelease(uint obj);
        [DllImport(IOKit)] private static extern IntPtr IOHIDDeviceCreate(IntPtr allocator, uint service);
        [DllImport(IOKit)] private static extern int IOHIDDeviceOpen(IntPtr device, uint options);
        [DllImport(IOKit)] private static extern int IOHIDDeviceClose(IntPtr device, uint options);
        [DllImport(IOKit)] private static extern int IOHIDDeviceSetReport(IntPtr device, int reportType, IntPtr reportId, byte[] report, IntPtr length);
        [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr obj);

        /// <summary>Sends every report in order through one IOKit handle. Throws with the IOKit return code
        /// when the device can't be found, opened, or refuses a report.</summary>
        public static void SendSequence(string registryPath, byte[][] reports)
        {
            var service = IORegistryEntryFromPath(0, registryPath);
            if (service == 0) throw new InvalidOperationException("Device not found in the I/O Registry.");
            try
            {
                var device = IOHIDDeviceCreate(IntPtr.Zero, service);
                if (device == IntPtr.Zero) throw new InvalidOperationException("Couldn't create a HID handle.");
                try
                {
                    var open = IOHIDDeviceOpen(device, 0);
                    if (open != 0) throw new InvalidOperationException($"Couldn't open the device (0x{open:X8}).");
                    try
                    {
                        foreach (var r in reports)
                        {
                            // The report ID goes in the first byte of the buffer as well (numbered reports).
                            var ret = IOHIDDeviceSetReport(device, kIOHIDReportTypeFeature, (IntPtr)r[0], r, (IntPtr)r.Length);
                            if (ret != 0) throw new InvalidOperationException($"Report 0x{r[0]:X2} ({r.Length} bytes) refused (0x{ret:X8}).");
                        }
                    }
                    finally { IOHIDDeviceClose(device, 0); }
                }
                finally { CFRelease(device); }
            }
            finally { IOObjectRelease(service); }
        }
    }
}
