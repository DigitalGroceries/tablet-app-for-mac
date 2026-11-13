using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using OpenTabletArtist.Services;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Platform.Pointer;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Issue #73 part 3 (Maxime: the real mouse's left button stops clicking after the pen's Double Click was
/// set; right-click still works). macOS merges every source's button state, so a virtual left button that the
/// driver posted down and never let go makes the real left button useless while the right one still works. These
/// cover every way the driver could drop its virtual mouse, or stop, while a button is held. The macOS mouse itself
/// can't be built off a Mac (CoreGraphics), so its wiring is checked in the source.</summary>
public class HeldButtonReleaseTests
{
    private sealed class FakePointer : IAbsolutePointer, IRelativePointer, IHeldButtonReleaser
    {
        public bool HasHeldButtons { get; set; }
        public int Releases { get; private set; }
        public bool Throws { get; set; }
        public void SetPosition(Vector2 pos) { }
        public void ReleaseHeldButtons()
        {
            Releases++;
            if (Throws) throw new InvalidOperationException("boom");
            HasHeldButtons = false;
        }
    }

    private sealed class PlainPointer : IAbsolutePointer
    {
        public void SetPosition(Vector2 pos) { }
    }

    private sealed class AbsMode : AbsoluteOutputMode
    {
        public override IAbsolutePointer? Pointer { get; set; }
    }

    private sealed class RelMode : RelativeOutputMode
    {
        public override IRelativePointer? Pointer { get; set; }
    }

    [Fact]
    public void AHeldButton_IsReleased_AndReportedAsReleased()
    {
        var p = new FakePointer { HasHeldButtons = true };
        Assert.True(HeldButtonRelease.TryRelease(p, "test", "unit test"));
        Assert.Equal(1, p.Releases);
        Assert.False(p.HasHeldButtons);
    }

    [Fact]
    public void NothingHeld_PostsNothing()
    {
        var p = new FakePointer();
        Assert.False(HeldButtonRelease.TryRelease(p, "test", "unit test"));
        Assert.Equal(0, p.Releases);
    }

    [Fact]
    public void ReleasingTwice_DoesNotPostASecondUp()
    {
        var p = new FakePointer { HasHeldButtons = true };
        HeldButtonRelease.TryRelease(p, "test", "unit test");
        Assert.False(HeldButtonRelease.TryRelease(p, "test", "unit test"));
        Assert.Equal(1, p.Releases);
    }

    [Fact]
    public void APointerThatCannotHoldButtons_IsLeftAlone()
    {
        Assert.False(HeldButtonRelease.TryRelease(new PlainPointer(), "test", "unit test"));
        Assert.False(HeldButtonRelease.TryRelease(null, "test", "unit test"));
    }

    [Fact]
    public void AFailedRelease_NeverThrows_AndIsNotReportedAsSuccess()
    {
        var p = new FakePointer { HasHeldButtons = true, Throws = true };
        var ok = Record.Exception(() => Assert.False(HeldButtonRelease.TryRelease(p, "test", "unit test")));
        Assert.Null(ok);
        Assert.Equal(1, p.Releases);
        Assert.True(p.HasHeldButtons);   // still held: the failure is not hidden as "released"
    }

    [Fact]
    public void DroppingAnAbsoluteOutputMode_ReleasesItsHeldButtons()   // tablet unplugged, settings change, driver closing
    {
        var p = new FakePointer { HasHeldButtons = true };
        var mode = new AbsMode { Pointer = p };
        mode.Dispose();
        Assert.Equal(1, p.Releases);
        mode.Dispose();                                                 // disposing twice stays harmless
        Assert.Equal(1, p.Releases);
    }

    [Fact]
    public void DroppingARelativeOutputMode_ReleasesItsHeldButtons()
    {
        var p = new FakePointer { HasHeldButtons = true };
        var mode = new RelMode { Pointer = p };
        mode.Dispose();
        Assert.Equal(1, p.Releases);
    }

    [Fact]
    public void DroppingAModeWhoseReleaseFails_StillDisposes()
    {
        var p = new FakePointer { HasHeldButtons = true, Throws = true };
        var mode = new AbsMode { Pointer = p };
        Assert.Null(Record.Exception(() => mode.Dispose()));
    }

