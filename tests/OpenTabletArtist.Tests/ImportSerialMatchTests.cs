using Newtonsoft.Json.Linq;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Item 19 (#26): imported profiles get the serial of the same tablet, not of the profile at the same position.</summary>
public class ImportSerialMatchTests
{
    [Fact]
    public void ReorderedProfiles_KeepTheirOwnSerials()
    {
        var current = JToken.Parse("{\"Profiles\":[{\"Tablet\":\"A\",\"SerialNumber\":\"A1\"},{\"Tablet\":\"B\",\"SerialNumber\":\"B1\"}]}");
        var incoming = JToken.Parse("{\"Profiles\":[{\"Tablet\":\"B\",\"SerialNumber\":\"[removed]\"},{\"Tablet\":\"A\",\"SerialNumber\":\"[removed]\"}]}");
        SettingsBundle.KeepLocalSerials(incoming, current);
        Assert.Equal("B1", (string?)incoming["Profiles"]![0]!["SerialNumber"]);
        Assert.Equal("A1", (string?)incoming["Profiles"]![1]!["SerialNumber"]);
    }

    [Fact]
    public void NestedSerial_ComesFromTheSameTablet()
    {
        var current = JToken.Parse("{\"Profiles\":[{\"Tablet\":\"A\",\"Pen\":{\"Serial\":\"PA\"}},{\"Tablet\":\"B\",\"Pen\":{\"Serial\":\"PB\"}}]}");
        var incoming = JToken.Parse("{\"Profiles\":[{\"Tablet\":\"B\",\"Pen\":{\"Serial\":\"[removed]\"}}]}");
        SettingsBundle.KeepLocalSerials(incoming, current);
        Assert.Equal("PB", (string?)incoming["Profiles"]![0]!["Pen"]!["Serial"]);
    }

    [Fact]
    public void UnknownTablet_GetsNoSerial()
    {
        var current = JToken.Parse("{\"Profiles\":[{\"Tablet\":\"A\",\"SerialNumber\":\"A1\"}]}");
        var incoming = JToken.Parse("{\"Profiles\":[{\"Tablet\":\"C\",\"SerialNumber\":\"[removed]\"}]}");
        SettingsBundle.KeepLocalSerials(incoming, current);
        Assert.Equal(JTokenType.Null, incoming["Profiles"]![0]!["SerialNumber"]!.Type);
    }
}
