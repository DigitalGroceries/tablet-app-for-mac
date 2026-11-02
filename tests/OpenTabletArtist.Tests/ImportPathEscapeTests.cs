using System;
using System.IO;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Item 22 (#26): a crafted backup can't write outside Presets/ or Configurations/.</summary>
[Collection("AppSettings")]
public class ImportPathEscapeTests : IDisposable
{
    readonly string _tmp = Directory.CreateTempSubdirectory("ota-escape").FullName;
    readonly Func<string> _a = SettingsBundle.AppDataDir, _o = SettingsBundle.OtdDataDir, _h = SettingsBundle.HomeDir;
    string Otd => Path.Combine(_tmp, "otd");
    string Presets => Path.Combine(Otd, "Presets");

    public ImportPathEscapeTests()
    {
        Directory.CreateDirectory(Presets); Directory.CreateDirectory(Path.Combine(_tmp, "app"));
        SettingsBundle.AppDataDir = () => Path.Combine(_tmp, "app"); SettingsBundle.OtdDataDir = () => Otd;
        SettingsBundle.HomeDir = () => "/Users/zak";
    }

    public void Dispose()
    {
        SettingsBundle.AppDataDir = _a; SettingsBundle.OtdDataDir = _o; SettingsBundle.HomeDir = _h;
        try { Directory.Delete(_tmp, true); } catch { }
    }

    [Theory]
    [InlineData("../evil.json")]
    [InlineData("../../evil.json")]
    [InlineData("a/../../evil.json")]
    [InlineData("/etc/evil.json")]
    [InlineData("~/evil.json")]
    [InlineData("a\\..\\..\\evil.json")]
    [InlineData("")]
    [InlineData("a//b.json")]
    [InlineData("./x.json")]
    [InlineData("script.sh")]
    public void BadNames_AreRefused(string name) => Assert.Null(SettingsBundle.SafeDestination(Presets, name));

    [Theory]
    [InlineData("PS.json")]
    [InlineData("Wacom/PTK-440.json")]
    public void NormalNames_StayInside(string name)
    {
        var full = SettingsBundle.SafeDestination(Presets, name);
        Assert.NotNull(full);
        Assert.StartsWith(Path.GetFullPath(Presets) + Path.DirectorySeparatorChar, full);
    }

    [Fact]
    public void SymlinkedSubfolder_IsRefused()
    {
        var outside = Directory.CreateDirectory(Path.Combine(_tmp, "outside")).FullName;
        Directory.CreateSymbolicLink(Path.Combine(Presets, "link"), outside);
        Assert.Null(SettingsBundle.SafeDestination(Presets, "link/x.json"));
    }

    [Fact]
    public void Apply_WritesNothingOutside()
    {
        var b = new SettingsBundle.Bundle();
        b.Files["presets/../../escaped.json"] = "{}";
        b.Files["presets/ok.json"] = "{}";
        SettingsBundle.Apply(b);
        Assert.False(File.Exists(Path.Combine(_tmp, "escaped.json")));
        Assert.False(File.Exists(Path.Combine(Otd, "escaped.json")));
        Assert.True(File.Exists(Path.Combine(Presets, "ok.json")));
    }
}