    // ---- the macOS mouse and the daemon: wiring checked in the source ----

    private static string RepoRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private static string Source(string relative) => File.ReadAllText(Path.Combine(RepoRoot(), relative));

    private const string Mouse = "external/OpenTabletDriver/OpenTabletDriver.Desktop/Interop/Input/MacOSVirtualMouse.cs";
    private const string Daemon = "external/OpenTabletDriver/OpenTabletDriver.Daemon/DriverDaemon.cs";
    private const string Program = "external/OpenTabletDriver/OpenTabletDriver.Daemon/Program.cs";

    private static string Body(string source, string signature)
    {
        var from = source[source.IndexOf(signature, StringComparison.Ordinal)..];
        return from[..from.IndexOf("\n        }", StringComparison.Ordinal)];
    }

    [Fact]
    public void TheMacMouse_IsAHeldButtonReleaser()
    {
        Assert.Matches(new Regex(@"class MacOSVirtualMouse : [^\n]*IHeldButtonReleaser"), Source(Mouse));
    }

    [Fact]
    public void Reset_LetsGoOfWhatWasPosted_NotWhatWasRequested()
    {
        // prev = what macOS was told is down. The old code posted ups for the requested state (curr) and
        // forgot the left button whenever the two differed (a Right click binding pressed as the pen left).
        var reset = Body(Source(Mouse), "public void Reset()");
        Assert.Contains("ProcessKeyStates(_prevButtonStates, 0);", reset);
        Assert.DoesNotContain("ProcessKeyStates(_currButtonStates, 0);", reset);
        Assert.Contains("_prevButtonStates != 0 || _currButtonStates != 0", reset);
    }

    [Fact]
    public void DoubleClick_IsSkippedWhileTheLeftButtonIsHeld_AndSaysSo()
    {
        var dc = Body(Source(Mouse), "public void PostDoubleClick()");
        var skip = dc.IndexOf("IsButtonSet(_prevButtonStates, CGMouseButton.kCGMouseButtonLeft)", StringComparison.Ordinal);
        var firstPost = dc.IndexOf("PostClickEvent(", StringComparison.Ordinal);
        Assert.True(skip >= 0 && skip < firstPost, "the held-left check must come before the first raw click event");
        Assert.Contains("Double Click skipped", dc);
    }

    [Fact]
    public void TheDaemon_ReleasesHeldButtons_OnASettingsChange_AndWhenItCloses()
    {
        var daemon = Source(Daemon);
        Assert.Matches(new Regex(@"ReleaseHeldButtons\(dev\.Properties\.Name, dev\.OutputMode\);\s*dev\.OutputMode\?\.Dispose\(\);"), daemon);
        Assert.Contains("HeldButtonRelease.TryRelease(pointer, group, reason);", daemon);
        Assert.Contains("public void ReleaseAllHeldButtons()", daemon);

        var program = Source(Program);
        var run = program.IndexOf("await host.Run(daemon, cts.Token);", StringComparison.Ordinal);
        var release = program.IndexOf("daemon.ReleaseAllHeldButtons();", StringComparison.Ordinal);
        var done = program.IndexOf("daemonRunning = false;", run, StringComparison.Ordinal);
        Assert.True(run >= 0 && release > run && release < done, "release after the host stops, before the daemon reports it is finished");
        Assert.Contains("Couldn't release the held mouse buttons on exit", program);   // a failure is logged, not hidden
    }

    /// <summary>Version 27 (review of #107): .NET does not run the Unloading handler on SIGTERM (a test process with the
    /// daemon's exact shutdown shape exited 143 without its clean-up), so the polite stop never reached
    /// ReleaseAllHeldButtons. The daemon must handle SIGTERM itself, cancel the default termination and close the same way.</summary>
    [Fact]
    public void TheDaemon_HandlesSigTermItself_SoThePoliteStopReleasesHeldButtons()
    {
        var program = Source(Program);
        var reg = program.IndexOf("PosixSignalRegistration.Create(PosixSignal.SIGTERM", StringComparison.Ordinal);
        Assert.True(reg >= 0, "the daemon registers its own SIGTERM handler");
        var body = program.Substring(reg, program.IndexOf("});", reg, StringComparison.Ordinal) - reg);
        Assert.Contains("ctx.Cancel = true;", body);   // the main thread finishes (release, then exit), not the default kill
        Assert.Contains("CloseDaemon();", body);       // the same path as SIGHUP / Ctrl+C: cancel the host, wait for the release
        Assert.True(reg < program.IndexOf("await host.Run(daemon, cts.Token);", StringComparison.Ordinal), "registered before the host runs");
        Assert.Contains("GC.KeepAlive(sigTerm);", program);   // a collected registration would quietly stop handling SIGTERM
    }

