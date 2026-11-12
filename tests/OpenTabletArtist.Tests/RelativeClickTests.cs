using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using OpenTabletDriver.Desktop.Interop.Input;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Version 26 (#36, Maxime, also #73): clicks in Mouse-like (Relative) mode, and buttons held while
/// the driver swaps its virtual mouse on a settings change. The macOS mouse itself can't be built off a Mac
/// (CoreGraphics), so the wiring is checked in the source.</summary>
public class RelativeClickTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private static string Source(string relative) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "external", "OpenTabletDriver", relative));

    private const string Mouse = "OpenTabletDriver.Desktop/Interop/Input/MacOSVirtualMouse.cs";
    private const string Relative = "OpenTabletDriver.Desktop/Interop/Input/Relative/MacOSRelativePointer.cs";
    private const string Daemon = "OpenTabletDriver.Daemon/DriverDaemon.cs";

    /// <summary>What relative mode used to feed the counter: movement, so two taps far apart looked like one spot.</summary>
    [Fact]
    public void Deltas_MadeTapsFarApartCountAsADoubleClick_ScreenPointsDoNot()
    {
        var withDeltas = new DoubleClickState { IntervalMs = 500 };
        Assert.Equal(1, withDeltas.OnMouseDown(new Vector2(0.4f, 0.2f), 0));
        withDeltas.OnMouseUp(80);
        Assert.Equal(2, withDeltas.OnMouseDown(new Vector2(0.3f, -0.1f), 300));   // the old bug: 400 px away, still "double"

        var withScreen = new DoubleClickState { IntervalMs = 500 };
        Assert.Equal(1, withScreen.OnMouseDown(new Vector2(500, 300), 0));
        withScreen.OnMouseUp(80);
        Assert.Equal(1, withScreen.OnMouseDown(new Vector2(900, 300), 300));      // the fix: a new single click
    }

    [Fact]
    public void ADragInRelativeMode_BreaksTheClickCount_WithScreenPoints()
    {
        var s = new DoubleClickState { IntervalMs = 500 };
        Assert.Equal(1, s.OnMouseDown(new Vector2(500, 300), 0));
        s.OnPosition(new Vector2(700, 300), buttonHeld: true);   // dragged 200 px
        s.OnMouseUp(150);
        Assert.Equal(1, s.OnMouseDown(new Vector2(700, 300), 300));
    }

    [Fact]
    public void RelativePointer_CountsClicksAtScreenPoints()
    {
        Assert.Matches(new Regex(@"protected override Vector2 ClickPosition\(float x, float y\)\s*\{\s*var at = GetCursorPosition\(\);"),
            Source(Relative));
        var mouse = Source(Mouse);
        Assert.Contains("_doubleClick.OnPosition(ClickPosition(x, y), buttonHeld:", mouse);
        Assert.Contains("? ClickPosition(_pendingX.Value, _pendingY.Value)", mouse);
        Assert.DoesNotContain("_doubleClick.OnPosition(new Vector2(x, y)", mouse);
    }

    [Fact]
    public void HeldButtons_AreLetGo_BeforeTheDaemonDropsTheOldMouse()
    {
        var daemon = Source(Daemon);
        Assert.Matches(new Regex(@"ReleaseHeldButtons\(dev\.Properties\.Name, dev\.OutputMode\);\s*dev\.OutputMode\?\.Dispose\(\);"), daemon);
        Assert.Contains("HeldButtonRelease.TryRelease(pointer, group, reason);", daemon);   // #73: now one shared, logged helper

        var mouse = Source(Mouse);
        var release = mouse[mouse.IndexOf("public void ReleaseHeldButtons()")..];
        release = release[..release.IndexOf("\n        }")];
        Assert.Contains("QueuePendingPositionFromSystem();", release);     // the up goes where the pointer is now
        Assert.Contains("ProcessKeyStates(_prevButtonStates, 0);", release); // ups for what macOS was told is down
    }
}
