using System;
using System.Collections.Generic;
using System.Linq;
using OpenTabletArtist.Services;
using OpenTabletDriver;
using OpenTabletDriver.Devices;
using OpenTabletDriver.Plugin.Components;
using OpenTabletDriver.Plugin.Devices;
using OpenTabletDriver.Plugin.Tablet;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// V28 #80, Daniel's report of 15 Nov: his app.log has, at 12:38:48.909, "Couldn't read the daemon's device list. —
/// RemoteSerializationException: Error writing JSON RPC Message: InvalidOperationException: Collection was modified".
/// That message comes from the app's GetDevices call (OtdInterop/DaemonClient.cs). The driver's log shows a device
/// being added at that moment (12:38:48.876 and .897, Daniel plugging his CTH-461 in). The driver answered GetDevices
/// with RootHub's live endpoint list, which JSON-RPC wrote out on another thread while the device change rewrote it.
/// Fixed in patches/otd-getdevices-snapshot.patch: the list is copied under the hub's lock. The GetTablets snapshot
/// (patches/otd-gettablets-snapshot.patch) is the same kind of guard for the tablet list.
/// </summary>
public class TabletListSnapshotTests
{
    [Fact]
    public void AReference_KeepsItsIdentifiers_WhenTheDeviceListChangesLater()
    {
        var devices = new List<OpenTabletDriver.InputDevice>();
        var tree = new InputDeviceTree(OfflineTablet.ConfigFor("Wacom PTK-440")!, devices);
        var reference = tree.CreateReference();
        Assert.IsType<DeviceIdentifier[]>(reference.Identifiers);
        devices.Add(null!);   // the live list changes after the reference was handed out
        Assert.Empty(reference.Identifiers);   // a lazy query would now throw or see the new entry
    }

    [Fact]
    public void TheDeviceList_CanBeReadToTheEnd_WhenATabletIsPluggedInWhileItIsBeingRead()
    {
        var hub = new FakeHub(new FakeEndpoint("mouse"), new FakeEndpoint("keyboard"));
        var root = new RootHub(new FakeHubs(hub));

        var list = root.GetDevices();
        using var reading = list.GetEnumerator();   // JSON-RPC has started writing the answer...
        Assert.True(reading.MoveNext());

        hub.PlugIn(new FakeEndpoint("CTH-461"));    // ...when the tablet is plugged in (Daniel, 12:38:48)

        var error = Record.Exception(() => { while (reading.MoveNext()) { } });
        Assert.Null(error);   // before the patch: InvalidOperationException 'Collection was modified'
        Assert.Equal(3, root.GetDevices().Count());   // the next read sees the new tablet
    }

    private sealed class FakeHubs(IDeviceHub hub) : IDeviceHubsProvider
    {
        public IEnumerable<IDeviceHub> DeviceHubs { get; } = [hub];
    }

    private sealed class FakeHub(params IDeviceEndpoint[] start) : IDeviceHub
    {
        private readonly List<IDeviceEndpoint> _devices = [.. start];
        public event EventHandler<DevicesChangedEventArgs>? DevicesChanged;
        public IEnumerable<IDeviceEndpoint> GetDevices() => _devices.ToArray();

        public void PlugIn(IDeviceEndpoint device)
        {
            var before = _devices.ToArray();
            _devices.Add(device);
            DevicesChanged?.Invoke(this, new DevicesChangedEventArgs(before, _devices.ToArray()));
        }
    }

    private sealed class FakeEndpoint(string path) : IDeviceEndpoint
    {
        public int ProductID => 0;
        public int VendorID => 0;
        public int InputReportLength => 0;
        public int OutputReportLength => 0;
        public int FeatureReportLength => 0;
        public string? Manufacturer => null;
        public string? ProductName => path;
        public string? FriendlyName => path;
        public string? SerialNumber => null;
        public string DevicePath => path;
        public bool CanOpen => false;
        public IDictionary<string, string> DeviceAttributes { get; } = new Dictionary<string, string>();
        public IDeviceEndpointStream Open() => throw new NotSupportedException();
        public string? GetDeviceString(byte index) => null;
        public void OpenAndSendFeatureInit(byte[] initData) { }
        public void OpenAndSendOutputInit(byte[] initData) { }
    }
}
