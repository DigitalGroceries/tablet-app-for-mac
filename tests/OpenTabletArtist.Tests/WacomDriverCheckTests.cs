using System;
using System.Collections.Generic;
using System.Linq;
using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.Tests;

public class WacomDriverCheckTests
{
    static IEnumerable<string> None(string _) => Enumerable.Empty<string>();

    [Fact]
    public void CleanMac_FindsNothing()
        => Assert.Empty(WacomDriverCheck.Find(_ => false, None, new[] { "Finder", "Dock" }));

    [Fact]
    public void FindsTheWacomFolder()
        => Assert.Contains("/Applications/Wacom Tablet.localized",
            WacomDriverCheck.Find(p => p == "/Applications/Wacom Tablet.localized", None, new string[0]));

    [Fact]
    public void FindsLaunchAgentsByPrefixOnly()
    {
        var found = WacomDriverCheck.Find(_ => false,
            d => d == "/Library/LaunchAgents" ? new[] { "/Library/LaunchAgents/com.wacom.wacomtablet.plist", "/Library/LaunchAgents/com.other.plist" } : new string[0],
            new string[0]);
        Assert.Equal(new[] { "/Library/LaunchAgents/com.wacom.wacomtablet.plist" }, found);
    }

    [Fact]
    public void FindsRunningDriver()
        => Assert.Contains("process: WacomTabletDriver", WacomDriverCheck.Find(_ => false, None, new[] { "wacomtabletdriver" }));

    [Fact]
    public void Warning_IsPlain()
        => Assert.StartsWith("The Wacom driver is still installed.", WacomDriverCheck.Warning);

    [Fact]
    public void HelpLeftover_IsNotTheDriver()
    {
        Assert.Empty(WacomDriverCheck.Find(p => p == "/Library/Application Support/Tablet", None, new string[0]));
        Assert.Contains("/Library/Application Support/Tablet", WacomDriverCheck.FindLeftovers(p => p == "/Library/Application Support/Tablet", None));
    }

    [Fact]
    public void NeverMatchesOurOwnProcesses()
        => Assert.Empty(WacomDriverCheck.Find(_ => false, None, new[] { "OpenTabletDriver", "OpenTabletDriver.Daemon", "OpenTabletArtist", "TabletDriver" }));

    [Fact]
    public void EmptyApplicationsFolder_WithoutUtility_IsRemovableNotLive()
    {
        bool exists(string p) => p == WacomDriverCheck.ApplicationsLocalized;
        Assert.Contains(WacomDriverCheck.ApplicationsLocalized, WacomDriverCheck.ClassifyRemovable(exists, None));
        Assert.Empty(WacomDriverCheck.ClassifyLive(exists, None, Array.Empty<string>()));
        Assert.False(WacomDriverCheck.HasUtility(exists));
    }

    [Fact]
    public void ApplicationsFolder_WithUtility_IsLiveNotRemovable()
    {
        bool exists(string p) =>
            p == WacomDriverCheck.ApplicationsLocalized
            || p == WacomDriverCheck.UtilityApp
            || p == WacomDriverCheck.ApplicationsLocalized + "/Wacom Tablet Utility.app";
        Assert.Empty(WacomDriverCheck.ClassifyRemovable(exists, None));
        Assert.Contains(WacomDriverCheck.ApplicationsLocalized, WacomDriverCheck.ClassifyLive(exists, None, Array.Empty<string>()));
        Assert.True(WacomDriverCheck.HasUtility(exists));
    }

    [Fact]
    public void RunningProcess_IsLive_AndBlocksNothingInRemovable()
    {
        Assert.Contains("process: WacomTabletDriver",
            WacomDriverCheck.ClassifyLive(_ => false, None, new[] { "WacomTabletDriver" }));
        Assert.Empty(WacomDriverCheck.ClassifyRemovable(_ => false, None));
    }

    [Fact]
    public void TryRemove_DeletesEmptyApplicationsFolder()
    {
        var gone = new HashSet<string>();
        bool exists(string p) => p == WacomDriverCheck.ApplicationsLocalized && !gone.Contains(p);
        var result = WacomDriverCheck.TryRemoveRemovable(exists, None, p => gone.Add(p));
        Assert.Equal(new[] { WacomDriverCheck.ApplicationsLocalized }, result.Removed);
        Assert.Empty(result.Failed);
        Assert.Contains(WacomDriverCheck.ApplicationsLocalized, gone);
    }

    [Fact]
    public void TryRemove_DoesNotDeleteWhenUtilityPresent()
    {
        var deleted = new List<string>();
        bool exists(string p) =>
            p == WacomDriverCheck.ApplicationsLocalized
            || p == WacomDriverCheck.UtilityApp
            || p == WacomDriverCheck.ApplicationsLocalized + "/Wacom Tablet Utility.app";
        var result = WacomDriverCheck.TryRemoveRemovable(exists, None, deleted.Add);
        Assert.Empty(result.Removed);
        Assert.Empty(deleted);
    }

