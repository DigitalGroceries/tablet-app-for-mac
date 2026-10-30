using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Newtonsoft.Json.Linq;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Settings export/import, diagnostic redaction and the running-process conflict check
/// (ideas from MockTab by Cyzor, GPL-3).</summary>
[Collection("AppSettings")]
public class SafetyUpdateTests : IDisposable
{
    readonly string _tmp = Directory.CreateTempSubdirectory("ota-safety").FullName;
    readonly Func<string> _a = SettingsBundle.AppDataDir, _o = SettingsBundle.OtdDataDir, _h = SettingsBundle.HomeDir;
    string App => Path.Combine(_tmp, "app");
    string Otd => Path.Combine(_tmp, "otd");

    public SafetyUpdateTests()
    {
        Directory.CreateDirectory(App); Directory.CreateDirectory(Path.Combine(Otd, "Presets"));
        SettingsBundle.AppDataDir = () => App; SettingsBundle.OtdDataDir = () => Otd;
        SettingsBundle.HomeDir = () => "/Users/zak";
    }

    public void Dispose()
    {
        SettingsBundle.AppDataDir = _a; SettingsBundle.OtdDataDir = _o; SettingsBundle.HomeDir = _h;
        try { Directory.Delete(_tmp, true); } catch { }
    }

    [Theory]
    [InlineData("Wacom PTK-440", "Intuos4PTK440_30Oct26.tabletsettings")]
    [InlineData("Wacom PTH-660", "PTH660_30Oct26.tabletsettings")]
    [InlineData(null, "TabletApp_30Oct26.tabletsettings")]
    [InlineData("", "TabletApp_30Oct26.tabletsettings")]
    public void SuggestedName_IsModelAndDate(string? tablet, string expected) =>
        Assert.Equal(expected, SettingsBundle.SuggestedFileName(tablet, new DateTime(2026, 10, 30)));

    [Fact]
    public void PortableExport_RemovesSerialsHomeAndMachineKeys()
    {
        File.WriteAllText(Path.Combine(App, "settings.json"),
            "{\"ui.language\":\"en\",\"daemon.userPath\":\"/Users/zak/x\",\"PerAppTables:T\":\"[{\\\"AppPath\\\":\\\"/Users/zak/Apps/Krita.app\\\"}]\"}");
        File.WriteAllText(Path.Combine(Otd, "settings.json"),
            "{\"Profiles\":[{\"Tablet\":\"Wacom PTK-440\",\"SerialNumber\":\"8HAB001234\"}]}");
        File.WriteAllText(Path.Combine(Otd, "Presets", "PS.json"), "{\"a\":1}");
        var file = Path.Combine(_tmp, "x.tabletsettings");
        SettingsBundle.Export(file, portable: true);

        using var zip = ZipFile.OpenRead(file);
        string Read(string n) { using var r = new StreamReader(zip.GetEntry(n)!.Open()); return r.ReadToEnd(); }
        var all = string.Join("\n", zip.Entries.Select(e => Read(e.FullName)));
        Assert.DoesNotContain("8HAB001234", all);
        Assert.DoesNotContain("/Users/zak", all);
        Assert.DoesNotContain("daemon.userPath", all);
        Assert.Contains("~/Apps/Krita.app", all);
        Assert.NotNull(zip.GetEntry("presets/PS.json"));
        Assert.NotNull(zip.GetEntry("manifest.json"));
    }

    [Fact]
    public void Preview_ListsChangesWithoutApplying()
    {
        File.WriteAllText(Path.Combine(Otd, "settings.json"), "{\"Profiles\":[{\"Tablet\":\"A\",\"X\":1}]}");
        File.WriteAllText(Path.Combine(App, "settings.json"), "{\"ui.language\":\"en\"}");
        var file = Path.Combine(_tmp, "x.tabletsettings");
        SettingsBundle.Export(file, portable: true);
        File.WriteAllText(Path.Combine(Otd, "settings.json"), "{\"Profiles\":[{\"Tablet\":\"A\",\"X\":2}]}");

        var p = SettingsBundle.Preview(SettingsBundle.Read(file));
        Assert.Contains(p.Changes, c => c.Area == "tablet" && c.Item == "A" && c.Kind == "changed");
        Assert.Contains(p.Changes, c => c.Item == "ui.language" && c.Kind == "same");
        Assert.Contains("2", File.ReadAllText(Path.Combine(Otd, "settings.json")));   // nothing applied
    }

    [Fact]
    public void Read_RejectsOtherZips()
    {
        var file = Path.Combine(_tmp, "other.zip");
        using (var z = ZipFile.Open(file, ZipArchiveMode.Create)) z.CreateEntry("hello.txt");
        Assert.Throws<InvalidDataException>(() => SettingsBundle.Read(file));
    }

    [Fact]
    public void AutoBackups_KeepTheLastFive()
    {
        File.WriteAllText(Path.Combine(App, "settings.json"), "{}");
        Directory.CreateDirectory(SettingsBundle.BackupsDir);
        for (int i = 0; i < 8; i++)
            File.WriteAllText(Path.Combine(SettingsBundle.BackupsDir, $"auto-2026-10-0{i}-000000-x.tabletsettings"), "");
        SettingsBundle.Prune();
        var left = SettingsBundle.AutoBackups();
        Assert.Equal(5, left.Count);
        Assert.EndsWith("auto-2026-10-07-000000-x.tabletsettings", left[0]);
    }

    [Theory]
    [InlineData("Tablet serial: 8HAB001234 connected", "Tablet serial: [removed] connected")]
    [InlineData("\"SerialNumber\": \"ABC123\"", "\"SerialNumber\": \"[removed]\"")]
    [InlineData("no serials here", "no serials here")]
    public void SerialText_IsRedacted(string input, string expected) =>
        Assert.Equal(expected, SettingsBundle.RedactSerialText(input));

    [Fact]
    public void ProcessMatcher_ExactNamesOnly()
    {
        Assert.Empty(ConflictProcessMatcher.MatchedLabels(new[] { "Xencelabs", "", null, "Wacom", "OpenTabletDriver.Daemon" }));
        Assert.Equal(new[] { "Wacom Tablet Driver", "Wacom I/O Manager", "Xencelabs Driver" },
            ConflictProcessMatcher.MatchedLabels(new[] { "WacomTabletDriver", "Wacom_IOManager", "XencelabsDriver" }));
        Assert.Contains(ConflictProcessMatcher.MatchedLabels(new[] { "OpenTabletDriver.Daemon", "OpenTabletDriver.Daemon" }),
            l => l.StartsWith("OpenTabletDriver Daemon"));
    }
}
