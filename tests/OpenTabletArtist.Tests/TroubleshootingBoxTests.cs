using System.IO;
using System.Linq;
using System.Text.Json;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>#25: Settings › Troubleshooting gathers restart / diagnostic zip / permission reset.</summary>
public class TroubleshootingBoxTests
{
    private static readonly string[] RequiredKeys =
    [
        "settings.tab.troubleshoot",
        "settings.troubleshoot.title",
        "settings.troubleshoot.intro",
        "settings.troubleshoot.restart_title",
        "settings.troubleshoot.restart_body",
        "settings.troubleshoot.restart_button",
        "settings.troubleshoot.diag_title",
        "settings.troubleshoot.diag_body",
        "settings.troubleshoot.diag_button",
        "settings.troubleshoot.perm_title",
        "settings.troubleshoot.perm_body",
        "settings.troubleshoot.perm_button",
    ];

    static string FindRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "OpenTabletArtist/OpenTabletArtist.csproj")))
            d = d.Parent;
        return d!.FullName;
    }

    [Theory]
    [InlineData("en")]
    [InlineData("bg")]
    [InlineData("de")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("ja")]
    [InlineData("ru")]
    [InlineData("zh-CN")]
    public void AllLocales_HaveTroubleshootingKeys(string locale)
    {
        var path = Path.Combine(FindRoot(), "OpenTabletArtist", "Assets", "i18n", $"{locale}.json");
        Assert.True(File.Exists(path), $"Missing locale file: {path}");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var key in RequiredKeys)
            Assert.True(doc.RootElement.TryGetProperty(key, out var v) && v.GetString()?.Length > 0,
                $"{locale} missing {key}");
    }

    [Fact]
    public void TroubleshootingTab_IsCrossPlatform_AndNotPresetBacked()
    {
        Assert.True(SettingsViewModel.TabAppliesToOs(SettingsTab.Troubleshooting, true, false));
        Assert.True(SettingsViewModel.TabAppliesToOs(SettingsTab.Troubleshooting, false, true));
        Assert.True(SettingsViewModel.TabAppliesToOs(SettingsTab.Troubleshooting, false, false));
        Assert.False(SettingsViewModel.TabRescansPresets(SettingsTab.Troubleshooting));
    }

    [Fact]
    public void PermissionReset_StillOffersTheFourTccutilCommands()
    {
        var cmds = PermissionReset.Commands();
        Assert.Equal(4, cmds.Count);
        Assert.Contains(cmds, c => c.SequenceEqual(new[] { "reset", "ListenEvent", PermissionReset.AppBundleId }));
        Assert.Contains(cmds, c => c.SequenceEqual(new[] { "reset", "Accessibility", PermissionReset.AppBundleId }));
    }
}
