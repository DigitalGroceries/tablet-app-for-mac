using System.IO;
using System.Linq;
using Avalonia.Headless.XUnit;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>Build R4: theme names are colour names; ids unchanged so saved choices carry over; no Wacom
/// style wording in user-visible theme text.</summary>
public class BuildR4ThemeNamesTests
{
    [AvaloniaFact]
    public void ThemeLabels_AreColourNames_WithIdsUnchanged()
    {
        var o = new ThemeViewModel().ThemeOptions.ToDictionary(t => t.Id, t => t.Label);
        Assert.Equal("Graphite", o[ThemeService.Wacom]);
        Assert.Equal("Silver", o[ThemeService.Light]);
        Assert.Equal("Charcoal", o[ThemeService.Dark]);
        Assert.Equal("Rose", o[ThemeService.Anime]);
        Assert.Equal("Burgundy", o[ThemeService.DarkSakura]);
        Assert.Equal("System", o[ThemeService.System]);
        Assert.Equal("Custom", o[ThemeService.Custom]);
        Assert.Equal("Wacom", ThemeService.Wacom);   // saved id still resolves
        Assert.All(new ThemeViewModel().ThemeOptions, t =>
        {
            Assert.DoesNotContain("Wacom", t.Label);
            Assert.DoesNotContain("Wacom", t.Description);
        });
    }
}
