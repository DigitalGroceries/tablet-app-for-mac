using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// TASKS #74 (Version 28): a second copy of the app couldn't reach the driver left by the first copy. That driver
/// was closing (no threads, no path) yet still held the driver lock, so (1) the new driver quit at once and the app
/// called it "an incomplete install", and (2) the Driver page said "not running" because .NET's process lookup
/// can't name a closing process on macOS. These tests pin the finder, the messages and the replace-the-stuck-driver
/// path against exactly that situation: a closing driver that holds the lock.
/// </summary>
public class StuckDriverTests
{
    private const string OursPath = "/Applications/Tablet App for Mac.app/Contents/MacOS/Daemon/OpenTabletDriver.Daemon";
    private const string T7CopyPath = "/Volumes/4TB T7/TEST/Tablet App for Mac.app/Contents/MacOS/Daemon/OpenTabletDriver.Daemon";
    private const string UsersOwnOtd = "/Applications/OpenTabletDriver.app/Contents/MacOS/OpenTabletDriver.Daemon";

    // ------------------------------------------------------------------ finder: proc_bsdinfo parsing

    /// <summary>Builds a raw <c>struct proc_bsdinfo</c> the way XNU fills it (bsd/sys/proc_info.h).</summary>
    private static byte[] BsdInfo(uint status, string comm, string name, ulong startSec)
    {
        var b = new byte[DriverProcessFinder.BsdInfoSize];
        BitConverter.GetBytes(status).CopyTo(b, 4);
        Encoding.UTF8.GetBytes(comm).CopyTo(b, 48);
        Encoding.UTF8.GetBytes(name).CopyTo(b, 64);
        BitConverter.GetBytes(startSec).CopyTo(b, 120);
        return b;
    }

    [Fact]
    public void Finder_ReadsTheFullNameOfAClosingDriver()
    {
        // XNU keeps 15 characters of the name in pbi_comm and the whole file name (up to 31) in pbi_name.
        var raw = BsdInfo(DriverProcessFinder.StatusZombie, "OpenTabletDrive", "OpenTabletDriver.Daemon", 1_763_050_227);

        var info = DriverProcessFinder.ParseBsdInfo(raw);

        Assert.NotNull(info);
        Assert.Equal("OpenTabletDriver.Daemon", info!.Value.Name);
        Assert.Equal("OpenTabletDrive", info.Value.Comm);
        Assert.Equal(DriverProcessFinder.StatusZombie, info.Value.Status);
        Assert.Equal(1_763_050_227, info.Value.StartSeconds);
        Assert.True(DriverProcessFinder.IsDriverName(info.Value.Name));
    }

    [Fact]
    public void Finder_RejectsAShortBuffer() =>
        Assert.Null(DriverProcessFinder.ParseBsdInfo(new byte[DriverProcessFinder.BsdInfoSize - 1]));

    [Theory]
    [InlineData("OpenTabletDriver", false)]        // OpenTabletDriver's own app: never ours to stop
    [InlineData("OpenTabletDriver.UX", false)]
    [InlineData("", false)]                        // no full name: the 15-char comm alone is never trusted
    [InlineData("OpenTabletDriver.Daemon", true)]
    [InlineData("opentabletdriver.daemon", true)]
    public void Finder_MatchesOnlyTheDaemonName(string name, bool expected) =>
        Assert.Equal(expected, DriverProcessFinder.IsDriverName(name));

    [Theory]
    [InlineData(OursPath, true)]
    [InlineData(T7CopyPath, true)]                 // another copy of the app (the 13 Nov case)
    [InlineData("/Applications/Free Tablet App for Mac.app/Contents/MacOS/Daemon/arm64/OpenTabletDriver.Daemon", true)]
    [InlineData(UsersOwnOtd, false)]               // the user's own OpenTabletDriver install
    [InlineData(null, false)]
    [InlineData("", false)]
    public void Finder_KnowsTheDriverInsideACopyOfTheApp(string? path, bool expected) =>
        Assert.Equal(expected, DriverProcessFinder.IsTabletAppDriverPath(path));

    // ------------------------------------------------------------------ Driver page card

    [Fact]
    public void DriverCard_SaysClosingInsteadOfNotRunning()
    {
        var info = DaemonProcess.From(new[] { new DriverProcessInfo(10092, null, null, DriverProcessState.Closing) });
        Assert.False(info.Running);
        Assert.True(info.Closing);
    }

