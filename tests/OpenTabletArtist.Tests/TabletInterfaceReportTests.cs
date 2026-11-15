using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>TASKS #81 (d): every tablet HID interface and which ones our driver opened (ioreg user-client creators).</summary>
public class TabletInterfaceReportTests
{
    static XElement Val(object? v) => v switch
    {
        string s => new XElement("string", s),
        int i => new XElement("integer", i),
        Dictionary<string, object?> d => new XElement("dict", d.SelectMany(kv => new[] { new XElement("key", kv.Key), Val(kv.Value) })),
        List<object?> l => new XElement("array", l.Select(Val)),
        _ => new XElement("string", ""),
    };
    static Dictionary<string, object?> D(params (string K, object? V)[] kv) => kv.ToDictionary(x => x.K, x => x.V);
    static string Plist(params Dictionary<string, object?>[] roots) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n"
        + new XElement("plist", new XAttribute("version", "1.0"), new XElement("array", roots.Select(Val)));

    static Dictionary<string, object?> Client(string creator) =>
        D(("IOObjectClass", "IOHIDLibUserClient"), ("IOUserClientCreator", creator));

    static Dictionary<string, object?> Iface(int page, int usage, int size, params Dictionary<string, object?>[] clients) => D(
        ("IOObjectClass", "IOHIDDevice"), ("Product", "Intuos PTK-440"), ("VendorID", 0x056A), ("ProductID", 0x00B8),
        ("PrimaryUsagePage", page), ("PrimaryUsage", usage), ("MaxInputReportSize", size), ("Transport", "USB"),
        ("SerialNumber", "8HIS003190"),
        ("IORegistryEntryChildren", clients.Cast<object?>().ToList()));

    static readonly string Intuos = Plist(
        Iface(0x0D, 0x02, 10, Client("pid 812, OpenTabletDriver.Daemon"), Client("pid 640, USB Overdrive Helper")),
        Iface(0xFF00, 0x01, 64),
        Iface(0x01, 0x02, 8, Client("pid 300, WindowServer")),
        D(("Product", "Magic Mouse"), ("VendorID", 0x05AC), ("ProductID", 0x0269), ("PrimaryUsagePage", 1), ("PrimaryUsage", 2)));

    [Fact]
    public void Parse_KeepsOnlyTabletInterfaces_WithTheirOpeners()
    {
        var list = TabletInterfaceReport.Parse(Intuos);
        Assert.Equal(3, list.Count);
        Assert.Equal(new[] { "pen", "vendor channel (ExpressKeys, ring, status)", "mouse" }, list.Select(i => i.Role));
        Assert.Equal(new[] { "OpenTabletDriver.Daemon", "USB Overdrive Helper" }, list[0].OpenedBy);
        Assert.Empty(list[1].OpenedBy);
    }

    [Fact]
    public void Format_SaysWhichInterfacesTheDriverOpened_AndWhoElseDid()
    {
        var text = TabletInterfaceReport.Format(Intuos);
        Assert.Contains("Intuos PTK-440 · VID 0x056A PID 0x00B8 · USB · 3 interface(s)", text);
        Assert.Contains("pen (usage 0x0D/0x02, reports up to 10 bytes): opened by our driver · also opened by USB Overdrive Helper", text);
        Assert.Contains("vendor channel (ExpressKeys, ring, status) (usage 0xFF00/0x01, reports up to 64 bytes): NOT opened by our driver", text);
        Assert.DoesNotContain("Magic Mouse", text);
        Assert.DoesNotContain("8HIS003190", text);
        Assert.DoesNotContain("has none of this tablet", text);
    }

    [Fact]
    public void Format_WarnsWhenTheDriverOpenedNothing()
    {
        var text = TabletInterfaceReport.Format(Plist(Iface(0x0D, 0x02, 10)));
        Assert.Contains("Our driver has none of this tablet's interfaces open.", text);
    }

    [Fact]
    public void Format_UnreadableOrEmpty_IsSaidPlainly()
    {
        Assert.Equal("  (not available: ioreg could not be read)", TabletInterfaceReport.Format(null));
        Assert.Equal("  (no tablet interface found)", TabletInterfaceReport.Format(Plist()));
        Assert.Equal("  (no tablet interface found)", TabletInterfaceReport.Format("not a plist"));
    }

    [Theory]
    [InlineData(0x0D, 0x05, "touch")]
    [InlineData(0x01, 0x06, "keyboard")]
    [InlineData(0x0C, 0x01, "media keys")]
    [InlineData(0x08, 0x01, "other")]
    public void Role_NamesTheInterface(int page, int usage, string role) =>
        Assert.Equal(role, TabletInterfaceReport.Role(page, usage));

    [Fact]
    public void Summary_HasTheTabletConnectionsSection()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var zip = DiagnosticBundle.Create(dir, new[] { "Intuos" }, Array.Empty<string>(), "1.0",
            inputDevices: () => "  (test)", permissions: () => "  (test)", eventTaps: () => "  (test)",
            tabletInterfaces: () => TabletInterfaceReport.Format(Intuos));
        using var z = ZipFile.OpenRead(zip);
        using var r = new StreamReader(z.GetEntry("summary.txt")!.Open());
        var s = r.ReadToEnd();
        Assert.Contains("Tablet connections (HID interfaces) and which ones the driver opened:", s);
        Assert.Contains("opened by our driver", s);
        Assert.DoesNotContain("8HIS003190", s);
    }

    [Fact]
    public void Format_KeepsTwoIdenticalTablets_Apart_WithoutPrintingTheirLocation()
    {
        Dictionary<string, object?> At(Dictionary<string, object?> iface, int loc) { iface["LocationID"] = loc; return iface; }
        var xml = Plist(
            At(Iface(0x0D, 0x02, 10, Client("pid 812, OpenTabletDriver.Daemon")), 0x14100000),
            At(Iface(0xFF00, 0x01, 64, Client("pid 812, OpenTabletDriver.Daemon")), 0x14100000),
            At(Iface(0x0D, 0x02, 10), 0x14200000),
            At(Iface(0xFF00, 0x01, 64), 0x14200000));
        var text = TabletInterfaceReport.Format(xml);
        Assert.Contains("Intuos PTK-440 (tablet 1) · VID 0x056A PID 0x00B8 · USB · 2 interface(s)", text);
        Assert.Contains("Intuos PTK-440 (tablet 2) · VID 0x056A PID 0x00B8 · USB · 2 interface(s)", text);
        var second = text[text.IndexOf("(tablet 2)", StringComparison.Ordinal)..];
        Assert.Contains("Our driver has none of this tablet's interfaces open.", second);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(text, "Our driver has none"));
        Assert.DoesNotContain("14100000", text);
        Assert.DoesNotContain(0x14100000.ToString(), text);
    }
}
