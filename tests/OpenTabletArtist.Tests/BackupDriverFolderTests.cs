using System.IO;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Item 16 (#26): backups use the connected daemon's data folder (e.g. --appdata), not this process's default.</summary>
public class BackupDriverFolderTests
{
    [Fact]
    public void DaemonReportedFolder_Wins()
    {
        var custom = Path.Combine(Path.GetTempPath(), "TabletData");
        Assert.Equal(custom, SettingsBundle.ResolveOtdDir(Path.Combine(custom, "settings.json"), () => "/default/OpenTabletDriver"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NotConnected_FallsBackToDefault(string? reported)
        => Assert.Equal("/default/OpenTabletDriver", SettingsBundle.ResolveOtdDir(reported, () => "/default/OpenTabletDriver"));
}
