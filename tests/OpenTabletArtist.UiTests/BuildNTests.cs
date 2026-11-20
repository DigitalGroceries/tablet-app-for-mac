using System.Linq;
using OpenTabletArtist.Converters;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>Build N.1 polish: title-case exceptions and short Function names.</summary>
public class BuildNTests
{
    [Theory]
    [InlineData("expresskeys", "ExpressKeys")]
    [InlineData("ExpressKeys", "ExpressKeys")]
    [InlineData("json", "JSON")]
    [InlineData("touch ring", "Touch ring")]
    [InlineData("MAPPING", "Mapping")]
    public void TitleCase_KeepsExceptions(string input, string expected) =>
        Assert.Equal(expected, TitleCaseConverter.Apply(input));

    [Fact]
    public void FunctionNames_AreShort()
    {
        // Version 26 (#35): "Scroll" (a plain mouse wheel) added last. V29 #153: "Keystroke…" is "Keyboard shortcut…", and
        // Volume, Brightness and Previous / next track can be picked straight from the menu.
        Assert.Equal(new[] { "Keyboard shortcut…", "Smooth rotate", "Smooth zoom", "Smooth brush size", "Smooth hardness", "Scroll",
                             "Volume", "Brightness", "Previous / next track" },
            RingModeRow.ActionNames.ToArray());
        // Build Q: "(Experimental)" suffix; the dropdown stretches to half the row, so 32 chars is the cap.
        Assert.All(RingModeRow.ActionNames, n => Assert.True(n.Length <= 32, n));
    }
}
