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

    /// <summary>Build 6 report (20 Nov 08:50:46): Zak clicked the desktop on the built-in screen; macOS named Finder's
    /// desktop, 0,-2160 3992x3160 (all three screens), as the focused window and the TV changed. These are the screens
    /// from that report.</summary>
    private static readonly List<(uint, Box)> Build6Screens = new()
    {
        (1, new Box(0, 0, 1496, 967)),
        (2, new Box(1496, 0, 1600, 1000)),
        (3, new Box(152, -2160, 3840, 2160)),
    };

    [Fact]
    public void TheDesktopInFront_UsesThePointersScreen_NotTheMiddleOfAllScreens()
    {
        var desktop = new Box(0, -2160, 3992, 3160);
        Assert.Equal(3, MacOSSystemKeys.CoversWholeScreens(desktop, Build6Screens));
        var (display, why) = MacOSSystemKeys.PickDisplay(desktop, (915, 365), Build6Screens, 1);
        Assert.Equal(1u, display);
        Assert.Equal("the pointer's screen; the desktop is in front", why);
        Assert.Equal(2u, MacOSSystemKeys.PickDisplay(desktop, (2000, 500), Build6Screens, 1).Display);
        Assert.Equal(3u, MacOSSystemKeys.PickDisplay(desktop, (849, -1476), Build6Screens, 1).Display);
    }

    [Fact]
    public void TheDesktopInFront_WithNoPointer_UsesTheMainScreen()
    {
        var (display, why) = MacOSSystemKeys.PickDisplay(new Box(0, -2160, 3992, 3160), null, Build6Screens, 1);
        Assert.Equal(1u, display);
        Assert.Equal("the main screen", why);
    }

    [Fact]
    public void AFinderWindowOnTheMac_StillPicksTheMac_WhereverThePointerIs()
    {
        // 08:51:11 in the same report: a Finder window on the built-in screen, the pointer up on the TV
        var (display, why) = MacOSSystemKeys.PickDisplay(new Box(222, 133, 1054, 933), (1094, -891), Build6Screens, 1);
        Assert.Equal(1u, display);
        Assert.Equal("the front window's screen", why);
    }

    [Fact]
    public void AFullScreenWindowOnOneScreen_IsStillThatScreensWindow()
    {
        Assert.Equal(1, MacOSSystemKeys.CoversWholeScreens(new Box(152, -2160, 3840, 2160), Build6Screens));
        Assert.Equal(3u, MacOSSystemKeys.PickDisplay(new Box(152, -2160, 3840, 2160), (300, 300), Build6Screens, 1).Display);
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

    [Fact]
    public void ControlFromTheMacScreen_ChangesTheExternalScreen_UnderThePointerFirst()
    {
        bool builtIn(uint d) => d == BuiltIn;
        Assert.Equal(Side, MacOSSystemKeys.OtherDisplay(BuiltIn, ZaksScreens, (2000, 500), builtIn));
        Assert.Equal(Sony, MacOSSystemKeys.OtherDisplay(BuiltIn, ZaksScreens, (300, 300), builtIn));   // pointer on the Mac: first external
    }

    [Fact]
    public void ControlFromAnExternalScreen_ChangesTheMacScreen()
    {
        bool builtIn(uint d) => d == BuiltIn;
        Assert.Equal(BuiltIn, MacOSSystemKeys.OtherDisplay(Sony, ZaksScreens, (0, -100), builtIn));
        Assert.Equal(BuiltIn, MacOSSystemKeys.OtherDisplay(Side, ZaksScreens, null, builtIn));
    }

    [Fact]
    public void ControlWithOneScreen_HasNoOtherScreen()
    {
        var one = new List<(uint, Box)> { (BuiltIn, new Box(0, 0, 1496, 967)) };
        Assert.Equal(0u, MacOSSystemKeys.OtherDisplay(BuiltIn, one, null, d => d == BuiltIn));
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

/// <summary>V29 #87, Grok's review: when a direct brightness change is trusted, and when a screen goes to the key route.</summary>
public class DirectBrightnessTests
{
    private sealed class FakeScreen
    {
        public float Value = 0.5f;
        public bool GetFails, SetFails, Lags;
        public int Sets;
    }

    private static (DirectBrightness Direct, List<string> Log) Make(Dictionary<uint, FakeScreen> screens, uint builtIn = 1)
    {
        var log = new List<string>();
        var direct = new DirectBrightness(
            d => screens[d].GetFails ? (false, 0f) : (true, screens[d].Value),
            (d, v) => { var s = screens[d]; if (s.SetFails) return false; s.Sets++; if (!s.Lags) s.Value = v; return true; },
            d => d == builtIn,
            (text, _) => log.Add(text));
        return (direct, log);
    }

    [Fact]
    public void AScreenThatAnswers_IsChangedOneStep()
    {
        var screens = new Dictionary<uint, FakeScreen> { [2] = new() };
        var (direct, _) = Make(screens);
        Assert.Equal(DirectBrightness.Outcome.Changed, direct.Step(2, up: true, "test"));
        Assert.Equal(0.5625f, screens[2].Value, 4);
    }

    [Fact]
    public void AScreenThatCantBeRead_GoesToTheKeyRoute_AndIsNeverSetDirectly()
    {
        var screens = new Dictionary<uint, FakeScreen> { [3] = new() { GetFails = true } };
        var (direct, _) = Make(screens);
        Assert.Equal(DirectBrightness.Outcome.NotPossible, direct.Step(3, true, "test"));
        Assert.True(direct.UsesKeyRoute(3));
        Assert.Equal(0, screens[3].Sets);
    }

    [Fact]
    public void ASetThatIsRefused_GoesToTheKeyRoute()
    {
        var screens = new Dictionary<uint, FakeScreen> { [3] = new() { SetFails = true } };
        var (direct, _) = Make(screens);
        Assert.Equal(DirectBrightness.Outcome.NotPossible, direct.Step(3, true, "test"));
        Assert.True(direct.UsesKeyRoute(3));
    }

    [Fact]
    public void OneReadBackThatHasntMovedYet_IsNotAFailure_ThreeInARowAre()
    {
        var screens = new Dictionary<uint, FakeScreen> { [2] = new() { Lags = true } };
        var (direct, _) = Make(screens);
        Assert.Equal(DirectBrightness.Outcome.NotSeenYet, direct.Step(2, true, "test"));
        Assert.Equal(DirectBrightness.Outcome.NotSeenYet, direct.Step(2, true, "test"));
        Assert.False(direct.UsesKeyRoute(2));
        Assert.Equal(DirectBrightness.Outcome.NotSeenYet, direct.Step(2, true, "test"));
        Assert.True(direct.UsesKeyRoute(2));
        Assert.Equal(DirectBrightness.Outcome.NotPossible, direct.Step(2, true, "test"));
    }

    [Fact]
    public void ARealMove_ResetsTheCount()
    {
        var screens = new Dictionary<uint, FakeScreen> { [2] = new() { Lags = true } };
        var (direct, _) = Make(screens);
        direct.Step(2, true, "test");
        direct.Step(2, true, "test");
        screens[2].Lags = false;
        Assert.Equal(DirectBrightness.Outcome.Changed, direct.Step(2, true, "test"));
        screens[2].Lags = true;
        direct.Step(2, true, "test");
        direct.Step(2, true, "test");
        Assert.False(direct.UsesKeyRoute(2));
    }

    [Fact]
    public void TheBuiltInScreen_IsNeverMovedToTheKeyRoute_ByReadBacksAlone()
    {
        var screens = new Dictionary<uint, FakeScreen> { [1] = new() { Lags = true } };
        var (direct, _) = Make(screens, builtIn: 1);
        for (int i = 0; i < 6; i++) Assert.Equal(DirectBrightness.Outcome.NotSeenYet, direct.Step(1, false, "test"));
        Assert.False(direct.UsesKeyRoute(1));
    }

    [Fact]
    public void AtTheTop_AnotherUpStep_IsNotCountedAsAMiss()
    {
        var screens = new Dictionary<uint, FakeScreen> { [2] = new() { Value = 1f } };
        var (direct, _) = Make(screens);
        for (int i = 0; i < 5; i++) Assert.Equal(DirectBrightness.Outcome.Changed, direct.Step(2, true, "test"));
        Assert.False(direct.UsesKeyRoute(2));
    }

    [Theory]
    [InlineData("OpenTabletArtist", true)]
    [InlineData("Tablet App for Mac", true)]
    [InlineData("Adobe Photoshop 2026", false)]
    [InlineData(null, false)]
    public void OurOwnWindows_NeverCountAsTheFrontWindow(string? owner, bool own)
    {
        Assert.Equal(own, MacOSSystemKeys.IsOwnApp(owner));
    }
}
