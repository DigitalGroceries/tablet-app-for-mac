using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Newtonsoft.Json.Linq;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Item 21 (#26): pen serials don't leak into portable backups.</summary>
[Collection("AppSettings")]
public class PortablePenSerialTests : IDisposable
{
    readonly string _tmp = Directory.CreateTempSubdirectory("ota-penserial").FullName;
    readonly Func<string> _a = SettingsBundle.AppDataDir, _o = SettingsBundle.OtdDataDir, _h = SettingsBundle.HomeDir;

    public PortablePenSerialTests()
    {
        Directory.CreateDirectory(Path.Combine(_tmp, "app")); Directory.CreateDirectory(Path.Combine(_tmp, "otd"));
        SettingsBundle.AppDataDir = () => Path.Combine(_tmp, "app"); SettingsBundle.OtdDataDir = () => Path.Combine(_tmp, "otd");
        SettingsBundle.HomeDir = () => "/Users/zak";
    }

    public void Dispose()
    {
        SettingsBundle.AppDataDir = _a; SettingsBundle.OtdDataDir = _o; SettingsBundle.HomeDir = _h;
        try { Directory.Delete(_tmp, true); } catch { }
    }

    const string Pens = "[{\"Key\":\"123456789\",\"Serial\":123456789,\"RawToolId\":2050,\"DisplayName\":\"Grip Pen\"}]";

    [Fact]
    public void PortableExport_BlanksPenSerials_FullBackupKeepsThem()
    {
        var app = new JObject { ["PerPenTables:Wacom PTK-440"] = Pens, ["MainPen:Wacom PTK-440"] = "123456789", ["ui.language"] = "en" };
        File.WriteAllText(Path.Combine(_tmp, "app", "settings.json"), app.ToString());
        string Read(string file)
        {
            using var zip = ZipFile.OpenRead(file);
            using var r = new StreamReader(zip.GetEntry("app-settings.json")!.Open());
            return r.ReadToEnd();
        }
        var portable = Path.Combine(_tmp, "p.tabletsettings");
        SettingsBundle.Export(portable, portable: true);
        var text = Read(portable);
        Assert.DoesNotContain("123456789", text);
        Assert.Contains("Grip Pen", text);   // the pen's settings themselves still travel

        var full = Path.Combine(_tmp, "f.tabletsettings");
        SettingsBundle.Export(full, portable: false);
        Assert.Contains("123456789", Read(full));
    }

    [Fact]
    public void RedactedPenKeys_AreNotImported()
    {
        Assert.True(SettingsBundle.IsRedactedPenKey("MainPen:T", "[removed]"));
        Assert.True(SettingsBundle.IsRedactedPenKey("PerPenTables:T", "[{\"Key\":\"[removed]\"}]"));
        Assert.False(SettingsBundle.IsRedactedPenKey("PerPenTables:T", Pens));
        Assert.False(SettingsBundle.IsRedactedPenKey("ui.language", "[removed]"));
    }

    [Fact]
    public void NumericSerial_InDriverSettings_IsRedacted()
    {
        var t = JToken.Parse("{\"Profiles\":[{\"Tablet\":\"A\",\"SerialNumber\":12345}]}");
        SettingsBundle.RedactSerials(t);
        Assert.Equal("[removed]", (string?)t.SelectToken("Profiles[0].SerialNumber"));
    }
}
