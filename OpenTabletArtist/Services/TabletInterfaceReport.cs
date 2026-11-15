using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenTabletArtist.Services;

/// <summary>One HID interface of a tablet and the programs that opened it.</summary>
public sealed record TabletInterface(string Product, int VendorId, int ProductId, string Role, int UsagePage, int Usage,
    int MaxInputReport, string? Transport, IReadOnlyList<string> OpenedBy, string? Device = null);

/// <summary>
/// TASKS #81 (d): every HID interface of each tablet (pen, touch, ExpressKeys/vendor channel, the mouse and keyboard
/// interfaces some tablets add) and which of them our driver opened. Read from <c>ioreg -r -c IOHIDDevice -l -a</c>:
/// a program that opens an interface shows up as a child user client with <c>IOUserClientCreator = "pid N, name"</c>.
/// The serial number is never read. The location (or Bluetooth device id) is read only to tell two identical
/// tablets apart (<see cref="TabletInterface.Device"/>) and is never written into the report.
/// </summary>
public static class TabletInterfaceReport
{
    /// <summary>Wacom, Huion, XP-Pen, UC-Logic/Gaomon, Hanvon, Genius/KYE, Waltop, XenceLabs, Veikk.</summary>
    static readonly HashSet<int> TabletVendors = new() { 0x056A, 0x256C, 0x28BD, 0x5543, 0x0B57, 0x0458, 0x172F, 0x2D80, 0x2FEB };

    static readonly Regex Creator = new(@"^pid\s+(\d+),\s*(.+)$", RegexOptions.CultureInvariant);

    /// <summary>Pure: the tablet interfaces found in the ioreg plist.</summary>
    internal static IReadOnlyList<TabletInterface> Parse(string xml)
    {
        var list = new List<TabletInterface>();
        if (PlistReader.TryParse(xml) is not List<object?> roots) return list;
        foreach (var node in roots.OfType<Dictionary<string, object?>>())
        {
            var vid = Int(node, "VendorID");
            if (!TabletVendors.Contains(vid) && !HasPenUsage(node)) continue;
            var page = Int(node, "PrimaryUsagePage"); var usage = Int(node, "PrimaryUsage");
            var product = InputDeviceReport.RemoveOwner(Str(node, "Product") ?? Str(node, "IORegistryEntryName") ?? "(unnamed)");
            list.Add(new TabletInterface(product, vid, Int(node, "ProductID"), Role(page, usage), page, usage,
                Int(node, "MaxInputReportSize"), Str(node, "Transport"), Openers(node), DeviceKey(node)));
        }
        return list;
    }

    internal static string Role(int page, int usage) => (page, usage) switch
    {
        (0x0D, 0x01) => "digitizer",
        (0x0D, 0x02) => "pen",
        (0x0D, 0x04) => "touch screen",
        (0x0D, 0x05) => "touch",
        (0x0D, _) => "digitizer (other)",
        (0x01, 0x02) => "mouse",
        (0x01, 0x06) => "keyboard",
        (0x0C, _) => "media keys",
        _ when page >= 0xFF00 => "vendor channel (ExpressKeys, ring, status)",
        _ => "other",
    };

    /// <summary>Process names of the user clients under this interface; nested children are searched too.</summary>
    static IReadOnlyList<string> Openers(Dictionary<string, object?> node)
    {
        var names = new List<string>();
        void Walk(Dictionary<string, object?> d)
        {
            if (Str(d, "IOUserClientCreator") is { } c && Creator.Match(c) is { Success: true } m)
            {
                var name = m.Groups[2].Value.Trim();
                if (!names.Contains(name)) names.Add(name);
            }
            if (d.TryGetValue("IORegistryEntryChildren", out var ch) && ch is List<object?> kids)
                foreach (var k in kids.OfType<Dictionary<string, object?>>()) Walk(k);
        }
        if (node.TryGetValue("IORegistryEntryChildren", out var top) && top is List<object?> first)
            foreach (var k in first.OfType<Dictionary<string, object?>>()) Walk(k);
        return names;
    }

    internal static bool IsOurDriver(string process) =>
        process.Contains("OpenTabletDriver", StringComparison.OrdinalIgnoreCase) || process.StartsWith("OpenTabletDr", StringComparison.OrdinalIgnoreCase);

    /// <summary>Pure: the "Tablet connections" section.</summary>
    internal static string Format(string? ioregXml)
    {
        if (ioregXml == null) return "  (not available: ioreg could not be read)";
        var all = Parse(ioregXml);
        if (all.Count == 0) return "  (no tablet interface found)";
        var sb = new StringBuilder();
        var groups = all.GroupBy(i => (i.VendorId, i.ProductId, i.Device)).ToList();
        var seen = new Dictionary<(int, int), int>();
        foreach (var g in groups)
        {
            var first = g.First();
            var model = (first.VendorId, first.ProductId);
            var nth = seen[model] = seen.GetValueOrDefault(model) + 1;
            var twin = groups.Count(x => (x.Key.VendorId, x.Key.ProductId) == model) > 1 ? $" (tablet {nth})" : "";
            sb.AppendLine($"  {first.Product}{twin} · VID 0x{first.VendorId:X4} PID 0x{first.ProductId:X4}{(first.Transport != null ? " · " + first.Transport : "")} · {g.Count()} interface(s)");
            foreach (var i in g)
            {
                var ours = i.OpenedBy.Any(IsOurDriver);
                var others = i.OpenedBy.Where(p => !IsOurDriver(p)).ToList();
                sb.AppendLine($"    {i.Role} (usage 0x{i.UsagePage:X2}/0x{i.Usage:X2}, reports up to {i.MaxInputReport} bytes): "
                    + (ours ? "opened by our driver" : "NOT opened by our driver")
                    + (others.Count > 0 ? " · also opened by " + string.Join(", ", others) : ""));
            }
            if (!g.Any(i => i.OpenedBy.Any(IsOurDriver)))
                sb.AppendLine("    Our driver has none of this tablet's interfaces open.");
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>Section for summary.txt. Never throws.</summary>
    public static string Collect()
    {
        if (!OperatingSystem.IsMacOS()) return "  (macOS only)";
        try { return Format(InputDeviceReport.RunTool("/usr/sbin/ioreg", new[] { "-r", "-c", "IOHIDDevice", "-l", "-a" }, 8000)); }
        catch (Exception ex)
        {
            AppLog.Warn("Diagnostics: couldn't list the tablet interfaces.", ex);
            return "  (could not be read: " + ex.GetType().Name + ")";
        }
    }

    /// <summary>Same for every interface of one physical tablet; used only for grouping, never printed.</summary>
    static string? DeviceKey(Dictionary<string, object?> n) =>
        n.TryGetValue("LocationID", out var v) && v is long l ? "loc:" + l
        : Str(n, "PhysicalDeviceUniqueID") is { } u ? "id:" + u : null;

    static bool HasPenUsage(Dictionary<string, object?> n) =>
        n.TryGetValue("DeviceUsagePairs", out var dup) && dup is List<object?> l
        && l.OfType<Dictionary<string, object?>>().Any(p => Int(p, "DeviceUsagePage") == 0x0D && Int(p, "DeviceUsage") is 1 or 2);

    static string? Str(Dictionary<string, object?> d, string key) => d.TryGetValue(key, out var v) && v is string s && s.Length > 0 ? s : null;
    static int Int(Dictionary<string, object?> d, string key) => d.TryGetValue(key, out var v) && v is long l ? (int)l : 0;
}