    [Fact]
    public void TheApp_StopsTheDriverPolitelyBeforeKillingIt()
    {
        foreach (var file in new[] { "OpenTabletArtist/Services/DaemonLifecycleService.cs", "OpenTabletArtist/Services/StaleDriverGuard.cs" })
        {
            var text = Source(file);
            Assert.Contains("GracefulProcessStop.Stop(", text);
            Assert.DoesNotContain("proc.Kill();", text);
            Assert.DoesNotContain("p.Kill();", text);
        }
    }

    // ---- GracefulProcessStop, with real processes (macOS / Linux) ----

    [Fact]
    public void GracefulStop_EndsAWellBehavedProcess_WithoutKilling()
    {
        if (OperatingSystem.IsWindows()) return;
        using var proc = Process.Start(new ProcessStartInfo("/bin/sh") { ArgumentList = { "-c", "exec sleep 30" }, UseShellExecute = false })!;
        var politely = GracefulProcessStop.Stop(proc, TimeSpan.FromSeconds(5));
        Assert.True(politely);
        Assert.True(proc.HasExited);
    }

    [Fact]
    public void GracefulStop_KillsAProcessThatIgnoresTheRequest()
    {
        if (OperatingSystem.IsWindows()) return;
        // The shell ignores SIGTERM and waits; only the fall-back kill can end it.
        using var proc = Process.Start(new ProcessStartInfo("/bin/sh") { ArgumentList = { "-c", "trap '' TERM; while :; do sleep 1; done" }, UseShellExecute = false })!;
        System.Threading.Thread.Sleep(300);   // let the trap be installed
        var politely = GracefulProcessStop.Stop(proc, TimeSpan.FromMilliseconds(400));
        Assert.False(politely);
        Assert.True(proc.WaitForExit(5000));
    }

    // #107 review (13 Nov, "No silent failures"): each way the stop can go leaves its own log line.
    [Fact]
    public void GracefulStop_LogsWhenItHadToKill()
    {
        if (OperatingSystem.IsWindows()) return;
        var lines = new System.Collections.Concurrent.ConcurrentQueue<string>();
        void Capture(string line) => lines.Enqueue(line);
        AppLog.LineWritten += Capture;
        try
        {
            using var proc = Process.Start(new ProcessStartInfo("/bin/sh") { ArgumentList = { "-c", "trap '' TERM; while :; do sleep 1; done" }, UseShellExecute = false })!;
            System.Threading.Thread.Sleep(300);
            GracefulProcessStop.Stop(proc, TimeSpan.FromMilliseconds(400));
            proc.WaitForExit(5000);
            Assert.Contains(lines, l => l.Contains($"process {proc.Id} did not exit within") && l.Contains("killing it"));
        }
        finally { AppLog.LineWritten -= Capture; }
    }

    [Fact]
    public void GracefulStop_LogsAPoliteExit()
    {
        if (OperatingSystem.IsWindows()) return;
        var lines = new System.Collections.Concurrent.ConcurrentQueue<string>();
        void Capture(string line) => lines.Enqueue(line);
        AppLog.LineWritten += Capture;
        try
        {
            using var proc = Process.Start(new ProcessStartInfo("/bin/sh") { ArgumentList = { "-c", "exec sleep 30" }, UseShellExecute = false })!;
            Assert.True(GracefulProcessStop.Stop(proc, TimeSpan.FromSeconds(5)));
            Assert.Contains(lines, l => l.Contains($"process {proc.Id} exited by itself"));
        }
        finally { AppLog.LineWritten -= Capture; }
    }
}
