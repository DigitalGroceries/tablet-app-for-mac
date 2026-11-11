using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>#38 (follow-up to #22/#81): Restore factory settings and Revert to default also reset the app's own
/// switches for a tablet's main page (All buttons, Touch Ring, each key's and ring's Off, saved copies, ring-mode
/// lists), so they match the bindings. Per-app and per-pen tables and other tablets are left alone.</summary>
[Collection("AppSettings")]
public class TabletAppStateTests
{
    private static string NewTablet() => "TestTablet-AppState-" + Guid.NewGuid().ToString("N")[..8];

    private static void Fill(string t)
    {
        AppSettings.SetMany(new Dictionary<string, string>
        {
            [$"AuxEnabled:{t}"] = "false",
            [$"AuxBackup:{t}"] = "[\"saved keys\"]",
            [$"AuxKeyOff:{t}"] = "{\"2\":null}",
            [$"WheelEnabled:{t}"] = "false",
            [$"WheelBackup:{t}"] = "[\"saved ring\"]",
            [$"WheelOff:{t}"] = "{\"1\":{}}",
            [$"TipBackup:{t}"] = "tip",
            [$"RingModes:{t}:0"] = "[modes]",
            [$"RingModesBackup:{t}:0"] = "{backup}",
            [$"MapAllDisplays:{t}"] = "1",
            // per-app / per-pen: not ours to reset here
            [$"RingModes:{t}:0:app:com.apple.safari"] = "[safari modes]",
            [$"AuxKeyOff:{t}:app:com.apple.safari"] = "{\"3\":null}",
            [$"PerAppTables:{t}"] = "[safari]",
        });
    }

    [Fact]
    public void Factory_ResetsTheMainPageSwitches_AndKeepsPerAppTables()
    {
        var t = NewTablet();
        Fill(t);
        Assert.True(TabletAppState.ResetToFactory(new[] { t }));

        foreach (var key in new[] { $"AuxEnabled:{t}", $"AuxBackup:{t}", $"AuxKeyOff:{t}", $"WheelEnabled:{t}", $"WheelBackup:{t}",
                     $"WheelOff:{t}", $"TipBackup:{t}", $"RingModes:{t}:0", $"RingModesBackup:{t}:0", $"MapAllDisplays:{t}" })
            Assert.Null(AppSettings.Get(key));
        Assert.Equal("[safari modes]", AppSettings.Get($"RingModes:{t}:0:app:com.apple.safari"));
        Assert.Equal("{\"3\":null}", AppSettings.Get($"AuxKeyOff:{t}:app:com.apple.safari"));
        Assert.Equal("[safari]", AppSettings.Get($"PerAppTables:{t}"));
    }

    [Fact]
    public void Factory_LeavesOtherTabletsAlone()
    {
        var t = NewTablet(); var other = NewTablet();
        Fill(t); Fill(other);
        TabletAppState.ResetToFactory(new[] { t });
        Assert.Equal("false", AppSettings.Get($"AuxEnabled:{other}"));
        Assert.Equal("{\"1\":{}}", AppSettings.Get($"WheelOff:{other}"));
    }

    [Fact]
    public void Default_SnapshotThenRestore_PutsTheSwitchesBack_AndDropsLaterOnes()
    {
        var t = NewTablet();
        AppSettings.SetMany(new Dictionary<string, string> { [$"AuxEnabled:{t}"] = "false", [$"RingModes:{t}:0"] = "[mine]" });
        var snap = TabletAppState.Snapshot(new[] { t });
        Assert.Equal("false", snap[$"AuxEnabled:{t}"]);
        Assert.Equal("[mine]", snap[$"RingModes:{t}:0"]);
        Assert.DoesNotContain($"WheelOff:{t}", snap.Keys);

        // later changes: switch on, a ring turned off, another list
        AppSettings.SetMany(new Dictionary<string, string>
            { [$"AuxEnabled:{t}"] = "true", [$"WheelOff:{t}"] = "{\"1\":{}}", [$"RingModes:{t}:1"] = "[later]" });

        Assert.True(TabletAppState.Restore(snap, new[] { t }));
        Assert.Equal("false", AppSettings.Get($"AuxEnabled:{t}"));
        Assert.Equal("[mine]", AppSettings.Get($"RingModes:{t}:0"));
        Assert.Null(AppSettings.Get($"WheelOff:{t}"));
        Assert.Null(AppSettings.Get($"RingModes:{t}:1"));
    }

    [Theory]
    [InlineData("RingModes:T:0", true)]
    [InlineData("RingModes:T:12", true)]
    [InlineData("RingModes:T:0:app:com.apple.safari", false)]
    [InlineData("RingModes:T:", false)]
    public void MainScope_IsTheWheelNumberOnly(string key, bool main) =>
        Assert.Equal(main, TabletAppState.IsMainScope(key, "RingModes:T:"));

    private static string RepoRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    [Fact]
    public void FactoryAndDefault_UseIt_AndOpenPagesReloadTheSwitches()
    {
        var main = File.ReadAllText(Path.Combine(RepoRoot(), "OpenTabletArtist", "ViewModels", "MainViewModel.cs"));
        Assert.Contains("TabletAppState.ResetToFactory(TabletsOf(merged));", main);
        Assert.Contains("UserDefaultSettings.SaveAppState(TabletAppState.Snapshot(TabletsOf(settings)));", main);
        Assert.Contains("TabletAppState.Restore(appState, TabletsOf(defaults));", main);
        Assert.Equal(2, CountOf(main, "editor.ReloadAppState();"));
    }

    private static int CountOf(string text, string what)
    {
        int n = 0, i = 0;
        while ((i = text.IndexOf(what, i, StringComparison.Ordinal)) >= 0) { n++; i += what.Length; }
        return n;
    }
}
