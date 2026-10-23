using System;
using System.IO;
using Newtonsoft.Json.Linq;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Build R: rename to "Tablet App for Mac" — settings copied once from the old folder.</summary>
public class BuildRTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ota-r-" + Guid.NewGuid().ToString("N"));
    private string Old => Path.Combine(_root, "OpenTabletArtist");
    private string New => Path.Combine(_root, "Tablet App for Mac");
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private void WriteOld(JObject o) { Directory.CreateDirectory(Old); File.WriteAllText(Path.Combine(Old, "settings.json"), o.ToString()); }

    [Fact]
    public void Migrate_CopiesOnlyTheListedKeys_AndKeepsTheOldFolder()
    {
        WriteOld(new JObject
        {
            ["RingModes:abc"] = "[1]", ["RingModesBackup:abc"] = "b", ["MapAllDisplays:abc"] = "true",
            ["AllDisplaysForceProportions"] = "false", ["RingModeHud"] = "true", ["Theme"] = "Dark",
            ["AutoStartChoice"] = "auto", ["AutoStartPrompted"] = "true",
        });
        var n = SettingsMigration.Migrate(Old, New);
        var got = JObject.Parse(File.ReadAllText(Path.Combine(New, "settings.json")));
        Assert.Equal(6, n);
        Assert.Equal("[1]", (string?)got["RingModes:abc"]);
        Assert.Equal("b", (string?)got["RingModesBackup:abc"]);
        Assert.Equal("true", (string?)got["MapAllDisplays:abc"]);
        Assert.Equal("false", (string?)got["AllDisplaysForceProportions"]);
        Assert.Equal("true", (string?)got["RingModeHud"]);
        Assert.Equal("auto", (string?)got[SettingsMigration.LegacyAutoStartKey]);
        Assert.Null(got["Theme"]);
        Assert.Null(got["AutoStartPrompted"]);
        Assert.True(File.Exists(Path.Combine(Old, "settings.json")));
    }

    private string RDir => Path.Combine(_root, "Free Tablet App for Mac");
    private string R2 => Path.Combine(_root, "Tablet App for Mac");

    [Fact]
    public void R2_CopiesBuildRsFolderWhole_WithUnknownFields_AndKeepsIt()
    {
        Directory.CreateDirectory(Path.Combine(RDir, "sub"));
        var json = new JObject { ["RingModes:abc"] = "[1]", ["PerAppTables"] = new JObject { ["com.x"] = 1 }, ["Theme"] = "Dark" }.ToString();
        File.WriteAllText(Path.Combine(RDir, "settings.json"), json);
        File.WriteAllText(Path.Combine(RDir, "sub", "x.txt"), "x");
        WriteOld(new JObject { ["RingModes:abc"] = "[9]" });
        Assert.Equal(2, SettingsMigration.Migrate(RDir, Old, R2));
        Assert.Equal(json, File.ReadAllText(Path.Combine(R2, "settings.json")));
        Assert.Equal("x", File.ReadAllText(Path.Combine(R2, "sub", "x.txt")));
        Assert.True(File.Exists(Path.Combine(RDir, "settings.json")));
        Assert.True(File.Exists(Path.Combine(Old, "settings.json")));
    }

    [Fact]
    public void R2_FallsBackToTheOpenTabletArtistFolder_WhenNoBuildRFolder()
    {
        WriteOld(new JObject { ["RingModeHud"] = "true", ["Theme"] = "Dark" });
        Assert.Equal(1, SettingsMigration.Migrate(RDir, Old, R2));
        var got = JObject.Parse(File.ReadAllText(Path.Combine(R2, "settings.json")));
        Assert.Equal("true", (string?)got["RingModeHud"]);
        Assert.Null(got["Theme"]);
    }

    [Fact]
    public void R2_DoesNothing_WhenTheNewFolderExists()
    {
        Directory.CreateDirectory(RDir); File.WriteAllText(Path.Combine(RDir, "settings.json"), "{}");
        Directory.CreateDirectory(R2);
        Assert.Equal(-1, SettingsMigration.Migrate(RDir, Old, R2));
        Assert.False(File.Exists(Path.Combine(R2, "settings.json")));
    }

    [Fact]
    public void Migrate_DoesNothing_WhenTheNewFolderExists()
    {
        WriteOld(new JObject { ["RingModeHud"] = "true" });
        Directory.CreateDirectory(New);
        Assert.Equal(-1, SettingsMigration.Migrate(Old, New));
        Assert.False(File.Exists(Path.Combine(New, "settings.json")));
    }

    [Fact]
    public void Migrate_DoesNothing_WithoutAnOldFolder() =>
        Assert.Equal(-1, SettingsMigration.Migrate(Old, New));

    [Fact]
    public void Migrate_UnreadableOldSettings_DoesNotThrow()
    {
        Directory.CreateDirectory(Old);
        File.WriteAllText(Path.Combine(Old, "settings.json"), "{not json");
        Assert.Equal(-1, SettingsMigration.Migrate(Old, New));
    }

    private sealed class FakeSvc : ILoginItemService
    {
        public bool Supported = true; public int Enables, Disables;
        public bool IsSupported => Supported;
        public LoginItemStatus Status => LoginItemStatus.NotRegistered;
        public bool TryEnable(out string? e) { Enables++; e = null; return true; }
        public bool TryDisable(out string? e) { Disables++; e = null; return true; }
        public void OpenLoginItemsSettings() { }
    }

    [Theory]
    [InlineData("auto", 1, 0)]
    [InlineData("manual", 0, 1)]
    public void LegacyChoice_IsCarriedOverOnce_WithoutAsking(string legacy, int enables, int disables)
    {
        var store = new System.Collections.Generic.Dictionary<string, string> { [SettingsMigration.LegacyAutoStartKey] = legacy };
        var svc = new FakeSvc();
        var c = new AutoStartCoordinator(svc, k => store.TryGetValue(k, out var v) ? v : null, (k, v) => store[k] = v);
        Assert.True(c.ApplyLegacyChoice());
        Assert.False(c.ShouldPrompt);
        Assert.Equal(enables, svc.Enables); Assert.Equal(disables, svc.Disables);
        Assert.False(c.ApplyLegacyChoice());
    }

    [Fact]
    public void NoLegacyChoice_StillAsks()
    {
        var store = new System.Collections.Generic.Dictionary<string, string>();
        var c = new AutoStartCoordinator(new FakeSvc(), k => store.TryGetValue(k, out var v) ? v : null, (k, v) => store[k] = v);
        Assert.False(c.ApplyLegacyChoice());
        Assert.True(c.ShouldPrompt);
    }

    [Fact]
    public void ProductName_AndFolderNames()
    {
        Assert.Equal("Tablet App for Mac", AppPaths.ProductName);
        Assert.Equal("OpenTabletArtist", AppPaths.LegacyFolderName);
        Assert.DoesNotContain("Wacom", AppPaths.ProductName);
        Assert.DoesNotContain("OpenTabletArtist", AppPaths.ProductName);
    }
}
