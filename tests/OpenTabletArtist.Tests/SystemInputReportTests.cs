using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>TASKS #81 (c) event taps and (e) permissions in the diagnostic report.</summary>
public class SystemInputReportTests
{
    static EventTapInfo Tap(string name, int pid, bool listenOnly, ulong mask, bool enabled = true) =>
        new(1, 1, listenOnly, mask, pid, name, 0, enabled);

    [Fact]
    public void Permissions_SayWhatIsMissing()
    {
        var text = SystemInputReport.FormatPermissions(accessibility: false, inputMonitoring: 1);
        Assert.Contains("Input Monitoring (driver reads the tablet): NOT allowed", text);
        Assert.Contains("Accessibility / Device Control (clicks, keys, scroll): NOT allowed", text);
        Assert.Contains("not asked yet", SystemInputReport.FormatPermissions(true, 2));
        Assert.Contains("Input Monitoring (driver reads the tablet): allowed", SystemInputReport.FormatPermissions(true, 0));
    }

    [Fact]
    public void Mask_NamesTheEventKinds()
    {
        ulong mask = (1UL << 1) | (1UL << 2) | (1UL << 10) | (1UL << 22);
        Assert.Equal("mouse clicks, scroll, keys", SystemInputReport.DescribeMask(mask));
        Assert.Equal("all events", SystemInputReport.DescribeMask(ulong.MaxValue));
        Assert.StartsWith("other", SystemInputReport.DescribeMask(1UL << 40));
    }

    [Fact]
    public void Taps_MarkOursAndCountOthersThatCanChangeEvents()
    {
        var taps = new[]
        {
            Tap("OpenTabletArtist", 100, listenOnly: true, 1UL << 10),
            Tap("OpenTabletDriver.Daemon", 200, listenOnly: false, 1UL << 1),
            Tap("USB Overdrive Helper", 300, listenOnly: false, (1UL << 1) | (1UL << 2)),
            Tap("Raycast", 400, listenOnly: true, 1UL << 10),
            Tap("Old tool", 500, listenOnly: false, 1UL << 1, enabled: false),
        };
        var text = SystemInputReport.FormatEventTaps(taps, ownPid: 100, driverPid: 200);
        Assert.Contains("OpenTabletArtist (this app) · pid 100 · listens only", text);
        Assert.Contains("OpenTabletDriver.Daemon (our driver)", text);
        Assert.Contains("USB Overdrive Helper · pid 300 · CAN CHANGE OR DROP events · mouse clicks", text);
        Assert.Contains("switched off", text);
        Assert.Contains("1 tap(s) of other programs can change or drop mouse/keyboard events.", text);
    }

    [Fact]
    public void Taps_NoneOrUnreadable_AreSaidPlainly()
    {
        Assert.Equal("  (none)", SystemInputReport.FormatEventTaps(System.Array.Empty<EventTapInfo>(), 1, null));
        Assert.Equal("  (could not be read: DllNotFoundException)", SystemInputReport.FormatEventTaps(null, 1, null, "DllNotFoundException"));
        Assert.Contains("No other program", SystemInputReport.FormatEventTaps(new[] { Tap("Raycast", 9, true, 1UL << 10) }, 1, null));
    }

    [Fact]
    public void Collect_OffMac_DoesNotThrow()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return;
        Assert.Equal("  (macOS only)", SystemInputReport.CollectEventTaps());
        Assert.Equal("  (macOS only)", SystemInputReport.CollectPermissions());
    }

    [Fact]
    public void TapStruct_MatchesCoreGraphicsLayout()
    {
        var t = typeof(SystemInputReport).GetNestedType("CGEventTapInformation", System.Reflection.BindingFlags.NonPublic)!;
        Assert.Equal(48, Marshal.SizeOf(t));
        Assert.Equal(16, (int)Marshal.OffsetOf(t, "eventsOfInterest"));
        Assert.Equal(32, (int)Marshal.OffsetOf(t, "enabled"));
    }

    [Fact]
    public void Summary_HasPermissionsAndEventTapSections()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var zip = DiagnosticBundle.Create(dir, new[] { "Intuos" }, Array.Empty<string>(), "1.0",
            inputDevices: () => "  (test)",
            permissions: () => SystemInputReport.FormatPermissions(false, 0),
            eventTaps: () => SystemInputReport.FormatEventTaps(new[] { Tap("SteerMouse", 7, false, 1UL << 1) }, 1, null));
        using var z = ZipFile.OpenRead(zip);
        using var r = new StreamReader(z.GetEntry("summary.txt")!.Open());
        var s = r.ReadToEnd();
        Assert.Contains("Permissions:\n  Input Monitoring (driver reads the tablet): allowed", s.Replace("\r", ""));
        Assert.Contains("Accessibility / Device Control (clicks, keys, scroll): NOT allowed", s);
        Assert.Contains("Programs that see mouse and keyboard events (event taps):", s);
        Assert.Contains("SteerMouse · pid 7 · CAN CHANGE OR DROP events", s);
    }
}
