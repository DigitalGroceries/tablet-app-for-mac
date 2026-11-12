using System;
using System.IO;
using System.Linq;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

public class PressurePluginInstallerTests
{
    [Fact]
    public void SourceCandidates_BundledLocationFirst()
    {
        var baseDir = Path.Combine("C:", "app");
        var first = PressurePluginPaths.SourceCandidates(baseDir).First();
        Assert.Equal(
            Path.GetFullPath(Path.Combine(baseDir, "BundledPlugins", "OpenTabletArtistDynamics", "OpenTabletArtist.Dynamics.dll")),
            first);
    }

    [Fact]
    public void SourceCandidates_IncludesDevBuildOutput()
    {
        var candidates = PressurePluginPaths.SourceCandidates(Path.Combine("C:", "repo", "OpenTabletArtist", "bin", "Debug", "net10.0")).ToList();
        // bundled, macOS Contents/Resources, then dev output for net10.0 (current) and net8.0 (older plugin builds)
        Assert.Equal(6, candidates.Count);
        Assert.Contains(candidates, c => c.Contains(Path.Combine("plugins", "OpenTabletArtist.Dynamics")) && c.Contains(Path.Combine("bin", "Debug", "net10.0")));
        Assert.Contains(candidates, c => c.Contains(Path.Combine("plugins", "OpenTabletArtist.Dynamics")) && c.Contains(Path.Combine("bin", "Debug", "net8.0")));
    }

    [Fact]
    public void SourceCandidates_LookInTheMacBundlesResourcesSecond()
    {
        var baseDir = Path.Combine("/", "Applications", "OpenTabletArtist.app", "Contents", "MacOS");
        var second = PressurePluginPaths.SourceCandidates(baseDir).ElementAt(1);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(baseDir, "..", "Resources", "BundledPlugins", "OpenTabletArtistDynamics", "OpenTabletArtist.Dynamics.dll")),
            second);
    }

    [Fact]
    public void CopyIfNeeded_FreshThenUpToDate()
    {
        var dir = TempDir();
        try
        {
            var src = Path.Combine(dir, "src.dll");
            File.WriteAllText(src, "v1");
            var pluginDir = Path.Combine(dir, "plugins");

            Assert.Equal(PluginInstallOutcome.Installed, PressurePluginInstaller.CopyIfNeeded(src, pluginDir));
            var dest = Path.Combine(pluginDir, "OpenTabletArtistDynamics", "OpenTabletArtist.Dynamics.dll");
            Assert.True(File.Exists(dest));

            Assert.Equal(PluginInstallOutcome.None, PressurePluginInstaller.CopyIfNeeded(src, pluginDir)); // up to date
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void CopyIfNeeded_OverwriteReportsUpdated()
    {
        var dir = TempDir();
        try
        {
            var src = Path.Combine(dir, "src.dll");
            File.WriteAllText(src, "v1");
            var pluginDir = Path.Combine(dir, "plugins");
            Assert.Equal(PluginInstallOutcome.Installed, PressurePluginInstaller.CopyIfNeeded(src, pluginDir));

            File.WriteAllText(src, "v2-longer-content"); // different size + newer
            Assert.Equal(PluginInstallOutcome.Updated, PressurePluginInstaller.CopyIfNeeded(src, pluginDir));
            var dest = Path.Combine(pluginDir, "OpenTabletArtistDynamics", "OpenTabletArtist.Dynamics.dll");
            Assert.Equal("v2-longer-content", File.ReadAllText(dest));
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>Build 7: test builds give every file the same fixed date, so a new plugin of the same size or with
    /// an older date than the installed one must still replace it when the bytes differ.</summary>
    [Fact]
    public void CopyIfNeeded_SameSizeAndOlderDate_ButDifferentBytes_IsUpdated()
    {
        var dir = TempDir();
        try
        {
            var src = Path.Combine(dir, "src.dll");
            File.WriteAllText(src, "AAAA");
            var pluginDir = Path.Combine(dir, "plugins");
            Assert.Equal(PluginInstallOutcome.Installed, PressurePluginInstaller.CopyIfNeeded(src, pluginDir));
            var dest = Path.Combine(pluginDir, "OpenTabletArtistDynamics", "OpenTabletArtist.Dynamics.dll");
            File.SetLastWriteTimeUtc(dest, new DateTime(2026, 11, 9, 18, 0, 0, DateTimeKind.Utc));

            File.WriteAllText(src, "BBBB");                                                       // same size
            File.SetLastWriteTimeUtc(src, new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc)); // older date
            Assert.Equal(PluginInstallOutcome.Updated, PressurePluginInstaller.CopyIfNeeded(src, pluginDir));
            Assert.Equal("BBBB", File.ReadAllText(dest));

            Assert.Equal(PluginInstallOutcome.None, PressurePluginInstaller.CopyIfNeeded(src, pluginDir)); // now identical
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void CopyIfNeeded_MissingSource_IsSourceMissing()
        => Assert.Equal(PluginInstallOutcome.SourceMissing,
            PressurePluginInstaller.CopyIfNeeded(Path.Combine("C:", "nope", "x.dll"), Path.Combine("C:", "plugins")));

    // Build 8 (Zak, 12 Nov 2026): his installed plugin was an old, READ-ONLY copy from 23 Oct. The copy failed,
    // the failure was reported as "up to date", and the driver kept the old plugin (no radial menu). The test
    // makes exactly that file. (Run as root, a read-only file is writable anyway; checked as a normal user on
    // 12 Nov: the old File.Copy threw UnauthorizedAccessException, PluginFileCopy.Replace replaced the file.)
    [Fact]
    public void CopyIfNeeded_ReplacesAReadOnlyOldCopy_AndLeavesItWritable()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = TempDir();
        try
        {
            var src = Path.Combine(dir, "new.dll");
            File.WriteAllText(src, "new plugin with the radial menu");
            var pluginDir = Path.Combine(dir, "Plugins");
            var dest = Path.Combine(pluginDir, PressurePluginPaths.PluginFolderName, PressurePluginPaths.DllName);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.WriteAllText(dest, "old plugin from 23 Oct");
            File.SetUnixFileMode(dest, UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

            Assert.Equal(PluginInstallOutcome.Updated, PressurePluginInstaller.CopyIfNeeded(src, pluginDir, out var error));
            Assert.Null(error);
            Assert.Equal("new plugin with the radial menu", File.ReadAllText(dest));
            Assert.True(File.GetUnixFileMode(dest).HasFlag(UnixFileMode.UserWrite));
        }
        finally { Directory.Delete(dir, true); }
    }

    // A copy that really can't be made is reported as Failed with the reason, never as "up to date".
    [Fact]
    public void CopyIfNeeded_WhenTheCopyFails_SaysFailed_NotUpToDate()
    {
        var dir = TempDir();
        try
        {
            var src = Path.Combine(dir, "new.dll");
            File.WriteAllText(src, "new");
            // The plugin "folder" is a file, so nothing can be created inside it.
            var pluginDir = Path.Combine(dir, "Plugins");
            File.WriteAllText(pluginDir, "not a folder");

            Assert.Equal(PluginInstallOutcome.Failed, PressurePluginInstaller.CopyIfNeeded(src, pluginDir, out var error));
            Assert.False(string.IsNullOrEmpty(error));
        }
        finally { Directory.Delete(dir, true); }
    }

    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), $"otdplug_{Guid.NewGuid():N}");
        Directory.CreateDirectory(d);
        return d;
    }
}
