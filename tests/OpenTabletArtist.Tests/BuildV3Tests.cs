using Newtonsoft.Json.Linq;
using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Version 3: the pen-button test reads <c>Data.PenButtons</c> from the daemon's report stream.</summary>
public class BuildV3Tests
{
    [Fact]
    public void PenButtons_AreReadFromPenReports()
    {
        var data = JObject.Parse("{\"Data\":{\"Position\":{\"X\":1,\"Y\":2},\"Pressure\":0,\"PenButtons\":[false,true]}}");
        Assert.True(DeviceReportSample.TryParsePenButtons(data, out var b));
        Assert.Equal(new[] { false, true }, b);
    }

    [Fact]
    public void ReportsWithoutPenButtons_AreIgnored()
    {
        var data = JObject.Parse("{\"Data\":{\"AuxButtons\":[true]}}");
        Assert.False(DeviceReportSample.TryParsePenButtons(data, out _));
    }
}
