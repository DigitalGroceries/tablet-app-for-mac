using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using OpenTabletArtist.Domain;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Discussion #31: PTH-651 touch trackpad filter sync and the wireless receiver identifiers.</summary>
public class TouchWirelessTests
{
    private static Profile Make(string tablet) =>
        new() { Tablet = tablet, OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)) };

    [Fact]
    public void Pth651_GetsTouchFilterOnce()
    {
        var p = Make("Wacom PTH-651");
        Assert.True(TouchTrackpad.SyncFilter(p));
        Assert.False(TouchTrackpad.SyncFilter(p));
        Assert.Single(p.Filters, f => f.Path == TouchTrackpad.FilterTypeName);
    }

    [Fact]
    public void UserCanSwitchTouchOff()
    {
        var p = Make("Wacom PTH-651");
        TouchTrackpad.SyncFilter(p);
        p.Filters.First(f => f.Path == TouchTrackpad.FilterTypeName).Enable = false;
        Assert.False(TouchTrackpad.SyncFilter(p));
        Assert.False(p.Filters.First(f => f.Path == TouchTrackpad.FilterTypeName).Enable);
    }

    [Fact]
    public void OtherTablets_Untouched()
    {
        var p = Make("Wacom PTK-640");
        Assert.False(TouchTrackpad.SyncFilter(p));
        Assert.Empty(p.Filters);
    }

    [Fact]
    public void Pth651_ListsWirelessReceiver()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "external"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var json = JObject.Parse(File.ReadAllText(Path.Combine(dir!.FullName,
            "external/OpenTabletDriver/OpenTabletDriver.Configurations/Configurations/Wacom/PTH-651.json")));
        var ids = json["DigitizerIdentifiers"]!.Concat(json["AuxiliaryDeviceIdentifiers"]!)
            .Select(i => (int)i["ProductID"]!).ToList();
        Assert.Contains(789, ids);   // USB cable
        Assert.Contains(132, ids);   // 0x0084 Wacom Wireless Receiver (ACK-40401)
    }
}
