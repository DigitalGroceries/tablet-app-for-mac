using System;
using System.IO;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Item 20 (#26): when the app preferences can't be written, the import fails loudly and the driver
/// settings aren't touched.</summary>
[Collection("AppSettings")]
public class ImportPreferenceWriteFailureTests : IDisposable
{
    readonly string _tmp = Directory.CreateTempSubdirectory("ota-prefwrite").FullName;
    readonly Func<string> _a = SettingsBundle.AppDataDir, _o = SettingsBundle.OtdDataDir, _h = SettingsBundle.HomeDir;
    readonly Func<System.Collections.Generic.IReadOnlyDictionary<string, string>, Func<string, bool>, bool> _w = SettingsBundle.WritePreferences;
    string Otd => Path.Combine(_tmp, "otd");

    public ImportPreferenceWriteFailureTests()
    {
        Directory.CreateDirectory(Otd); Directory.CreateDirectory(Path.Combine(_tmp, "app"));
        SettingsBundle.AppDataDir = () => Path.Combine(_tmp, "app"); SettingsBundle.OtdDataDir = () => Otd;
        SettingsBundle.HomeDir = () => "/Users/zak";
    }

    public void Dispose()
    {
        SettingsBundle.AppDataDir = _a; SettingsBundle.OtdDataDir = _o; SettingsBundle.HomeDir = _h;
        SettingsBundle.WritePreferences = _w;
        try { Directory.Delete(_tmp, true); } catch { }
    }

    [Fact]
    public void FailedPreferenceWrite_Throws_AndLeavesDriverSettingsAlone()
    {
        var b = new SettingsBundle.Bundle();
        b.Files["app-settings.json"] = "{\"ui.language\":\"fr\"}";
        b.Files["otd-settings.json"] = "{\"Profiles\":[{\"Tablet\":\"NEW\"}]}";
        File.WriteAllText(Path.Combine(Otd, "settings.json"), "{\"Profiles\":[{\"Tablet\":\"OLD\"}]}");
        SettingsBundle.WritePreferences = (_, _) => false;

        Assert.Throws<IOException>(() => SettingsBundle.Apply(b));
        Assert.Contains("OLD", File.ReadAllText(Path.Combine(Otd, "settings.json")));
    }

    /// <summary>Review of #49/#52: the write seam must default to the item 17 replace, not a merge.</summary>
    [Fact]
    public void Seam_DefaultsToReplaceAll()
    {
        var m = _w.Method;
        Assert.Equal(typeof(AppSettings), m.DeclaringType);
        Assert.Equal(nameof(AppSettings.ReplaceAll), m.Name);
    }

    /// <summary>Review of #49 + #53 together: a portable import must NOT wipe this Mac's own pen settings.</summary>
    [Fact]
    public void PortableImport_KeepsLocalPenSettings_WhileReplacingTheRest()
    {
        const string localPens = "[{\"Key\":\"987\",\"Serial\":987,\"DisplayName\":\"My pen\"}]";
        var local = new Newtonsoft.Json.Linq.JObject
        {
            ["PerPenTables:Wacom PTK-440"] = localPens, ["MainPen:Wacom PTK-440"] = "987",
            ["PerAppTables:T"] = "[added after backup]", ["daemon.userPath"] = "/keep/me", ["ui.language"] = "en",
        };
        // A portable export of another Mac: pens redacted (one with Serial but no Key).
        var exported = new Newtonsoft.Json.Linq.JObject
        {
            ["PerPenTables:Wacom PTK-440"] = "[{\"Serial\":555,\"DisplayName\":\"Their pen\"}]",
            ["MainPen:Wacom PTK-440"] = "555", ["ui.language"] = "fr",
        };
        SettingsBundle.RedactPenSerials(exported);
        var b = new SettingsBundle.Bundle();
        b.Files["app-settings.json"] = exported.ToString();
        SettingsBundle.WritePreferences = (values, keep) => { AppSettings.Replace(local, values, keep); return true; };

        SettingsBundle.Apply(b);

        Assert.Equal(localPens, (string?)local["PerPenTables:Wacom PTK-440"]);   // kept, not wiped
        Assert.Equal("987", (string?)local["MainPen:Wacom PTK-440"]);
        Assert.Equal("fr", (string?)local["ui.language"]);                       // imported
        Assert.Null(local["PerAppTables:T"]);                                    // item 17 still replaces
        Assert.Equal("/keep/me", (string?)local["daemon.userPath"]);             // machine key kept
    }

    [Fact]
    public void PenWithSerialButNoKey_ReadsAsRedacted()
    {
        var o = new Newtonsoft.Json.Linq.JObject { ["PerPenTables:T"] = "[{\"Serial\":555}]" };
        SettingsBundle.RedactPenSerials(o);
        Assert.True(SettingsBundle.IsRedactedPenKey("PerPenTables:T", (string)o["PerPenTables:T"]!));
        Assert.DoesNotContain("555", (string)o["PerPenTables:T"]!);
    }
}
