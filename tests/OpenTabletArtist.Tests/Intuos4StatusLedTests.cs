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