    [Fact]
    public void DriverCard_PrefersTheDriverThatCanAnswer()
    {
        var start = new DateTime(2026, 11, 13, 17, 10, 27);
        var info = DaemonProcess.From(new[]
        {
            new DriverProcessInfo(10092, null, null, DriverProcessState.Closing),
            new DriverProcessInfo(12001, OursPath, start, DriverProcessState.Running),
        });
        Assert.True(info.Running);
        Assert.False(info.Closing);
        Assert.Equal(OursPath, info.Path);
        Assert.Equal(start, info.StartTime);
    }

    [Fact]
    public void DriverCard_NothingAtAll() => Assert.Equal(new DaemonProcess.Info(false, null, null), DaemonProcess.From(Array.Empty<DriverProcessInfo>()));

    // ------------------------------------------------------------------ lifecycle with a fake process table

    /// <summary>A fake process table and signal sender. <see cref="OnSignal"/> decides what a signal does.</summary>
    private sealed class FakeSystem
    {
        public List<DriverProcessInfo> Table { get; } = new();
        public List<(int Pid, int Signal)> Signals { get; } = new();
        public Func<int, int, bool> OnSignal { get; set; } = (_, _) => true; // true = the process goes away

        public IReadOnlyList<DriverProcessInfo> Find() => Table.ToList();

        public int Signal(int pid, int sig)
        {
            Signals.Add((pid, sig));
            if (OnSignal(pid, sig)) Table.RemoveAll(p => p.Pid == pid);
            return 0;
        }

        public DaemonLifecycleService Service() => new(Find, Signal, _ => { });
    }

    /// <summary>A stand-in driver: a shell script that exits with <paramref name="exitCode"/> while
    /// <paramref name="lockFile"/> exists (the lock is held), and otherwise keeps running for 4 s (a healthy start).</summary>
    private static string FakeDriver(string dir, int exitCode, string lockFile)
    {
        var path = Path.Combine(dir, "OpenTabletDriver.Daemon");
        File.WriteAllText(path, $"#!/bin/sh\nif [ -e '{lockFile}' ]; then echo 'OpenTabletDriver Daemon is already running.'; exit {exitCode}; fi\nsleep 4\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "ota-stuck-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    [Fact]
    public void IsRunning_IgnoresAClosingDriver()
    {
        var sys = new FakeSystem();
        sys.Table.Add(new DriverProcessInfo(10092, null, null, DriverProcessState.Closing));
        Assert.False(sys.Service().IsRunning());

        sys.Table.Add(new DriverProcessInfo(12001, OursPath, null, DriverProcessState.Running));
        Assert.True(sys.Service().IsRunning());
    }

    [Fact]
    public void Launch_SaysAnotherDriverIsRunning_NotIncompleteInstall()
    {
        if (OperatingSystem.IsWindows()) return; // shell-script stand-in
        var dir = TempDir();
        var lockFile = Path.Combine(dir, "lock"); File.WriteAllText(lockFile, "");
        var sys = new FakeSystem();
        sys.Table.Add(new DriverProcessInfo(10092, null, null, DriverProcessState.Closing));

        var problem = sys.Service().Launch(FakeDriver(dir, DriverProcessFinder.AlreadyRunningExitCode, lockFile));

        Assert.NotNull(problem);
        Assert.Contains("Another tablet driver is already running (process 10092)", problem);
        Assert.DoesNotContain("incomplete install", problem);
    }

    [Fact]
    public void Launch_OldDriverExitZero_WithADriverStillThere_CountsAsAlreadyRunning()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = TempDir();
        var lockFile = Path.Combine(dir, "lock"); File.WriteAllText(lockFile, "");
        var sys = new FakeSystem();
        sys.Table.Add(new DriverProcessInfo(10092, null, null, DriverProcessState.Closing));

        var problem = sys.Service().Launch(FakeDriver(dir, 0, lockFile));

        Assert.Contains("isn't answering", problem);
    }

    [Fact]
    public void Launch_ExitZero_NothingElseThere_KeepsTheInstallMessage()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = TempDir();
        var lockFile = Path.Combine(dir, "lock"); File.WriteAllText(lockFile, "");
        var sys = new FakeSystem();

        var problem = sys.Service().Launch(FakeDriver(dir, 0, lockFile));

        Assert.Contains("incomplete install", problem);
    }

