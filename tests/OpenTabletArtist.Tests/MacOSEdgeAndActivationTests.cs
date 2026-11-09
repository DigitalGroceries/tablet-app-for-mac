using System.Collections.Generic;
using System.Numerics;
using OpenTabletDriver.Desktop.Interop.Input;
using Xunit;
using Rect = OpenTabletDriver.Desktop.Interop.Input.MacOSScreenEdge.Rect;
using W = OpenTabletDriver.Desktop.Interop.Input.MacOSWindowPick.Window;

namespace OpenTabletArtist.Tests;

/// <summary>Version 25, second report (PTH-850, macOS 27): the Dock edge clamp (OTD #4726) and the rules for
/// choosing which app to activate under a pen tap.</summary>
public class MacOSEdgeAndActivationTests
{
    static readonly Rect Main = new(Vector2.Zero, new Vector2(1512, 982));
    static readonly Rect Right = new(new Vector2(1512, -200), new Vector2(2560, 1440));
    static readonly Rect Virtual = new(new Vector2(0, -200), new Vector2(4072, 1440));
    const float I = MacOSScreenEdge.Inset;

    [Fact]
    public void Bottom_and_right_edges_are_pulled_in_by_one_64th()
    {
        var p = MacOSScreenEdge.Clamp(new Vector2(1512, 982), new[] { Main }, Main);
        Assert.Equal(1512 - I, p.X);
        Assert.Equal(982 - I, p.Y);
        Assert.True(p.Y > 981.9f);   // still at the edge, so the Dock triggers
    }

    [Fact]
    public void Points_inside_are_unchanged_and_below_minimum_are_raised()
    {
        Assert.Equal(new Vector2(700, 400), MacOSScreenEdge.Clamp(new Vector2(700, 400), new[] { Main }, Main));
        Assert.Equal(Vector2.Zero, MacOSScreenEdge.Clamp(new Vector2(-3, -1), new[] { Main }, Main));
    }

    [Fact]
    public void Each_display_has_its_own_edges()
    {
        var displays = new[] { Main, Right };
        // Bottom of the taller right display: clamp to its bottom, not the main display's.
        var p = MacOSScreenEdge.Clamp(new Vector2(2000, 1240), displays, Virtual);
        Assert.Equal(1240 - I, p.Y);
        Assert.Equal(2000, p.X);
        // Main display's bottom edge.
        Assert.Equal(982 - I, MacOSScreenEdge.Clamp(new Vector2(800, 982), displays, Virtual).Y);
        // Below the main display (dead space next to the taller display) goes to the nearest display.
        var q = MacOSScreenEdge.Clamp(new Vector2(800, 1100), displays, Virtual);
        Assert.Equal(982 - I, q.Y);
    }

    [Fact]
    public void Falls_back_to_the_virtual_screen_without_displays()
    {
        var p = MacOSScreenEdge.Clamp(new Vector2(5000, 5000), null, Virtual);
        Assert.Equal(new Vector2(4072 - I, 1240 - I), p);
    }

    static readonly W DockOverlay = new(10, "Dock", 20, 0, 0, 1512, 982);
    static readonly W DockBar = new(10, "Dock", 20, 400, 900, 700, 82);
    static readonly W Popover = new(30, "Safari", 101, 600, 300, 200, 150);
    static readonly W Safari = new(30, "Safari", 0, 100, 100, 900, 700);
    static readonly W Mail = new(40, "Mail", 0, 50, 50, 1200, 800);
    static readonly W Desktop = new(50, "Finder", -2147483603, 0, 0, 1512, 982);

    [Fact]
    public void Picks_the_front_most_window_under_the_point_skipping_the_Dock_overlay()
    {
        var list = new List<W> { DockOverlay, Safari, Mail, Desktop };
        Assert.Equal(Safari, MacOSWindowPick.Pick(list, new Vector2(200, 200), new[] { Main }));
        Assert.Equal(Mail, MacOSWindowPick.Pick(list, new Vector2(1100, 600), new[] { Main }));
    }

    [Fact]
    public void Desktop_tap_picks_Finders_desktop_window()
    {
        var list = new List<W> { DockOverlay, Safari, Mail, Desktop };
        Assert.Equal(Desktop, MacOSWindowPick.Pick(list, new Vector2(1400, 900), new[] { Main }));
    }

    [Fact]
    public void Panels_and_popovers_are_not_skipped()
    {
        var list = new List<W> { Popover, Safari };
        Assert.Equal(Popover, MacOSWindowPick.Pick(list, new Vector2(650, 350), new[] { Main }));
    }

    [Fact]
    public void The_Dock_bar_itself_is_kept_but_its_overlay_is_not()
    {
        var list = new List<W> { DockOverlay, DockBar, Safari, Desktop };
        Assert.Equal(DockBar, MacOSWindowPick.Pick(list, new Vector2(500, 950), new[] { Main }));
        // Without display bounds every Dock window is skipped.
        Assert.Equal(Desktop, MacOSWindowPick.Pick(list, new Vector2(500, 950)));
    }

    [Fact]
    public void Transparent_or_empty_windows_and_misses_are_skipped()
    {
        var clear = new W(60, "Ghost", 0, 0, 0, 1512, 982, Alpha: 0);
        var empty = new W(61, "Empty", 0, 0, 0, 0, 0);
        Assert.Equal(Safari, MacOSWindowPick.Pick(new List<W> { clear, empty, Safari }, new Vector2(200, 200), new[] { Main }));
        Assert.Null(MacOSWindowPick.Pick(new List<W> { Safari }, new Vector2(5, 5), new[] { Main }));
    }

    static readonly W ServerShield = new(458, MacOSWindowPick.WindowServerName, 2147483630, 0, 0, 1512, 982);
    static readonly W ServerMenuBar = new(458, MacOSWindowPick.WindowServerName, 24, 0, 0, 1512, 33);

    [Fact]
    public void A_full_screen_Window_Server_window_is_skipped_issue_73()
    {
        var list = new List<W> { ServerShield, Safari, Mail, Desktop };
        Assert.Equal(Safari, MacOSWindowPick.Pick(list, new Vector2(200, 200), new[] { Main }));
        Assert.Equal(Desktop, MacOSWindowPick.Pick(list, new Vector2(1400, 900), new[] { Main }));
    }

    [Fact]
    public void A_small_Window_Server_window_still_stops_the_search_issue_73()
    {
        // A tap on the menu bar must not activate the app behind it.
        var list = new List<W> { ServerMenuBar, Desktop };
        Assert.Equal(ServerMenuBar, MacOSWindowPick.Pick(list, new Vector2(5, 5), new[] { Main }));
        // Without display bounds nothing tells a full-screen window from a small one: keep it.
        Assert.Equal(ServerShield, MacOSWindowPick.Pick(new List<W> { ServerShield, Safari }, new Vector2(200, 200)));
    }

    [Fact]
    public void A_display_sized_Window_Server_window_that_only_overlaps_is_kept_issue_73()
    {
        // Same size as the display but shifted: it doesn't cover the display, so it still stops the search.
        var shifted = new W(458, MacOSWindowPick.WindowServerName, 2147483630, 700, 0, 1512, 982);
        Assert.Equal(shifted, MacOSWindowPick.Pick(new List<W> { shifted, Safari }, new Vector2(800, 200), new[] { Main }));
    }
}
