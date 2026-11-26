using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>TASKS #212 part 7 (team vote 23 Nov 2026): where the keys sit on the user's tablet goes into a report only
/// when the user ticks it. It is off by default, and without the tick it is not in the settings snapshot either.</summary>
public class KeyOrderInReportTests
{
    private static string Read(ZipArchive z, string name)
    {
        using var r = new StreamReader(z.GetEntry(name)!.Open());
        return r.ReadToEnd();
    }

    [Fact]
    public void TheBoxIsNotTicked()
    {
        var vm = new ReportViewModel(() => (new System.Collections.Generic.List<string>(), Array.Empty<string>(), null), null, "test") { HasKeyOrder = true };
        Assert.False(vm.IncludeKeyOrder);
        Assert.Contains("Set up my buttons", vm.KeyOrderLabel);
        Assert.Contains("nothing about you", vm.KeyOrderHelp);
    }

    [Fact]
    public void WithoutTheTickTheKeyOrderIsTakenOutOfTheSettings()
    {
        var json = "{\"Theme\":\"dark\",\"KeyOrder:Wacom PTK-440\":\"{\\\"v\\\":1,\\\"keys\\\":[2,0,1]}\",\"KeyOrder:Other\":\"x\",\"AuxEnabled:Wacom PTK-440\":\"true\"}";
        var cleaned = DiagnosticBundle.WithoutKeyOrder(json);
        Assert.DoesNotContain("KeyOrder:", cleaned);
        Assert.Contains("Theme", cleaned);
        Assert.Contains("AuxEnabled:Wacom PTK-440", cleaned);
    }

    [Fact]
    public void SettingsThatCannotBeReadAreLeftAsTheyAre()
    {
        Assert.Equal("{not json", DiagnosticBundle.WithoutKeyOrder("{not json"));
        Assert.Equal("[1,2]", DiagnosticBundle.WithoutKeyOrder("[1,2]"));
    }

    [Fact]
    public void TheTextNamesTheModelAndTheKeyNumbersAndNothingElse()
    {
        var text = DiagnosticBundle.KeyOrderText(new[]
        {
            ("Wacom PTK-640", new KeyOrderMap(new[] { 5, 4, KeyOrderMap.NotSeen, 7, 0, 1, 2, 3 }, groups: new[] { 4, 4 })),
            ("Wacom CTH-460", new KeyOrderMap(new[] { 0, 1, 2, 3 }, flipped: true)),
        });
        Assert.Contains("Wacom PTK-640", text);
        Assert.Contains("Keys in the order they sit: 6, 5, -, 8, 1, 2, 3, 4", text);
        Assert.Contains("Groups: 4 + 4", text);
        Assert.Contains("Tablet turned round when mapped: no", text);
        Assert.Contains("Wacom CTH-460", text);
        Assert.Contains("Tablet turned round when mapped: yes", text);
        Assert.True(text.IndexOf("Wacom CTH-460", StringComparison.Ordinal) < text.IndexOf("Wacom PTK-640", StringComparison.Ordinal));   // by name
    }

    [Fact]
    public void NoSavedOrderNoText() => Assert.Equal("", DiagnosticBundle.KeyOrderText(Array.Empty<(string, KeyOrderMap)>()));

    [Fact]
    public void TheReportHoldsTheKeyOrderOnlyWhenAsked()
    {
        var tablet = "TestTablet-KeyReport-" + Guid.NewGuid().ToString("N")[..8];
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            KeyOrderStore.Save(tablet, new KeyOrderMap(new[] { 1, 0 }));
            Assert.True(DiagnosticBundle.HasKeyOrder());

            var plain = DiagnosticBundle.Create(Path.Combine(dir, "a"), new[] { "Intuos" }, Array.Empty<string>(), "1.0",
                inputDevices: () => "", permissions: () => "", eventTaps: () => "", tabletInterfaces: () => "");
            using (var z = ZipFile.OpenRead(plain))
            {
                Assert.Null(z.GetEntry("key-order.txt"));
                foreach (var e in z.Entries.Where(e => e.FullName.StartsWith("settings/", StringComparison.Ordinal)))
                    Assert.DoesNotContain(tablet, Read(z, e.FullName));
                Assert.DoesNotContain(tablet, Read(z, "summary.txt"));
            }

            var asked = DiagnosticBundle.Create(Path.Combine(dir, "b"), new[] { "Intuos" }, Array.Empty<string>(), "1.0",
                inputDevices: () => "", permissions: () => "", eventTaps: () => "", tabletInterfaces: () => "", includeKeyOrder: true);
            using (var z = ZipFile.OpenRead(asked))
            {
                var text = Read(z, "key-order.txt");
                Assert.Contains(tablet, text);
                Assert.Contains("Keys in the order they sit: 2, 1", text);
            }
        }
        finally { KeyOrderStore.Clear(tablet); }
    }
}
