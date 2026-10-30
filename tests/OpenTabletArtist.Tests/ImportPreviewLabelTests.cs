using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

public class ImportPreviewLabelTests
{
    [Theory]
    [InlineData("RingModes:Wacom PTK-440:0", "ring", "Wacom PTK-440", null)]
    [InlineData("RingModes:Wacom PTK-440:1", "ring", "Wacom PTK-440", "#2")]
    [InlineData("RingModes:Wacom PTK-440:0:app:com.adobe.Photoshop", "ring", "Wacom PTK-440", "com.adobe.Photoshop")]
    [InlineData("RingModesBackup:Wacom PTK-440:0", "ringbackup", "Wacom PTK-440", null)]
    [InlineData("PerPenTables:Wacom PTK-440", "pen", "Wacom PTK-440", null)]
    [InlineData("MainPen:Wacom PTK-440", "mainpen", "Wacom PTK-440", null)]
    [InlineData("PerAppTables:Wacom PTK-440", "app", "Wacom PTK-440", null)]
    [InlineData("Theme", "pref", "Theme", null)]
    public void Keys_SplitIntoDistinctLines(string key, string area, string item, string? detail)
        => Assert.Equal((area, item, detail), SettingsBundle.DescribeAppKey(key));

    [Fact]
    public void SameTablet_DifferentKeys_GiveDifferentRows()
    {
        var keys = new[] { "RingModes:T:0", "RingModesBackup:T:0", "PerPenTables:T", "MainPen:T", "PerAppTables:T", "PerAppAllBindings:T" };
        var rows = keys.Select(k => { var (a, i, d) = SettingsBundle.DescribeAppKey(k); return ViewModels.BackupViewModel.RowText(new SettingsChange(a, i, "changed", d)); }).ToList();
        Assert.Equal(rows.Count, rows.Distinct().Count());
        Assert.Contains("Changed  T - Touch Ring modes", rows);
        Assert.Contains("Changed  T - Pen settings", rows);
    }
}
