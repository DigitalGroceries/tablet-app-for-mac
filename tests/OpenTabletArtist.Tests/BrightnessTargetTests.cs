using System.Collections.Generic;
using OpenTabletDriver.Desktop.Interop.Input.Keyboard;
using Xunit;
using Box = OpenTabletDriver.Desktop.Interop.Input.Keyboard.MacOSSystemKeys.Box;

namespace OpenTabletArtist.Tests;

/// <summary>V29 #87 (Zak's build 4 test, 20 Nov 06:40): a Touch Ring brightness step must change the screen the Mac's own
/// key changes - the screen of the window last clicked - not always the built-in one. Zak's layout: built-in 1496 x 967,
/// SONY TV 3840 x 2160 above it, a third display to the side.</summary>
public class BrightnessTargetTests
{
    private const uint BuiltIn = 1, Sony = 2, Side = 3;

    private static readonly List<(uint, Box)> ZaksScreens = new()
    {
        (BuiltIn, new Box(0, 0, 1496, 967)),
        (Sony, new Box(-1172, -2160, 3840, 2160)),
        (Side, new Box(1496, 0, 1600, 1000)),
    };

    [Fact]
    public void AWindowOnTheTv_PicksTheTv_EvenWithThePointerOnTheMac()
    {
        var (display, why) = MacOSSystemKeys.PickDisplay(new Box(100, -1500, 1200, 800), (300, 300), ZaksScreens, BuiltIn);
        Assert.Equal(Sony, display);
        Assert.Equal("the front window's screen", why);
    }

    [Fact]
    public void AWindowAcrossTwoScreens_PicksTheScreenWithMostOfIt()
    {
        // 200 px on the built-in screen, 1000 px on the side screen
        var (display, _) = MacOSSystemKeys.PickDisplay(new Box(1296, 100, 1200, 600), null, ZaksScreens, BuiltIn);
        Assert.Equal(Side, display);
    }

    [Fact]
    public void NoWindow_UsesThePointersScreen()
    {
        var (display, why) = MacOSSystemKeys.PickDisplay(null, (2000, 500), ZaksScreens, BuiltIn);
        Assert.Equal(Side, display);
        Assert.Equal("the pointer's screen", why);
    }

    [Fact]
    public void AWindowOffEveryScreen_FallsBackToThePointer()
    {
        var (display, _) = MacOSSystemKeys.PickDisplay(new Box(9000, 9000, 500, 500), (0, -100), ZaksScreens, BuiltIn);
        Assert.Equal(Sony, display);
    }

    [Fact]
    public void NothingKnown_UsesTheMainScreen()
    {
        var (display, why) = MacOSSystemKeys.PickDisplay(null, null, ZaksScreens, BuiltIn);
        Assert.Equal(BuiltIn, display);
        Assert.Equal("the main screen", why);
    }

    [Theory]
    [InlineData(0.5f, true, 0.5625f)]
    [InlineData(0.5f, false, 0.4375f)]
    [InlineData(1f, true, 1f)]           // stays at the top
    [InlineData(0f, false, 0f)]          // stays at the bottom
    [InlineData(0.51f, true, 0.5625f)]   // snapped to the Mac's 16 steps first
    [InlineData(0.97f, true, 1f)]
    public void OneStep_IsOneOfTheMacsSixteenSteps(float current, bool up, float expected)
    {
        Assert.Equal(expected, MacOSSystemKeys.NextBrightness(current, up), 4);
    }

    [Fact]
    public void ControlHeld_IsSentWithTheKey_LikeTheMacsControlBrightness()
    {
        Assert.Equal(0xA00UL, MacOSSystemKeys.EventFlags(down: true, control: false));
        Assert.Equal(0x40B00UL, MacOSSystemKeys.EventFlags(down: false, control: true));
    }

    [Fact]
    public void OffAMac_NothingIsPosted()
    {
        if (System.OperatingSystem.IsMacOS()) return;
        Assert.False(MacOSSystemKeys.Post("BrightnessUp", true));
        Assert.False(MacOSSystemKeys.Post("NotABrightnessKey", true));
    }
}