    [Fact]
    public void TryRemove_RecordsPermissionFailures()
    {
        bool exists(string p) => p == "/Library/Application Support/Tablet";
        var result = WacomDriverCheck.TryRemoveRemovable(exists, None, _ => throw new UnauthorizedAccessException());
        Assert.Empty(result.Removed);
        Assert.Equal(new[] { "/Library/Application Support/Tablet" }, result.Failed);
    }

    [Fact]
    public void LoginItem_ReRegistersOnlyWhenOnAndSignatureChanged()
    {
        Assert.True(OpenTabletArtist.Services.MacLoginItemService.NeedsReRegister(true, null, "A"));
        Assert.True(OpenTabletArtist.Services.MacLoginItemService.NeedsReRegister(true, "A", "B"));
        Assert.False(OpenTabletArtist.Services.MacLoginItemService.NeedsReRegister(true, "A", "A"));
        Assert.False(OpenTabletArtist.Services.MacLoginItemService.NeedsReRegister(false, "A", "B"));
    }
}

public class TrayDiagnosticsTests
{
    [Xunit.Fact]
    public void Diagnostics_name_the_running_executable()
        => Xunit.Assert.Contains("exe ", OpenTabletArtist.AppTray.TrayDiagnostics());
}

public class UpdaterZipNameTests
{
    /// <summary>The owner, 4 Nov 2026: Apple silicon only, no Intel builds.</summary>
    [Xunit.Fact]
    public void One_zip_for_Apple_silicon_and_no_Intel_zip()
    {
        Xunit.Assert.Equal("Tablet-App-for-Mac.zip", OpenTabletArtist.Services.Updater.ZipName);
        Xunit.Assert.DoesNotContain("Intel", OpenTabletArtist.Services.Updater.ZipName);
    }

    /// <summary>The owner, 4 Nov 2026: simple names, "Version 23 BETA" then "Version 23".</summary>
    [Xunit.Fact]
    public void A_BETA_without_a_number_reads_N_BETA()
    {
        Xunit.Assert.Equal("23 BETA", OpenTabletArtist.Domain.AppVersionInfo.Number("23-beta+abc1234"));
        Xunit.Assert.Equal("22 BETA 2", OpenTabletArtist.Domain.AppVersionInfo.Number("22-beta2"));
        Xunit.Assert.Equal(1, OpenTabletArtist.Services.Updater.ParseBeta("v23-beta"));
        Xunit.Assert.False(OpenTabletArtist.Services.Updater.BetaHasNumber("v23-beta"));
        Xunit.Assert.True(OpenTabletArtist.Services.Updater.BetaHasNumber("v22-beta2"));
        var json = """
        [ {"tag_name":"v23-beta","prerelease":true,"assets":[
            {"name":"Tablet-App-for-Mac.zip","browser_download_url":"https://x/z"},
            {"name":"SHA256SUMS","browser_download_url":"https://x/s"}]},
          {"tag_name":"v22-beta2","prerelease":true,"assets":[
            {"name":"Tablet-App-for-Mac.zip","browser_download_url":"https://x/z2"},
            {"name":"SHA256SUMS","browser_download_url":"https://x/s2"}]},
          {"tag_name":"v21","prerelease":false,"assets":[
            {"name":"Tablet-App-for-Mac.zip","browser_download_url":"https://x/z3"},
            {"name":"SHA256SUMS","browser_download_url":"https://x/s3"}]} ]
        """;
        var all = OpenTabletArtist.Services.Updater.ParseAll(json);
        Xunit.Assert.Equal(new[] { "23 BETA", "22 BETA 2", "21" }, all.Select(r => r.Label).ToArray());
        var c = OpenTabletArtist.Services.Updater.Choose(all, 21, 0);
        Xunit.Assert.Equal("23 BETA", c.Beta?.Label);
    }
}

public class SetupStepNumberTests
{
    [Xunit.Fact]
    public void Numbers_stay_fixed_when_a_step_finishes_early()
    {
        var steps = new System.Collections.Generic.List<OpenTabletArtist.Views.SetupWindow.Step>
            { OpenTabletArtist.Views.SetupWindow.Step.ReadPen, OpenTabletArtist.Views.SetupWindow.Step.MovePointer, OpenTabletArtist.Views.SetupWindow.Step.Restart };
        Xunit.Assert.Equal(2, OpenTabletArtist.Views.SetupWindow.StepNumber(steps, OpenTabletArtist.Views.SetupWindow.Step.MovePointer));
        Xunit.Assert.Equal(3, OpenTabletArtist.Views.SetupWindow.StepNumber(steps, OpenTabletArtist.Views.SetupWindow.Step.Restart));
        Xunit.Assert.Equal(3, steps.Count);
    }

    [Xunit.Fact]
    public void Restart_waits_for_the_daemon_and_never_kills()
    {
        var s = OpenTabletArtist.Views.SetupWindow.RestartScript(42);
        Xunit.Assert.Contains("pgrep -x OpenTabletDriver.Daemon", s);
        Xunit.Assert.Contains("kill -0 42", s);
        Xunit.Assert.DoesNotContain("kill -9", s);
        Xunit.Assert.DoesNotContain("pkill", s);
    }
}
