using Newtonsoft.Json.Linq;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

public class Intuos4StatusLedTests
{
    [Fact]
    public void Report_MatchesLinuxLayout()
    {
        Assert.Equal(new byte[] { 0x20, 0x04, 10, 127, 10, 0, 0, 0, 0 }, Intuos4StatusLed.BuildReport(true));
        var off = Intuos4StatusLed.BuildReport(false);
        Assert.Equal(9, off.Length);
        Assert.Equal(off[2], off[3]);   // steady: no change on touch
    }

    [Theory]
    [InlineData(0x056A, 0xB8, true)]
    [InlineData(0x056A, 0xBB, true)]
    [InlineData(0x056A, 0x0357, false)]
    [InlineData(0x28BD, 0xB8, false)]
    public void Intuos4Only(int vid, int pid, bool expected) =>
        Assert.Equal(expected, Intuos4StatusLed.IsIntuos4(vid, pid));

    [Fact]
    public void PicksLowestIntuos4InterfaceWithFeatureReports()
    {
        var devices = JArray.Parse("""
        [
          {"VendorID":1386,"ProductID":184,"FeatureReportLength":0,"DeviceAttributes":{"USB_INTERFACE_NUMBER":"0"}},
          {"VendorID":1386,"ProductID":184,"FeatureReportLength":33,"DeviceAttributes":{"USB_INTERFACE_NUMBER":"1"}},
          {"VendorID":1386,"ProductID":184,"FeatureReportLength":33,"DeviceAttributes":{"USB_INTERFACE_NUMBER":"2"}},
          {"VendorID":1386,"ProductID":855,"FeatureReportLength":33,"DeviceAttributes":{"USB_INTERFACE_NUMBER":"0"}}
        ]
        """);
        Assert.Equal((1386, 184, "1"), Intuos4StatusLed.PickInterface(devices));
        Assert.Null(Intuos4StatusLed.PickInterface(new JArray()));
    }
}

/// <summary>Item 23 (#27): with two Intuos4 tablets connected, both get the brightness setting.</summary>
public class Intuos4TwoTabletsTests
{
    [Fact]
    public void TwoSameModels_OneTargetCountingBoth()
    {
        var devices = JArray.Parse("""
        [
          {"VendorID":1386,"ProductID":184,"FeatureReportLength":33,"DeviceAttributes":{"USB_INTERFACE_NUMBER":"1"}},
          {"VendorID":1386,"ProductID":184,"FeatureReportLength":33,"DeviceAttributes":{"USB_INTERFACE_NUMBER":"2"}},
          {"VendorID":1386,"ProductID":184,"FeatureReportLength":33,"DeviceAttributes":{"USB_INTERFACE_NUMBER":"1"}},
          {"VendorID":1386,"ProductID":184,"FeatureReportLength":33,"DeviceAttributes":{"USB_INTERFACE_NUMBER":"2"}}
        ]
        """);
        var t = Assert.Single(Intuos4StatusLed.PickInterfaces(devices));
        Assert.Equal((1386, 184, "1", 2), t);
    }

    [Fact]
    public void TwoDifferentModels_TwoTargets()
    {
        var devices = JArray.Parse("""
        [
          {"VendorID":1386,"ProductID":184,"FeatureReportLength":33,"DeviceAttributes":{"USB_INTERFACE_NUMBER":"1"}},
          {"VendorID":1386,"ProductID":185,"FeatureReportLength":33,"DeviceAttributes":{"USB_INTERFACE_NUMBER":"1"}},
          {"VendorID":1386,"ProductID":855,"FeatureReportLength":33,"DeviceAttributes":{"USB_INTERFACE_NUMBER":"0"}}
        ]
        """);
        var t = Intuos4StatusLed.PickInterfaces(devices);
        Assert.Equal(2, t.Count);
        Assert.Contains((1386, 185, "1", 1), t);
        Assert.Empty(Intuos4StatusLed.PickInterfaces(new JArray()));
    }
}

/// <summary>Item 24 (#27): a dropped connection must not stop the light being set after reconnect.</summary>
public class Intuos4ReconnectTests
{
    [Fact]
    public void FailureWhileConnected_Latches() => Assert.True(Intuos4StatusLed.LatchesFailure(true));

    [Fact]
    public void FailureFromDroppedConnection_DoesNotLatch() => Assert.False(Intuos4StatusLed.LatchesFailure(false));

    [Fact]
    public void ReconnectAndReplug_ClearTheFailure()
    {
        var src = System.IO.File.ReadAllText(System.IO.Path.Combine(Root(), "OpenTabletArtist/Services/Intuos4StatusLed.cs"));
        Assert.Contains("if (_connection.IsConnected) { _failed = false;", src);
        Assert.Contains("_lastSent = null; _failed = false;", src);
    }

    private static string Root([System.Runtime.CompilerServices.CallerFilePath] string f = "") =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(f)!, "..", ".."));
}

/// <summary>Version 23 BETA (Jay, Discussion #31): the Status light setting only while an Intuos4 is connected.</summary>
public class StatusLightOnlyForIntuos4Tests
{
    [Fact]
    public void An_Intuos4_among_the_connected_tablets_shows_it() =>
        Assert.True(Intuos4StatusLed.AnyIntuos4(new[] { "Wacom PTH-651", "Wacom PTK-440" }));

    [Fact]
    public void Other_tablets_or_none_hide_it()
    {
        Assert.False(Intuos4StatusLed.AnyIntuos4(new[] { "Wacom PTH-651" }));
        Assert.False(Intuos4StatusLed.AnyIntuos4(System.Array.Empty<string>()));
    }

    [Fact]
    public void The_names_are_the_driver_configs_for_the_Intuos4_product_ids()
    {
        var dir = System.IO.Path.Combine(Root(), "external/OpenTabletDriver/OpenTabletDriver.Configurations/Configurations/Wacom");
        var names = new System.Collections.Generic.List<string>();
        foreach (var f in System.IO.Directory.GetFiles(dir, "*.json"))
        {
            var j = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(f));
            var ids = (j["DigitizerIdentifiers"] as Newtonsoft.Json.Linq.JArray ?? new()).Select(i => (int?)i["ProductID"] ?? 0);
            if (ids.Any(id => Intuos4StatusLed.IsIntuos4(Intuos4StatusLed.WacomVendorId, id))) names.Add((string)j["Name"]!);
        }
        Assert.Equal(Intuos4StatusLed.Intuos4Names.OrderBy(n => n), names.OrderBy(n => n));
    }

    private static string Root([System.Runtime.CompilerServices.CallerFilePath] string f = "") =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(f)!, "..", ".."));
}
