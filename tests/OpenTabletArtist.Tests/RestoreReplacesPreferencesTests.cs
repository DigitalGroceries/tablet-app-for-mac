using System;
using System.Collections.Generic;
using System.IO;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Item 17 (#26): restoring a backup removes preferences added after it, keeps this Mac's own keys,
/// and the preview shows what will be removed.</summary>
[Collection("AppSettings")]
public class RestoreReplacesPreferencesTests : IDisposable
{
    readonly string _tmp = Directory.CreateTempSubdirectory("ota-restore").FullName;
    readonly Func<string> _a = SettingsBundle.AppDataDir, _o = SettingsBundle.OtdDataDir, _h = SettingsBundle.HomeDir;

    public RestoreReplacesPreferencesTests()
    {
        Directory.CreateDirectory(Path.Combine(_tmp, "otd"));
        SettingsBundle.AppDataDir = () => Path.Combine(_tmp, "app");
        SettingsBundle.OtdDataDir = () => Path.Combine(_tmp, "otd");
        SettingsBundle.HomeDir = () => "/Users/zak";
    }

    public void Dispose()
    {
        SettingsBundle.AppDataDir = _a; SettingsBundle.OtdDataDir = _o; SettingsBundle.HomeDir = _h;
        try { Directory.Delete(_tmp, true); } catch { }
    }

    [Fact]
    public void ReplaceAll_RemovesNewerChoices_KeepsMachineKeys()
    {
        var obj = new Newtonsoft.Json.Linq.JObject { ["PerAppTables:T"] = "[added after backup]", ["daemon.userPath"] = "/keep/me", ["ui.language"] = "en" };
        AppSettings.Replace(obj, new Dictionary<string, string> { ["ui.language"] = "fr" }, SettingsBundle.IsMachineKey);
        Assert.Null(obj["PerAppTables:T"]);
        Assert.Equal("fr", (string?)obj["ui.language"]);
        Assert.Equal("/keep/me", (string?)obj["daemon.userPath"]);
    }

    [Fact]
    public void Preview_ShowsPreferencesThatWillBeRemoved()
    {
        Directory.CreateDirectory(Path.Combine(_tmp, "app"));
        File.WriteAllText(Path.Combine(_tmp, "app", "settings.json"), "{\"ui.language\":\"en\"}");
        var file = Path.Combine(_tmp, "b.tabletsettings");
        SettingsBundle.Export(file, portable: true);
        File.WriteAllText(Path.Combine(_tmp, "app", "settings.json"), "{\"ui.language\":\"en\",\"PerAppTables:T\":\"[x]\"}");
        var p = SettingsBundle.Preview(SettingsBundle.Read(file));
        Assert.Contains(p.Changes, c => c.Kind == "removed");
        Assert.True(p.HasChanges);
    }
}
