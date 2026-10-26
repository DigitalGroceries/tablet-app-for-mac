using System;
using System.Collections.Generic;
using System.Linq;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Views;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Version 14: setup no longer reopens for an empty Wacom Applications folder; the Applications
/// picker takes several apps at once.</summary>
public class BuildV14Tests
{
    static IEnumerable<string> None(string _) => Array.Empty<string>();

    [Fact]
    public void EmptyWacomApplicationsFolder_DoesNotOpenSetup()
    {
        // Zak's Mac, 26 Oct 2026: /Applications/Wacom Tablet.localized is left behind empty and belongs to the
        // system, so "Remove leftovers" can't delete it. It used to reopen setup at every start.
        bool Exists(string p) => p == WacomDriverCheck.ApplicationsLocalized;
        Assert.False(SetupWindow.WacomBlocksSetup(Exists, None, Array.Empty<string>()));
        // The old check still lists it (Home's leftover note and the remover use their own lists).
        Assert.Contains(WacomDriverCheck.ApplicationsLocalized, WacomDriverCheck.Find(Exists, None, Array.Empty<string>()));
    }

    [Fact]
    public void WacomUtilityInsideTheFolder_StillOpensSetup()
    {
        bool Exists(string p) => p == WacomDriverCheck.ApplicationsLocalized || p == WacomDriverCheck.UtilityApp;
        Assert.True(SetupWindow.WacomBlocksSetup(Exists, None, Array.Empty<string>()));
    }

    [Fact]
    public void RunningWacomDriver_StillOpensSetup()
    {
        Assert.True(SetupWindow.WacomBlocksSetup(_ => false, None, new[] { "WacomTabletDriver" }));
    }

    [Fact]
    public void WacomLaunchAgent_StillOpensSetup()
    {
        IEnumerable<string> List(string d) => d == "/Library/LaunchAgents"
            ? new[] { "/Library/LaunchAgents/com.wacom.wacomtablet.plist" } : Array.Empty<string>();
        Assert.True(SetupWindow.WacomBlocksSetup(_ => false, List, Array.Empty<string>()));
    }

    [Fact]
    public void PickedApps_SkipNonAppsAndDuplicates_KeepOrder()
    {
        var known = new Dictionary<string, MacAppCandidate>
        {
            ["/Applications/Affinity.app"] = new("com.seriflabs.affinity", "Affinity", "/Applications/Affinity.app"),
            ["/Applications/Rhino 8.app"] = new("com.mcneel.rhinoceros.8", "Rhino 8", "/Applications/Rhino 8.app"),
            ["/Applications/Affinity copy.app"] = new("COM.SERIFLABS.AFFINITY", "Affinity copy", "/Applications/Affinity copy.app"),
        };
        var picked = AddApplicationDialog.Pick(
            new[] { "/Applications/Affinity.app", "/Applications/Utilities", "/Applications/Rhino 8.app", "/Applications/Affinity copy.app" },
            p => known.TryGetValue(p, out var c) ? c : null);
        Assert.Equal(new[] { "Affinity", "Rhino 8" }, picked.Select(c => c.DisplayName));
    }

    [Fact]
    public void PickedApps_WithoutBundleId_AreSkipped()
    {
        var picked = AddApplicationDialog.Distinct(new[] { new MacAppCandidate("", "Broken", "/x.app"), new MacAppCandidate("a.b", "Ok", "/y.app") });
        Assert.Single(picked);
        Assert.Equal("Ok", picked[0].DisplayName);
    }
}
