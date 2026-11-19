using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HidSharp.Platform.MacOS;
using OpenTabletDriver.Devices.HidSharpBackend;
using OpenTabletDriver.Tests.Fakes;
using Xunit;

namespace OpenTabletDriver.Tests
{
    /// <summary>
    /// Tablet App for Mac #118 (GitHub issue by jbrannigan, CTL-4100WL over Bluetooth): on macOS every
    /// IOHIDUserDevice node (Bluetooth tablet, Magic Trackpad, Logitech BLE mouse) can have the same I/O
    /// Registry path, and HidSharpCore keyed devices by that path, so only the first one was listed and
    /// opening by path reached the first one. Devices are now keyed by registry entry ID. These tests cover
    /// the key itself; finding devices in the real registry needs two Bluetooth HID devices on a Mac.
    /// </summary>
    public class MacDeviceKeyTest
    {
        private const string SharedPath = "IOService:/IOResources/IOHIDResource/IOHIDResourceDeviceUserClient/IOHIDUserDevice"; // from #118

        private static NativeMethods.io_string_t P(string s)
        {
            var bytes = new byte[512];
            Encoding.UTF8.GetBytes(s).CopyTo(bytes, 0);
            return new NativeMethods.io_string_t { Value = bytes };
        }

        [Fact]
        public void TwoDevicesWithTheSamePath_AreTwoKeys()
        {
            var tablet = new MacDeviceKey(P(SharedPath), 0x100000A1B);
            var trackpad = new MacDeviceKey(P(SharedPath), 0x100000C2D);

            Assert.NotEqual(tablet, trackpad);
            Assert.False(((object)tablet).Equals(trackpad));
            Assert.NotEqual(tablet.ToDevicePath(), trackpad.ToDevicePath());

            // What HidManager.GetDevices does with the keys: both must survive as additions, and a
            // dictionary keyed by them must hold both (it threw on the second equal key before).
            object[] found = { trackpad, tablet };
            object[] known = { trackpad };
            Assert.Equal(new object[] { tablet }, found.Except(known).ToArray());
            var list = new Dictionary<object, string> { [trackpad] = "trackpad" };
            list.Add(tablet, "tablet");
            Assert.Equal(2, list.Count);
        }

        [Fact]
        public void TheSameDevice_GivesAnEqualKey()
        {
            var a = new MacDeviceKey(P(SharedPath), 0x42);
            var b = new MacDeviceKey(P(SharedPath), 0x42);

            Assert.Equal(a, b);
            Assert.True(((object)a).Equals(b));
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
            Assert.Empty(new object[] { a }.Except(new object[] { b }));
        }

        [Fact]
        public void WithoutAnId_ThePathAloneIsTheKey_AsBefore()
        {
            var a = new MacDeviceKey(P(SharedPath), 0);
            var b = new MacDeviceKey(P(SharedPath), 0);
            var other = new MacDeviceKey(P(SharedPath + "/x"), 0);

            Assert.Equal(a, b);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
            Assert.NotEqual(a, other);
            Assert.Equal(SharedPath, a.ToDevicePath());
            Assert.NotEqual(a, new MacDeviceKey(P(SharedPath), 7)); // known ID never equals unknown ID
        }

        [Fact]
        public void DevicePath_IsThePathThenHashThenTheIdInHex()
        {
            Assert.Equal(SharedPath + "#1A2B3C", new MacDeviceKey(P(SharedPath), 0x1A2B3C).ToDevicePath());
            Assert.Equal(SharedPath + "#FFFFFFFFFFFFFFFF", new MacDeviceKey(P(SharedPath), ulong.MaxValue).ToDevicePath());
        }

        [Theory]
        [InlineData(0x1UL)]
        [InlineData(0x100000A1BUL)]
        [InlineData(ulong.MaxValue)]
        public void MacRegistryPath_SplitsWhatTheKeyWrites(ulong id)
        {
            var devicePath = new MacDeviceKey(P(SharedPath), id).ToDevicePath();

            Assert.True(MacRegistryPath.TrySplit(devicePath, out var registryPath, out var entryId));
            Assert.Equal(SharedPath, registryPath);
            Assert.Equal(id, entryId);
        }

        [Theory]
        [InlineData(SharedPath)]                         // plain path: no ID
        [InlineData(SharedPath + "#")]                    // nothing after '#'
        [InlineData(SharedPath + "#xyz")]                 // not hex
        [InlineData(SharedPath + "#0")]                   // 0 = unknown ID
        [InlineData(SharedPath + "#1FFFFFFFFFFFFFFFF")]   // more than 64 bits
        [InlineData("#1A")]                               // no path before '#'
        [InlineData("")]
        public void MacRegistryPath_LeavesAPlainPathAlone(string devicePath)
        {
            Assert.False(MacRegistryPath.TrySplit(devicePath, out var registryPath, out var entryId));
            Assert.Equal(devicePath, registryPath);
            Assert.Equal(0UL, entryId);
        }

        [SkippableTheory]
        [InlineData("IOService:/AppleARMPE/arm-io/AppleT811xIO/usb-drd0@2280000/AppleT8112USBXHCI@00000000/usb-drd0-port-hs@00100000/Intuos4@00100000/IOUSBHostInterface@0/AppleUserUSBHostHIDDevice#100000A1B", "0")]
        [InlineData("IOService:/AppleARMPE/arm-io/AppleT811xIO/usb-drd0@2280000/AppleT8112USBXHCI@00000000/usb-drd0-port-hs@00100000/Intuos4@00100000/IOUSBHostInterface@1/AppleUserUSBHostHIDDevice#2F", "1")]
        public void DeviceAttributes_StillReadTheInterfaceNumber_WithAnId_OnMacOS(string devicePath, string expectedInterface)
        {
            Skip.IfNot(OperatingSystem.IsMacOS(), "The macOS path rules are used only on macOS.");
            var endpoint = new HidSharpEndpoint(new FakeHidDevice().WithDevicePath(devicePath));

            var attributes = endpoint.DeviceAttributes;

            Assert.True(attributes.ContainsKey("USB_INTERFACE_NUMBER"));
            Assert.Equal(expectedInterface, attributes["USB_INTERFACE_NUMBER"]);
        }
    }
}
