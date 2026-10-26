using Newtonsoft.Json.Linq;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

public class PenBusySamplerTests
{
    private static JObject PenReport(double pressure, double maxPressure = 8191, bool[]? buttons = null)
    {
        var data = new JObject
        {
            ["Tablet"] = new JObject
            {
                ["Properties"] = new JObject
                {
                    ["Specifications"] = new JObject
                    {
                        ["Digitizer"] = new JObject { ["MaxX"] = 1000, ["MaxY"] = 1000 },
                        ["Pen"] = new JObject { ["MaxPressure"] = maxPressure },
                    }
                }
            },
            ["Data"] = new JObject
            {
                ["Position"] = new JObject { ["X"] = 100, ["Y"] = 200 },
                ["Pressure"] = pressure,
            }
        };
        if (buttons != null)
            ((JObject)data["Data"]!)["PenButtons"] = new JArray(buttons.Select(b => (object)b).ToArray());
        return data;
    }

    [Fact]
    public void TipDown_WhenPressurePositive()
    {
        PenBusySampler.UpdateFromReport(PenReport(100), out var tip, out var buttons);
        Assert.True(tip);
        Assert.Null(buttons);
    }

    [Fact]
    public void TipUp_WhenPressureZero()
    {
        PenBusySampler.UpdateFromReport(PenReport(0), out var tip, out _);
        Assert.False(tip);
    }

    [Fact]
    public void PenButton_ParsedIndependently()
    {
        PenBusySampler.UpdateFromReport(PenReport(0, buttons: new[] { false, true }), out var tip, out var buttons);
        Assert.False(tip);
        Assert.True(buttons);
    }

    [Fact]
    public void AuxOnly_LeavesTipAndButtonsNull()
    {
        var aux = new JObject
        {
            ["Tablet"] = new JObject
            {
                ["Properties"] = new JObject
                {
                    ["Specifications"] = new JObject
                    {
                        ["Digitizer"] = new JObject { ["MaxX"] = 1000, ["MaxY"] = 1000 },
                        ["Pen"] = new JObject { ["MaxPressure"] = 8191 },
                    }
                }
            },
            ["Data"] = new JObject { ["AuxButtons"] = new JArray(true, false) }
        };
        PenBusySampler.UpdateFromReport(aux, out var tip, out var buttons);
        Assert.Null(tip);
        Assert.Null(buttons);
    }
}
