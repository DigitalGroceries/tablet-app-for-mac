using System;
using System.IO;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Item 18 (#26): a difference only in top-level driver settings (Tools, usable-area locks) shows in
/// the preview, so Apply is enabled and they can be restored.</summary>
[Collection("AppSettings")]
public class RestoreTopLevelDriverSettingsTests : IDisposable
{
    readonly string _tmp = Directory.CreateTempSubdirectory("ota-toplevel").FullName;
    readonly Func<string> _a = SettingsBundle.AppDataDir, _o = SettingsBundle.OtdDataDir, _h = SettingsBundle.HomeDir;
    string Otd => Path.Combine(_tmp, "otd");

    public RestoreTopLevelDriverSettingsTests()
    {
        Directory.CreateDirectory(Otd); Directory.CreateDirectory(Path.Combine(_tmp, "app"));
        SettingsBundle.AppDataDir = () => Path.Combine(_tmp, "app"); SettingsBundle.OtdDataDir = () => Otd;
        SettingsBundle.HomeDir = () => "/Users/zak";
    }

    public void Dispose()
    {
        SettingsBundle.AppDataDir = _a; SettingsBundle.OtdDataDir = _o; SettingsBundle.HomeDir = _h;
        try { Directory.Delete(_tmp, true); } catch { }
    }

    [Fact]
    public void OnlyALockDiffers_PreviewHasAChange()
    {
        File.WriteAllText(Path.Combine(Otd, "settings.json"), "{\"Profiles\":[{\"Tablet\":\"A\"}],\"LockUsableAreaDisplay\":true}");
        var file = Path.Combine(_tmp, "b.tabletsettings");
        SettingsBundle.Export(file, portable: true);
        File.WriteAllText(Path.Combine(Otd, "settings.json"), "{\"Profiles\":[{\"Tablet\":\"A\"}],\"LockUsableAreaDisplay\":false}");
        var p = SettingsBundle.Preview(SettingsBundle.Read(file));
        Assert.Contains(p.Changes, c => c.Area == "driver" && c.Item == "LockUsableAreaDisplay" && c.Kind == "changed");
        Assert.True(p.HasChanges);
    }

    [Fact]
    public void SameTopLevel_IsSame()
    {
        File.WriteAllText(Path.Combine(Otd, "settings.json"), "{\"Profiles\":[{\"Tablet\":\"A\"}],\"Tools\":[]}");
        var file = Path.Combine(_tmp, "b.tabletsettings");
        SettingsBundle.Export(file, portable: true);
        var p = SettingsBundle.Preview(SettingsBundle.Read(file));
        Assert.Contains(p.Changes, c => c.Area == "driver" && c.Item == "Tools" && c.Kind == "same");
    }

    /// <summary>Review of #50: a setting only in the current driver file is lost on Apply, so it shows as removed.</summary>
    [Fact]
    public void OnlyInCurrentFile_ShowsAsRemoved()
    {
        File.WriteAllText(Path.Combine(Otd, "settings.json"), "{\"Profiles\":[{\"Tablet\":\"A\"}]}");
        var file = Path.Combine(_tmp, "b.tabletsettings");
        SettingsBundle.Export(file, portable: true);
        File.WriteAllText(Path.Combine(Otd, "settings.json"), "{\"Profiles\":[{\"Tablet\":\"A\"}],\"LockUsableAreaDisplay\":true}");
        var p = SettingsBundle.Preview(SettingsBundle.Read(file));
        Assert.Contains(p.Changes, c => c.Area == "driver" && c.Item == "LockUsableAreaDisplay" && c.Kind == "removed");
        Assert.True(p.HasChanges);
    }
}
