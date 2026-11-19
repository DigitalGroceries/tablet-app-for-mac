#region License
/* Copyright 2026 the Tablet App for Mac team (DigitalGroceries).
   Added to HidSharpCore 1.3.0 (vendored in external/HidSharpCore) for issue #118.

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

      http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing,
   software distributed under the License is distributed on an
   "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
   KIND, either express or implied.  See the License for the
   specific language governing permissions and limitations
   under the License. */
#endregion

using System;
using System.Globalization;

namespace HidSharp.Platform.MacOS
{
    /// <summary>
    /// Identifies one macOS device: its I/O Registry path and its registry entry ID.
    /// Bluetooth HID devices (and any IOHIDUserDevice) can share the same registry path, so the path
    /// alone made a second device look like the first one: only the first was listed, and opening by
    /// path always reached the first (#118). The registry entry ID is unique for as long as the device
    /// stays connected, so it is the key; the path stays for display and for the interface number.
    /// An ID of 0 means the ID could not be read; then the path alone is used, as before.
    /// </summary>
    readonly struct MacDeviceKey : IEquatable<MacDeviceKey>
    {
        /// <summary>Separates the registry path from the hex entry ID in <see cref="ToDevicePath"/>.</summary>
        public const char IdSeparator = '#';

        public readonly NativeMethods.io_string_t Path;
        public readonly ulong EntryId;

        public MacDeviceKey(NativeMethods.io_string_t path, ulong entryId)
        {
            Path = path;
            EntryId = entryId;
        }

        /// <summary>
        /// The device path shown to callers: "IOService:/...#1A2B3C" (path, '#', entry ID in hex), or the
        /// plain path when the ID is unknown. Callers that compare devices by path (OpenTabletDriver does)
        /// then see two devices with the same registry path as two devices.
        /// </summary>
        public string ToDevicePath()
        {
            var path = Path.ToString();
            return EntryId == 0 ? path : path + IdSeparator + EntryId.ToString("X", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Finds the device's registry entry now. Returns 0 when it is gone. With an entry ID this never
        /// falls back to the path: the path could lead to another device with the same path.
        /// The caller owns the returned object (wrap it with ToIOObject and dispose it).
        /// </summary>
        public int GetService(uint masterPort)
        {
            if (EntryId != 0)
            {
                var matching = NativeMethods.IORegistryEntryIDMatching(EntryId); // consumed by IOServiceGetMatchingService
                return matching == IntPtr.Zero ? 0 : NativeMethods.IOServiceGetMatchingService(masterPort, matching);
            }

            var path = Path.Clone();
            return NativeMethods.IORegistryEntryFromPath(masterPort, ref path);
        }

        public bool Equals(MacDeviceKey other)
        {
            if (EntryId != other.EntryId) { return false; }
            return EntryId != 0 || Path == other.Path;
        }

        public override bool Equals(object obj)
        {
            return obj is MacDeviceKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return EntryId != 0 ? EntryId.GetHashCode() : Path.GetHashCode();
        }

        public override string ToString()
        {
            return ToDevicePath();
        }
    }
}
