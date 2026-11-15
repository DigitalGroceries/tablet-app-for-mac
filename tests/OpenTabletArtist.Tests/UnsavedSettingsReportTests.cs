using System;
using System.IO;
using System.IO.Compression;
using Newtonsoft.Json.Linq;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>TASKS #81 (b): saved settings vs the settings in use, with the differences.</summary>
public class UnsavedSettingsReportTests
{
    const string Saved = """{"Profiles":[{"Tablet":"Intuos","Bindings":{"TipActivationThreshold":1.0,"PenButtons":["Right click"]}}],"LockUsableAreaDisplay":true}""";
    const string Live = """{"Profiles":[{"Tablet":"Intuos","Bindings":{"TipActivationThreshold":5.0,"PenButtons":["Double click"],"EraserButtons":[]}}]}""";

    [Fact]
    public void Diff_ListsChangedAddedAndRemovedValues()
    {
        var diff = UnsavedSettingsReport.Diff(JToken.Parse(Saved), JToken.Parse(Live));
        Assert.Contains("~ Profiles[0].Bindings.TipActivationThreshold: 1.0 → 5.0", diff);
        Assert.Contains("~ Profiles[0].Bindings.PenButtons[0]: \"Right click\" → \"Double click\"", diff);
        Assert.Contains("+ Profiles[0].Bindings.EraserButtons: []", diff);
        Assert.Contains("- LockUsableAreaDisplay: true", diff);
        Assert.Equal(4, diff.Count);
    }

    [Fact]
    public void Diff_SameSettings_IsEmpty() =>
        Assert.Empty(UnsavedSettingsReport.Diff(JToken.Parse(Saved), JToken.Parse(Saved)));

    [Fact]
    public void Format_SaysTheStateAndTheDifferences()
    {
        var text = UnsavedSettingsReport.Format(new SettingsState("Unsaved", true, Live), Saved, Live);
        Assert.Contains("Save state: Unsaved (there are unsaved changes)", text);
        Assert.Contains("4 difference(s) between the saved file (otd-settings.json) and the settings in use (live-otd-settings.json):", text);
        Assert.Contains("The settings in use are the same as the saved file.",
            UnsavedSettingsReport.Format(new SettingsState("Saved", false, Saved), Saved, Saved));
    }

    [Fact]
    public void Format_MissingPieces_AreSaidPlainly()
    {
        Assert.Contains("not available", UnsavedSettingsReport.Format(null, Saved, null));
        Assert.Contains("Working settings: none", UnsavedSettingsReport.Format(new SettingsState("None", false, null), Saved, null));
        Assert.Contains("Saved settings file: not found", UnsavedSettingsReport.Format(new SettingsState("Unsaved", true, Live), null, Live));
        Assert.Contains("couldn't be compared", UnsavedSettingsReport.Format(new SettingsState("Unsaved", true, "{"), Saved, "{"));
        Assert.Contains("The working settings couldn't be read: IOException: x",
            UnsavedSettingsReport.Format(new SettingsState("(unknown)", false, null, "IOException: x"), Saved, null));
    }

    [Fact]
    public void AddTo_PutsBothFilesInTheZip_WithSerialsAndHomeRemoved()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var live = new JObject
        {
            ["SerialNumber"] = "8HIS003190",
            ["Path"] = Path.Combine(home, "Pictures", "x.png"),
        }.ToString();
        var dir = Directory.CreateTempSubdirectory().FullName;
        var zip = DiagnosticBundle.Create(dir, new[] { "Intuos" }, Array.Empty<string>(), "1.0",
            inputDevices: () => "", permissions: () => "", eventTaps: () => "", tabletInterfaces: () => "");
        UnsavedSettingsReport.AddTo(zip, new SettingsState("Unsaved", true, live), () => "{}");
        using var z = ZipFile.OpenRead(zip);
        string Read(string n) { using var r = new StreamReader(z.GetEntry(n)!.Open()); return r.ReadToEnd(); }
        var diff = Read(UnsavedSettingsReport.FileName);
        var json = Read(UnsavedSettingsReport.LiveFileName);
        Assert.Contains("Save state: Unsaved", diff);
        Assert.DoesNotContain("8HIS003190", diff + json);
        Assert.DoesNotContain(home, diff + json);
    }

    [Fact]
    public void AddTo_ABadSavedFile_StillWritesTheNote()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var zip = DiagnosticBundle.Create(dir, new[] { "Intuos" }, Array.Empty<string>(), "1.0",
            inputDevices: () => "", permissions: () => "", eventTaps: () => "", tabletInterfaces: () => "");
        UnsavedSettingsReport.AddTo(zip, new SettingsState("Unsaved", true, Live), () => throw new IOException("disk"));
        using var z = ZipFile.OpenRead(zip);
        using var r = new StreamReader(z.GetEntry(UnsavedSettingsReport.FileName)!.Open());
        Assert.Contains("The settings comparison failed: IOException", r.ReadToEnd());
    }

    [Fact]
    public void Capture_WithoutASession_IsNull() => Assert.Null(UnsavedSettingsReport.Capture(null));
}