    [Fact]
    public void LaunchOrReplaceStuck_StopsTheClosingDriver_ThenStartsOurs()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = TempDir();
        var lockFile = Path.Combine(dir, "lock"); File.WriteAllText(lockFile, "");
        var sys = new FakeSystem();
        sys.Table.Add(new DriverProcessInfo(10092, null, null, DriverProcessState.Closing));
        // The stuck driver goes on SIGTERM, and with it the lock it held.
        sys.OnSignal = (pid, sig) => { File.Delete(lockFile); return true; };

        var problem = sys.Service().LaunchOrReplaceStuck(FakeDriver(dir, DriverProcessFinder.AlreadyRunningExitCode, lockFile));

        Assert.Null(problem);                                   // our driver is up
        Assert.Equal(new[] { (10092, 15) }, sys.Signals);      // polite stop first, and it was enough
    }

    [Fact]
    public void LaunchOrReplaceStuck_KillsWhenThePoliteStopIsIgnored()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = TempDir();
        var lockFile = Path.Combine(dir, "lock"); File.WriteAllText(lockFile, "");
        var sys = new FakeSystem();
        sys.Table.Add(new DriverProcessInfo(10092, null, null, DriverProcessState.Closing));
        sys.OnSignal = (pid, sig) => { if (sig != 9) return false; File.Delete(lockFile); return true; };

        var problem = sys.Service().LaunchOrReplaceStuck(FakeDriver(dir, DriverProcessFinder.AlreadyRunningExitCode, lockFile));

        Assert.Null(problem);
        Assert.Equal(new[] { (10092, 15), (10092, 9) }, sys.Signals);
    }

    [Fact]
    public void LaunchOrReplaceStuck_ADriverThatWontGo_IsReportedNotHidden()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = TempDir();
        var lockFile = Path.Combine(dir, "lock"); File.WriteAllText(lockFile, "");
        var sys = new FakeSystem();
        sys.Table.Add(new DriverProcessInfo(10092, null, null, DriverProcessState.Closing));
        sys.OnSignal = (_, _) => false; // stuck in the kernel: nothing removes it

        var problem = sys.Service().LaunchOrReplaceStuck(FakeDriver(dir, DriverProcessFinder.AlreadyRunningExitCode, lockFile));

        Assert.NotNull(problem);
        Assert.Contains("couldn't be stopped", problem);
        Assert.Contains("Restart your Mac", problem);
    }

    [Fact]
    public void LaunchOrReplaceStuck_ARunningDriverIsConnectedTo_NotStopped()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = TempDir();
        var lockFile = Path.Combine(dir, "lock"); File.WriteAllText(lockFile, "");
        var sys = new FakeSystem();
        sys.Table.Add(new DriverProcessInfo(12001, T7CopyPath, null, DriverProcessState.Running));

        var problem = sys.Service().LaunchOrReplaceStuck(FakeDriver(dir, DriverProcessFinder.AlreadyRunningExitCode, lockFile));

        Assert.Null(problem);          // the caller connects to the running one
        Assert.Empty(sys.Signals);     // nothing was stopped
    }

    [Fact]
    public void StopUnansweringDrivers_LeavesTheUsersOwnOpenTabletDriverAlone()
    {
        var sys = new FakeSystem();
        sys.Table.Add(new DriverProcessInfo(5000, UsersOwnOtd, null, DriverProcessState.Running));

        Assert.False(sys.Service().StopUnansweringDrivers());
        Assert.Empty(sys.Signals);
    }

    [Fact]
    public void StopUnansweringDrivers_StopsTheDriverOfAnotherCopyOfTheApp()
    {
        var sys = new FakeSystem();
        sys.Table.Add(new DriverProcessInfo(10092, T7CopyPath, null, DriverProcessState.Running));

        Assert.True(sys.Service().StopUnansweringDrivers());
        Assert.Equal(new[] { (10092, 15) }, sys.Signals);
    }

    [Fact]
    public void StopPids_NeverSignalsAPidThatIsNoLongerADriver()
    {
        // A process id can be reused: only ids the finder still lists as drivers are signalled.
        var sys = new FakeSystem();
        sys.Table.Add(new DriverProcessInfo(10092, null, null, DriverProcessState.Closing));

        var left = sys.Service().StopPids(new[] { 10092, 4242 });

        Assert.Empty(left);
        Assert.DoesNotContain(sys.Signals, s => s.Pid == 4242);
    }
}
