using System.Linq;
using OpenTabletDriver.Desktop.Interop.Input.Keyboard;
using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Build G: the macOS virtual keyboard can send F13-F15, so the ring can send Photoshop's hidden
/// Option+F13 / Option+F14 view rotation. (Runs on any OS: it only reads the macOS key table.)</summary>
public class BuildGTests
{
    [Theory]
    [InlineData("F13")]
    [InlineData("F14")]
    [InlineData("F15")]
    public void MacKeyboard_SupportsF13ToF15(string key) =>
        Assert.Contains(key, new MacOSVirtualKeyboard().SupportedKeys);

    [Fact]
    public void MacKeyboard_StillHasF1ToF12_AndNoDuplicates()
    {
        var keys = new MacOSVirtualKeyboard().SupportedKeys.ToList();
        for (int f = 1; f <= 12; f++) Assert.Contains($"F{f}", keys);
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    // The ring-mode plugin only sends a chord when every key is supported by the keyboard (ParseKeys);
    // Option+F13 must pass that check on macOS.
    [Fact]
    public void OptionF13_IsAValidChord_OnTheMacKeyboard()
    {
        var supported = new MacOSVirtualKeyboard().SupportedKeys.ToHashSet();
        Assert.True(new[] { "Alt", "F13" }.All(supported.Contains));
        Assert.True(new[] { "Alt", "F14" }.All(supported.Contains));
    }

    // The ring row stores Option+F13 as a Multi-Key Binding and reads it back unchanged.
    [Fact]
    public void OptionF13_RoundTripsThroughTheBindingEditor()
    {
        var combo = new AuxCombo(Ctrl: false, Shift: false, Alt: true, Key: "F13");
        var store = AuxKeyBinding.MakeBinding(combo);
        Assert.NotNull(store);
        Assert.Equal(combo, AuxKeyBinding.ReadCombo(store));
    }
}
