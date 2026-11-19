using System;
using System.Globalization;

namespace OpenTabletDriver.Devices.HidSharpBackend
{
    /// <summary>
    /// Tablet App for Mac (#118): on macOS the vendored HidSharpCore gives each device a path of the form
    /// "IOService:/...#1A2B3C": the I/O Registry path, '#', and the device's registry entry ID in hex.
    /// The ID is what tells two devices with the same registry path apart (Bluetooth HID). Code that opens
    /// a device itself through IOKit (MacHidFeature) splits the two parts here and opens by the ID.
    /// Must match HidSharp.Platform.MacOS.MacDeviceKey.ToDevicePath.
    /// </summary>
    public static class MacRegistryPath
    {
        public const char IdSeparator = '#';

        /// <summary>
        /// Splits a device path into the registry path and the entry ID. Returns false (path = the whole
        /// string, ID = 0) when there is no valid "#hex" ending: then the path is a plain registry path.
        /// </summary>
        public static bool TrySplit(string devicePath, out string registryPath, out ulong entryId)
        {
            registryPath = devicePath ?? string.Empty;
            entryId = 0;
            if (string.IsNullOrEmpty(devicePath)) return false;

            int i = devicePath.LastIndexOf(IdSeparator);
            if (i <= 0 || i == devicePath.Length - 1) return false;

            var hex = devicePath.Substring(i + 1);
            if (hex.Length > 16) return false;
            foreach (var c in hex)
                if (!Uri.IsHexDigit(c)) return false;

            if (!ulong.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var id) || id == 0)
                return false;

            registryPath = devicePath.Substring(0, i);
            entryId = id;
            return true;
        }
    }
}
