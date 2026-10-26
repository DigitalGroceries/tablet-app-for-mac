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

/// <summary>Version 14: per-pen settings (Tool-row pen tiles).</summary>
public class BuildV14PenTests
{
    static string Tablet => "TestTablet-PerPen-" + Guid.NewGuid().ToString("N")[..8];

    static OpenTabletDriver.Desktop.Profiles.BindingSettings Bindings(bool tipBound)
    {
        var b = new OpenTabletDriver.Desktop.Profiles.BindingSettings();
        if (tipBound) b.TipButton = new OpenTabletDriver.Desktop.Reflection.PluginSettingStore(typeof(OpenTabletDriver.Desktop.Binding.MouseBinding));
        return b;
    }

    [Fact]
    public void Signal_RoundTrips_AndKeysOnSerial()
    {
        var s = new PenToolSignal(0x1A2B3C, 0x802, Eraser: false);
        Assert.Equal("1A2B3C 802 tip", s.Format());
        Assert.Equal(s, PenToolSignal.Parse(s.Format()));
        var eraser = new PenToolSignal(0x1A2B3C, 0x80A, Eraser: true);
        Assert.Equal(s.Key, eraser.Key); // both ends of one pen
        Assert.Equal("t802", new PenToolSignal(0, 0x802, false).Key);
        Assert.Null(PenToolSignal.Parse("garbage"));
        Assert.Null(PenToolSignal.Parse("0 0 tip"));
    }

    [Fact]
    public void Signal_FileRoundTrip()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "v14-pen-" + Guid.NewGuid().ToString("N"), "pen-tool");
        var s = new PenToolSignal(0xABC, 0x804, true);
        s.Write(path);
        Assert.Equal(s, PenToolSignal.Read(path));
        Assert.False(System.IO.File.Exists(path + ".tmp"));
    }

    [Theory]
    [InlineData(0x802u, "Grip Pen")]
    [InlineData(0x80Au, "Grip Pen")]
    [InlineData(0x804u, "Art Pen")]
    [InlineData(0x902u, "Airbrush Pen")]
    [InlineData(0x4802u, "Classic Pen")]
    [InlineData(0x2802u, "Inking Pen")]
    [InlineData(0x100802u, "Grip Pen")]
    [InlineData(0x123u, "Pen")]
    public void ToolNames_FollowLibwacom(uint id, string name) => Assert.Equal(name, PenBindings.ToolName(id));

    [Fact]
    public void UniqueName_NumbersRepeats()
    {
        Assert.Equal("Grip Pen", PenBindings.UniqueName(0x802, new string[0]));
        Assert.Equal("Grip Pen 2", PenBindings.UniqueName(0x802, new[] { "Grip Pen" }));
        Assert.Equal("Grip Pen 3", PenBindings.UniqueName(0x802, new[] { "Grip Pen", "Grip Pen 2" }));
    }

    [Fact]
    public void Overlay_CopiesOnlyPenParts()
    {
        var target = Bindings(false);
        target.AuxButtons = new OpenTabletDriver.Desktop.Reflection.PluginSettingStoreCollection { null };
        var pen = Bindings(true);
        pen.TipActivationThreshold = 7;
        pen.AuxButtons = new OpenTabletDriver.Desktop.Reflection.PluginSettingStoreCollection();
        PenBindings.Overlay(target, pen);
        Assert.NotNull(target.TipButton);
        Assert.Equal(7, target.TipActivationThreshold);
        Assert.Single(target.AuxButtons); // ExpressKeys untouched
    }

    [Fact]
    public void FirstPen_IsMain_OthersAreAdded_EraserEndIsTheSamePen_PucksIgnored()
    {
        var tablet = Tablet;
        var main = new PenToolSignal(0x111, 0x802, false);
        Assert.Equal(Services.PerPenStore.SeenResult.BecameMainPen, Services.PerPenStore.Seen(tablet, main, Bindings(true), out _));
        Assert.Equal(Services.PerPenStore.SeenResult.MainPen, Services.PerPenStore.Seen(tablet, main with { Eraser = true, RawToolId = 0x80A }, Bindings(true), out _));
        Assert.Empty(Services.PerPenStore.List(tablet));

        var art = new PenToolSignal(0x222, 0x804, false);
        Assert.Equal(Services.PerPenStore.SeenResult.Added, Services.PerPenStore.Seen(tablet, art, Bindings(true), out var added));
        Assert.Equal("Art Pen", added!.DisplayName);
        Assert.NotNull(added.Bindings.TipButton); // a copy of the main pen's settings
        Assert.Equal(Services.PerPenStore.SeenResult.Known, Services.PerPenStore.Seen(tablet, art with { Eraser = true, RawToolId = 0x80C }, Bindings(true), out _));

        Assert.Equal(Services.PerPenStore.SeenResult.Ignored, Services.PerPenStore.Seen(tablet, new PenToolSignal(0x333, 0x806, false), Bindings(true), out _));
        Assert.Single(Services.PerPenStore.List(tablet));

        Assert.True(Services.PerPenStore.Remove(tablet, art.Key));
        Assert.Empty(Services.PerPenStore.List(tablet));
    }

    [Fact]
    public void SecondGripPen_IsNumbered()
    {
        var tablet = Tablet;
        Services.PerPenStore.Seen(tablet, new PenToolSignal(1, 0x802, false), Bindings(false), out _);
        Services.PerPenStore.Seen(tablet, new PenToolSignal(2, 0x802, false), Bindings(false), out var a);
        Services.PerPenStore.Seen(tablet, new PenToolSignal(3, 0x802, false), Bindings(false), out var b);
        Assert.Equal("Grip Pen", a!.DisplayName);
        Assert.Equal("Grip Pen 2", b!.DisplayName);
    }
}
