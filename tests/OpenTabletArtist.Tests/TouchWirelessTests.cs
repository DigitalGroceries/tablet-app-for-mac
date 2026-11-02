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

public class TouchWirelessReviewTests
{
    private static OpenTabletDriver.Desktop.Settings With(string tablet)
    {
        var s = new OpenTabletDriver.Desktop.Settings();
        s.Profiles.Add(new Profile { Tablet = tablet, OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)) });
        return s;
    }

    [Fact]
    public void Switch_TurnsTrackpadOffAndOn()
    {
        var s = With("Wacom PTH-651");
        var p = s.Profiles.First();
        Assert.True(TouchTrackpad.IsOn(p));
        TouchTrackpad.Write(s, "Wacom PTH-651", false);
        Assert.False(TouchTrackpad.IsOn(p));
        TouchTrackpad.Write(s, "Wacom PTH-651", true);
        Assert.True(TouchTrackpad.IsOn(p));
        Assert.False(TouchTrackpad.IsOn(With("Wacom PTK-640").Profiles.First()));
    }

    [Fact]
    public void Notice_ShownForPth651Only_WithWirelessCaveat()
    {
        var items = TabletUpdateNotices.Pending(new[] { "Wacom PTH-651" }, _ => false);
        var n = Assert.Single(items, i => i.Id == "pth651-touch-trackpad-wireless");
        Assert.Contains(n.Paragraphs, p => p.Contains("Wireless: PTH-651 only, untested"));
        Assert.Contains(n.Paragraphs, p => p.Contains("switch it off"));
        Assert.DoesNotContain(TabletUpdateNotices.Pending(new[] { "Wacom PTH-660" }, _ => false), i => i.Id == "pth651-touch-trackpad-wireless");
    }

    [Fact]
    public void Card_SaysBetaWhenOn_NotUsedWhenOff()
    {
        var info = new TabletAboutInfo { HasTouch = true };
        Assert.Contains(OpenTabletArtist.ViewModels.TabletAboutFacts.BuildFeatures(info, true), f => f.Value == "Touch trackpad (BETA, please test)");
        Assert.Contains(OpenTabletArtist.ViewModels.TabletAboutFacts.BuildFeatures(info, false), f => f.Value == "Detected, not used yet");
    }

    [Fact]
    public void WirelessReceiverId_OnlyInPth651()
    {
        var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "external"))) dir = dir.Parent;
        var cfg = System.IO.Path.Combine(dir!.FullName, "external/OpenTabletDriver/OpenTabletDriver.Configurations/Configurations");
        var hits = System.IO.Directory.GetFiles(cfg, "*.json", System.IO.SearchOption.AllDirectories)
            .Where(f => JObject.Parse(System.IO.File.ReadAllText(f)).SelectTokens("$..ProductID").Any(t => (int)t == 132)
                     && JObject.Parse(System.IO.File.ReadAllText(f)).SelectTokens("$..VendorID").Any(t => (int)t == 1386))
            .Select(System.IO.Path.GetFileName).ToList();
        Assert.Equal(new[] { "PTH-651.json" }, hits);
    }
}
