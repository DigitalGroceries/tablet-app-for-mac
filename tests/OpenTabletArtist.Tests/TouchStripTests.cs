using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;
using OpenTabletDriver.Configurations;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Version 16: touch strips (Intuos3, Cintiq 21UX / 12WX / 22HD) reported as wheels and labelled
/// Up / Down, plus the tablet-support changes that come with them.</summary>
public class TouchStripTests
{
    private static JArray Tablet(string name, JObject? attributes) => new()
    {
        new JObject
        {
            ["Properties"] = new JObject
            {
                ["Name"] = name,
                ["Specifications"] = new JObject
                {
                    ["Digitizer"] = new JObject { ["Width"] = 203.2, ["Height"] = 152.4, ["MaxX"] = 40640, ["MaxY"] = 30480 },
                    ["Wheels"] = new JArray { new JObject(), new JObject() },
                },
                ["Attributes"] = attributes,
            },
        },
    };

    [Fact]
    public void Attribute_Marks_The_Wheels_As_Touch_Strips()
    {
        var info = TabletAboutInfo.From(Tablet("Wacom PTZ-630", new JObject { ["WheelsAreTouchStrips"] = "true" }), "Wacom PTZ-630");
        Assert.True(info!.WheelsAreTouchStrips);
        Assert.Equal(2, info.WheelCount);
    }

    [Fact]
    public void No_Attribute_Means_A_Ring()
    {
        Assert.False(TabletAboutInfo.From(Tablet("Wacom PTK-440", null), "Wacom PTK-440")!.WheelsAreTouchStrips);
        Assert.False(TabletAboutInfo.From(Tablet("Wacom PTK-440", new JObject { ["FeatureInitDelayMs"] = "150" }), "Wacom PTK-440")!.WheelsAreTouchStrips);
    }

    private static ButtonBinding Row(string label) =>
        new(0, AuxBinding.Unbound, isOtherBinding: false, otherLabel: "", canEdit: false, applyBinding: null, label: label);

    private static WheelEditor Strip() => new(
        wheelIndex: 0, title: "Left touch strip", showTitle: true,
        clockwise: Row("Up"), counterClockwise: Row("Down"), buttons: new List<ButtonBinding>(),
        clockwiseThreshold: 15, counterClockwiseThreshold: 15, stepSizeDegrees: 360.0 / 13,
        applyThreshold: null, isStrip: true);

    [Fact]
    public void Strip_Finger_Moving_Up_Lights_The_Up_Row()
    {
        var e = Strip();
        e.OnAbsolutePosition(8);
        e.OnAbsolutePosition(7);   // position counts from the top, so falling = up
        Assert.True(e.Clockwise.IsPressed);
        Assert.False(e.CounterClockwise.IsPressed);
        Assert.Equal("Up", e.LiveReadout);
    }

    [Fact]
    public void Strip_Finger_Moving_Down_Lights_The_Down_Row()
    {
        var e = Strip();
        e.OnAbsolutePosition(3);
        e.OnAbsolutePosition(4);
        Assert.True(e.CounterClockwise.IsPressed);
        Assert.Equal("Down", e.LiveReadout);
    }

    [Fact]
    public void Strip_Has_No_Ring_Picture_And_Asks_For_A_Touch()
    {
        var e = Strip();
        Assert.True(e.IsStrip);
        Assert.False(e.IsRing);
        Assert.Equal("Touch the strip", e.LiveReadout);
    }

    [Theory]
    [InlineData(0, 1, "Touch strip")]
    [InlineData(0, 2, "Left touch strip")]
    [InlineData(1, 2, "Right touch strip")]
    public void Strip_Titles(int index, int count, string expected)
    {
        Assert.Equal(expected, TabletDetailViewModel.StripTitle(index, count));
    }

    // ---- the bundled tablet configurations (what the app and its driver actually use) ----

    private static Dictionary<string, OpenTabletDriver.Plugin.Tablet.TabletConfiguration> Configs() =>
        new DeviceConfigurationProvider().TabletConfigurations.ToDictionary(c => c.Name);

    [Theory]
    [InlineData("Wacom PTZ-430", 1)]
    [InlineData("Wacom PTZ-431W", 1)]
    [InlineData("Wacom PTZ-630", 2)]
    [InlineData("Wacom PTZ-631W", 2)]
    [InlineData("Wacom PTZ-930", 2)]
    [InlineData("Wacom PTZ-1230", 2)]
    [InlineData("Wacom PTZ-1231W", 2)]
    [InlineData("Wacom Cintiq 21UX (DTZ-2100)", 2)]
    [InlineData("Wacom Cintiq 12WX (DTZ-1200W)", 2)]
    [InlineData("Wacom Cintiq 21UX (DTK-2100)", 2)]
    [InlineData("Wacom Cintiq 22HD (DTK-2200)", 2)]
    [InlineData("Wacom Cintiq 22HD Touch (DTH-2200)", 2)]
    public void Strip_Tablets_Declare_Their_Strips(string name, int strips)
    {
        var c = Configs()[name];
        Assert.Equal(strips, c.Specifications.Wheels!.Count);
        Assert.All(c.Specifications.Wheels, w => Assert.Equal(12u, w.AbsoluteWheelMax));
        Assert.Equal("true", c.Attributes!["WheelsAreTouchStrips"]);
    }

    [Fact]
    public void Cintiq13HD_Has_Nine_ExpressKeys()
    {
        var all = Configs();
        Assert.Equal(9u, all["Wacom Cintiq 13HD (DTK-1300)"].Specifications.AuxiliaryButtons!.ButtonCount);
        Assert.Equal(9u, all["Wacom Cintiq 13HD Touch (DTH-1300)"].Specifications.AuxiliaryButtons!.ButtonCount);
    }

    [Theory]
    [InlineData("Wacom PTH-460")]
    [InlineData("Wacom PTH-660")]
    [InlineData("Wacom PTH-860")]
    public void IntuosPro2017_Uses_The_Corrected_Ring(string name)
    {
        var c = Configs()[name];
        Assert.Contains(c.DigitizerIdentifiers, d => d.ReportParser.EndsWith("ReversedRingReportParser"));
        Assert.DoesNotContain(c.DigitizerIdentifiers, d => d.ReportParser.EndsWith(".IntuosV2ReportParser"));
        Assert.Equal(180f, c.Specifications.Wheels![0].AngleOfZeroReading);
    }

    [Fact]
    public void New_Wacom_CTK_4120_Is_Listed()
    {
        Assert.True(Configs().ContainsKey("Wacom CTK-4120"));
        Assert.Contains(Services.SupportedTabletsCatalog.All, t => t.Name == "Wacom CTK-4120" && t.Status == "Supported");
    }

    [Fact]
    public void Supported_Tablets_List_Has_No_Windows_Notes()
    {
        Assert.DoesNotContain(Services.SupportedTabletsCatalog.All, t => t.Notes.Contains("Windows"));
        var ptu = Services.SupportedTabletsCatalog.All.Single(t => t.Name == "Wacom PTU-600U");
        Assert.Equal("Supported", ptu.Status);
    }

    [Fact]
    public void Questionable_Sorts_Like_Has_Quirks()
    {
        var dtk = Services.SupportedTabletsCatalog.All.Single(t => t.Name == "Wacom Cintiq 21UX (DTK-2100)");
        Assert.Equal("Questionable", dtk.Status);
        Assert.Equal(1, dtk.StatusRank);
    }
}
