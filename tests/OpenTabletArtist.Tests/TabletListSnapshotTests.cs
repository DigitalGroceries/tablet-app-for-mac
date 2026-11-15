using System.Collections.Generic;
using OpenTabletArtist.Services;
using OpenTabletDriver;
using OpenTabletDriver.Plugin.Tablet;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// V28 #80 (Daniel's driver log, 12:38:48: 'Collection was modified' while the app read the device list; plan agreed by
/// Claude and Devin, built by Claude while Devin was out of usage): a tablet reference carries a snapshot of its device
/// identifiers, not a lazy query over the tree's live list that JSON-RPC enumerates later on another thread.
/// Likely cause, not proven: the stack trace is still to be checked.
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
}
