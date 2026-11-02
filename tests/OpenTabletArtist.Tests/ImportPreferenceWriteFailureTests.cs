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
    readonly Func<System.Collections.Generic.IReadOnlyDictionary<string, string>, bool> _w = SettingsBundle.WritePreferences;
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
        SettingsBundle.WritePreferences = _ => false;

        Assert.Throws<IOException>(() => SettingsBundle.Apply(b));
        Assert.Contains("OLD", File.ReadAllText(Path.Combine(Otd, "settings.json")));
    }
}
