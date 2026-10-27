using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Version 15 (Zak, 27 Oct 2026): "All versions stable or beta should allow the users to downgrade to an
/// earlier version." The update check also lists the two stable versions before the running one (BETAs are never
/// offered as a way back), and the swap script stops the old app's driver so the earlier version starts clean.</summary>
public class BuildV15EarlierVersionTests
{
    static Updater.Release S(int v) => new(v, $"z{v}", $"s{v}");
    static Updater.Release B(int v, int b) => new(v, $"z{v}b{b}", $"s{v}b{b}", b);

    static string[] Earlier(Updater.UpdateChoices c) => c.EarlierVersions.Select(r => r.Label).ToArray();

    [Fact]
    public void OnStable_OffersTheTwoStableVersionsBefore()
    {
        var c = Updater.Choose(new[] { S(14), B(14, 2), B(14, 1), S(13), S(12), S(11) }, 14, 0);
        Assert.Equal(new[] { "13", "12" }, Earlier(c));
        Assert.True(c.HasEarlier);
        Assert.False(c.Any);   // an earlier version is never announced as an update
    }

    [Fact]
    public void OnBeta_OffersTheTwoStableVersionsBefore_NeverABeta()
    {
        var c = Updater.Choose(new[] { B(15, 2), B(15, 1), S(14), B(14, 1), S(13), S(12) }, 15, 2);
        Assert.Equal(new[] { "14", "13" }, Earlier(c));
        Assert.Equal("14", c.GoBack?.Label);
    }

    [Fact]
    public void OnStableWithNewerVersions_StillOffersEarlier()
    {
        var c = Updater.Choose(new[] { B(16, 1), S(15), S(14), S(13) }, 14, 0);
        Assert.Equal("15", c.StableUpgrade?.Label);
        Assert.Equal("16 BETA 1", c.Beta?.Label);
        Assert.Equal(new[] { "13" }, Earlier(c));
    }

    [Fact]
    public void NothingEarlier_WhenOfflineOrFirstVersion()
    {
        Assert.False(Updater.UpdateChoices.None.HasEarlier);
        Assert.Empty(Updater.UpdateChoices.None.EarlierVersions);
        Assert.False(Updater.Choose(new[] { S(14) }, 14, 0).HasEarlier);
    }

    [Fact]
    public void DuplicateVersionsAreListedOnce()
    {
        var c = Updater.Choose(new[] { S(14), S(13), new Updater.Release(13, "other", "other"), S(12) }, 14, 0);
        Assert.Equal(new[] { "13", "12" }, Earlier(c));
    }

    [Theory]
    [InlineData("/Applications/Tablet App for Mac.app/Contents/MacOS/Daemon/arm64/{exe}", "/Applications/Tablet App for Mac.app", true)]
    [InlineData("/Applications/Tablet App for Mac.app/Contents/MacOS/Daemon/{exe}", "/Applications/Tablet App for Mac.app/", true)]
    [InlineData("/Applications/Tablet App for Mac 2.app/Contents/MacOS/Daemon/{exe}", "/Applications/Tablet App for Mac.app", false)]
    [InlineData("/usr/local/bin/{exe}", "/Applications/Tablet App for Mac.app", false)]
    [InlineData("/Applications/Tablet App for Mac.app/Contents/MacOS/Daemon/other", "/Applications/Tablet App for Mac.app", false)]
    [InlineData(null, "/Applications/Tablet App for Mac.app", false)]
    public void RecognisesTheDriverInsideAnApp(string? process, string app, bool expected)
        => Assert.Equal(expected, StaleDriverGuard.IsDriverOf(process?.Replace("{exe}", DaemonExePaths.DaemonExeName), app));

    [Fact]
    public void SwapScript_StopsOnlyTheOldDriverThenSwapsTheApp()
    {
        if (OperatingSystem.IsWindows()) return;
        var root = Directory.CreateTempSubdirectory("swap-test").FullName;
        try
        {
            var current = Path.Combine(root, "Installed.app");
            var fresh = Path.Combine(root, "new", "Fresh.app");
            var staging = Path.Combine(root, "staging");
            Directory.CreateDirectory(Path.Combine(current, "old-marker"));
            Directory.CreateDirectory(Path.Combine(fresh, "new-marker"));
            Directory.CreateDirectory(staging);
            // A fake "driver" (named so ps shows OpenTabletDriver) and a bystander with another name.
            var bin = Path.Combine(root, "bin");
            Directory.CreateDirectory(bin);
            var driverExe = Path.Combine(bin, "OpenTabletDriver");
            var otherExe = Path.Combine(bin, "bystander");
            File.Copy("/bin/sleep", driverExe); File.Copy("/bin/sleep", otherExe);
            using var driver = Process.Start(driverExe, "30");
            using var other = Process.Start(otherExe, "30");
            // "open" is stubbed through PATH so the test needn't be on a Mac.
            File.WriteAllText(Path.Combine(bin, "open"), "#!/bin/sh\ntouch \"$1/opened\"\n");
            Process.Start("chmod", new[] { "+x", Path.Combine(bin, "open") })!.WaitForExit();

            var psi = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
            psi.Environment["PATH"] = bin + ":" + Environment.GetEnvironmentVariable("PATH");
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(Updater.SwapScript(999_999));   // a process id that isn't running: no wait
            psi.ArgumentList.Add(current); psi.ArgumentList.Add(fresh); psi.ArgumentList.Add(staging);
            psi.ArgumentList.Add($"{driver.Id} {other.Id}");
            using (var sh = Process.Start(psi)!) Assert.True(sh.WaitForExit(15000));

            Assert.True(driver.WaitForExit(5000));          // the old driver was stopped
            Assert.False(other.HasExited);                 // the other process was left alone
            Assert.True(Directory.Exists(Path.Combine(current, "new-marker")));
            Assert.True(File.Exists(Path.Combine(current, "opened")));
            Assert.True(Directory.Exists(Path.Combine(staging, "old.app", "old-marker")));
            other.Kill();
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
}
