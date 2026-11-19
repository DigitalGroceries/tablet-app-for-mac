using Newtonsoft.Json.Linq;
using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.Tests;

public class TabletAboutInfoTests
{
    // A daemon TabletReference token: Properties = TabletConfiguration, Identifiers = matched devices.
    private static JArray Tablets(string name) => new()
    {
        new JObject
        {
            ["Properties"] = new JObject
            {
                ["Name"] = name,
                ["Specifications"] = new JObject
                {
                    ["Digitizer"] = new JObject { ["Width"] = 152.0, ["Height"] = 95.0, ["MaxX"] = 30400, ["MaxY"] = 19000 },
                    ["Pen"] = new JObject { ["MaxPressure"] = 8192, ["ButtonCount"] = 2 },
                    ["AuxiliaryButtons"] = new JObject { ["ButtonCount"] = 8 },
                    ["Wheels"] = new JArray { new JObject() },
                },
            },
            ["Identifiers"] = new JArray { new JObject { ["VendorID"] = 0x056A, ["ProductID"] = 0x0357 } },
        },
    };

    [Fact]
    public void From_ParsesTheDeclaredSpecs()
    {
        var a = TabletAboutInfo.From(Tablets("Wacom Intuos Pro M"), "Wacom Intuos Pro M");

        Assert.NotNull(a);
        Assert.Equal(152f, a!.WidthMm, 3);
        Assert.Equal(95f, a.HeightMm, 3);
        Assert.Equal(8192u, a.MaxPressure);
        Assert.Equal(2u, a.PenButtons);
        Assert.Equal(8u, a.ExpressKeys);
        Assert.Equal(1, a.WheelCount);
        // Resolution = MaxX / width = 30400 / 152 = 200 LP/mm ⇒ ×25.4 = 5080 LPI.
        Assert.Equal(200, a.LpMm);
        Assert.Equal(5080, a.Lpi);
        Assert.Equal(0x056A, a.VendorId);
        Assert.Equal(0x0357, a.ProductId);
    }

    [Fact]
    public void From_MatchesNameCaseInsensitively()
    {
        Assert.NotNull(TabletAboutInfo.From(Tablets("PTK-670"), "ptk-670"));
    }

    [Fact]
    public void From_NullWhenTabletAbsentOrNoData()
    {
        Assert.Null(TabletAboutInfo.From(Tablets("A"), "Not Connected"));
        Assert.Null(TabletAboutInfo.From(null, "A"));
    }

    [Theory]
    [InlineData(152, 95, "16:10  (1.600)")]    // exact 16:10
    [InlineData(160, 90, "16:9  (1.778)")]     // exact 16:9
    [InlineData(100, 100, "16:16  (1.000)")]   // square
    [InlineData(160, 120, "16:12  (1.333)")]   // 4:3
    public void FormatAspectRatio_CleanRatios_ShownExactly(double w, double h, string expected)
        => Assert.Equal(expected, TabletAboutInfo.FormatAspectRatio(w, h));

    [Fact]
    public void FormatAspectRatio_NearButNotExact_IsPrefixedCloseTo()
    {
        // 223 × 125: normalized height = 16×125/223 = 8.97 → rounds to 9 but isn't a clean 16:9.
        var s = TabletAboutInfo.FormatAspectRatio(223, 125);
        Assert.StartsWith("close to 16:9", s);
    }

    [Fact]
    public void Parse_OmitsAbsentOptionalSpecs()
    {
        // A minimal config: only a digitizer, no pen/aux/wheels/identifiers.
        var arr = new JArray
        {
            new JObject
            {
                ["Properties"] = new JObject
                {
                    ["Name"] = "Bare",
                    ["Specifications"] = new JObject
                    {
                        ["Digitizer"] = new JObject { ["Width"] = 100.0, ["Height"] = 50.0 },
                    },
                },
            },
        };
        var a = TabletAboutInfo.From(arr, "Bare");

        Assert.NotNull(a);
        Assert.Null(a!.MaxPressure);
        Assert.Null(a.ExpressKeys);
        Assert.Null(a.LpMm);         // no MaxX → not computable
        Assert.Null(a.Lpi);
        Assert.Equal(0, a.WheelCount);
        Assert.False(a.HasTouch);
        Assert.Null(a.VendorId);
    }

    // GitHub #119 (V28): a tablet set up before it was connected is described from the catalog with JSON nulls for the
    // parts it lacks (no mouse, no ExpressKeys, no pen block ...). Reading them threw and crashed the tablet page.
    [Fact]
    public void Parse_SurvivesJsonNullParts()
    {
        var t = new JArray
        {
            new JObject
            {
                ["Properties"] = new JObject
                {
                    ["Name"] = "Wacom CTL-4100WL",
                    ["Specifications"] = new JObject
                    {
                        ["Digitizer"] = new JObject { ["Width"] = 152.0, ["Height"] = 95.0, ["MaxX"] = 15200, ["MaxY"] = JValue.CreateNull() },
                        ["Pen"] = JValue.CreateNull(),
                        ["AuxiliaryButtons"] = JValue.CreateNull(),
                        ["MouseButtons"] = JValue.CreateNull(),
                        ["Touch"] = JValue.CreateNull(),
                        ["Wheels"] = JValue.CreateNull(),
                    },
                    ["Attributes"] = JValue.CreateNull(),
                    ["DigitizerIdentifiers"] = new JArray { JValue.CreateNull() },
                },
            },
        };

        var a = TabletAboutInfo.From(t, "Wacom CTL-4100WL");

        Assert.NotNull(a);
        Assert.Equal(152f, a!.WidthMm, 3);
        Assert.Null(a.ExpressKeys);
        Assert.Null(a.MouseButtons);
        Assert.Null(a.PenButtons);
        Assert.Null(a.MaxPressure);
        Assert.False(a.HasTouch);
        Assert.Equal(0, a.WheelCount);
        Assert.Null(a.VendorId);
    }

    [Fact]
    public void From_SkipsAnEntryThatIsNotAnObject()
    {
        var t = new JArray { JValue.CreateNull(), new JObject { ["Properties"] = JValue.CreateNull() } };
        Assert.Null(TabletAboutInfo.From(t, "Wacom CTL-4100WL"));
    }
}
