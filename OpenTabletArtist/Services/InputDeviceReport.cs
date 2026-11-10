using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace OpenTabletArtist.Services;

/// <summary>One mouse, trackpad or keyboard seen by the system. No serial number, no Bluetooth address, no location.</summary>
public sealed record InputDevice(
    string Kind,          // "mouse", "trackpad", "keyboard"
    string Name,          // product name, owner's name removed
    string? Vendor,       // manufacturer name when known
    int VendorId,
    int ProductId,
    string Connection,    // "USB", "Bluetooth", "Built-in", "Virtual (software)", ...
    string Driver,        // "Apple HID (...)", "Karabiner ... (DriverKit)", "third-party: ..."
    bool ThirdPartyDriver);

/// <summary>A third-party system extension (DriverKit) or kernel extension.</summary>
public sealed record InputDriverItem(string Kind, string BundleId, string? Version, string? Name, string? State);

/// <summary>
/// TASKS #4: "Mice and keyboards" section of the diagnostic report, so a report like "my mouse's left button stops
/// working" shows which pointing devices and which drivers were present. macOS: <c>ioreg -r -c IOHIDDevice -l -a</c>
/// (no permission needed, fast, gives type, IDs and connection), <c>systemextensionsctl list</c>, <c>kextstat -l</c>
/// and <c>ps</c> for known input tools. All parsing is in pure functions that take the command output as text, so
/// they are unit-tested with sample output. Privacy: serial numbers, Bluetooth addresses, location IDs and the
/// owner's name inside Bluetooth device names ("Zak's Magic Mouse") are never read or are removed.
/// </summary>
public static class InputDeviceReport
{
    // ---------- Collecting (runs the commands; everything fails soft) ----------

    /// <summary>The whole section as text, ready for summary.txt. Never throws.</summary>
    public static string Collect()
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                var hid = Run("/usr/sbin/ioreg", new[] { "-r", "-c", "IOHIDDevice", "-l", "-a" }, 8000);
                var sysext = Run("/usr/bin/systemextensionsctl", new[] { "list" }, 5000);
                var kext = Run("/usr/sbin/kextstat", new[] { "-l" }, 5000);
                var ps = Run("/bin/ps", new[] { "-axco", "comm" }, 3000);
                return FormatMac(hid, sysext, kext, ps);
            }
            if (OperatingSystem.IsLinux())
            {
                string? text = null;
                try { if (File.Exists("/proc/bus/input/devices")) text = File.ReadAllText("/proc/bus/input/devices"); } catch { }
                return FormatLinux(text);
            }
            return "  (not available on this system yet)";
        }
        catch (Exception ex)
        {
            AppLog.Debug("Diagnostics: input device list failed", ex);
            return "  (could not be read: " + ex.GetType().Name + ")";
        }
    }

    static string? Run(string exe, string[] args, int timeoutMs)
    {
        try
        {
            if (!File.Exists(exe)) return null;
            var psi = new ProcessStartInfo(exe)
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p == null) return null;
            var errTask = p.StandardError.ReadToEndAsync();   // drained so a full pipe can't block the child
            var outTask = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(true); } catch { }
                return null;
            }
            return outTask.Wait(2000) ? outTask.Result : null;
        }
        catch (Exception ex)
        {
            AppLog.Debug($"Diagnostics: couldn't run {exe}", ex);
            return null;
        }
    }

    // ---------- Formatting ----------

    internal static string FormatMac(string? ioregXml, string? systemExtensions, string? kextstat, string? processNames)
    {
        var sb = new StringBuilder();
        var devices = ioregXml == null ? null : ParseIoregHid(ioregXml);
        AppendDevices(sb, devices, ioregXml == null ? "ioreg could not be read" : null);
        var tools = KnownInputTools(ParseProcessNames(processNames));
        sb.AppendLine("  Mouse/keyboard tools running: " + (tools.Count == 0 ? "(none found)" : string.Join(", ", tools)));
        var ext = systemExtensions == null ? null : ParseSystemExtensions(systemExtensions).Where(IsThirdParty).ToList();
        sb.AppendLine("  Third-party driver extensions (DriverKit):" + (ext == null ? " (could not be read)" : ext.Count == 0 ? " (none)" : ""));
        if (ext != null) foreach (var e in ext)
            sb.AppendLine($"    {e.BundleId}{(e.Version != null ? " " + e.Version : "")}{(e.State != null ? " [" + e.State + "]" : "")}");
        var kx = kextstat == null ? null : ParseKextstat(kextstat).Where(IsThirdParty).ToList();
        sb.AppendLine("  Third-party kernel extensions loaded:" + (kx == null ? " (could not be read)" : kx.Count == 0 ? " (none)" : ""));
        if (kx != null) foreach (var e in kx)
            sb.AppendLine($"    {e.BundleId}{(e.Version != null ? " " + e.Version : "")}");
        return sb.ToString().TrimEnd();
    }

    internal static string FormatLinux(string? procBusInputDevices)
    {
        var sb = new StringBuilder();
        AppendDevices(sb, procBusInputDevices == null ? null : ParseLinuxInputDevices(procBusInputDevices),
            procBusInputDevices == null ? "/proc/bus/input/devices could not be read" : null);
        return sb.ToString().TrimEnd();
    }

    static void AppendDevices(StringBuilder sb, IReadOnlyList<InputDevice>? devices, string? problem)
    {
        if (devices == null) { sb.AppendLine("  (not available: " + problem + ")"); return; }
        if (devices.Count == 0) { sb.AppendLine("  (none found)"); return; }
        foreach (var d in devices) sb.AppendLine("  " + Describe(d));
    }

    internal static string Describe(InputDevice d)
    {
        var who = string.IsNullOrWhiteSpace(d.Vendor) || d.Name.StartsWith(d.Vendor!, StringComparison.OrdinalIgnoreCase)
            ? d.Name : $"{d.Name} ({d.Vendor})";
        return $"{d.Kind,-8} {who} · VID 0x{d.VendorId:X4} PID 0x{d.ProductId:X4} · {d.Connection} · driver: {d.Driver}";
    }

    // ---------- macOS: ioreg -r -c IOHIDDevice -l -a ----------

    static readonly Dictionary<int, string> VendorNames = new()
    {
        [0x05AC] = "Apple", [0x004C] = "Apple (Bluetooth vendor ID)", [0x046D] = "Logitech", [0x045E] = "Microsoft", [0x1532] = "Razer", [0x1B1C] = "Corsair",
        [0x1038] = "SteelSeries", [0x047D] = "Kensington", [0x413C] = "Dell", [0x17EF] = "Lenovo", [0x03F0] = "HP",
        [0x3434] = "Keychron", [0x258A] = "SINO WEALTH (Glorious and others)", [0x16C0] = "Karabiner virtual HID (or generic V-USB ID)",
        [0x0B05] = "ASUS", [0x04F2] = "Chicony", [0x04CA] = "Lite-On", [0x1EA7] = "SHARKOON/Generic",
    };

    /// <summary>Pure: turns the XML plist from <c>ioreg -r -c IOHIDDevice -l -a</c> into mice, trackpads and keyboards.
    /// Several HID interfaces of one device (consumer keys, vendor channel...) become one line.</summary>
    internal static IReadOnlyList<InputDevice> ParseIoregHid(string xml)
    {
        var roots = PlistReader.TryParse(xml) as List<object?>;
        var found = new List<InputDevice>();
        if (roots == null) return found;
        foreach (var node in roots.OfType<Dictionary<string, object?>>())
        {
            var kind = ClassifyHid(node);
            if (kind == null) continue;
            var vid = Int(node, "VendorID"); var pid = Int(node, "ProductID");
            if (vid == 0x056A || IsPenTablet(node)) continue;   // tablets are listed under "Tablets"; their pen pad also shows up as a mouse
            var product = Str(node, "Product") ?? Str(node, "IORegistryEntryName") ?? "(unnamed)";
            var manufacturer = Str(node, "Manufacturer");
            var name = RemoveOwner(product);
            var transport = Str(node, "Transport");
            var builtIn = node.TryGetValue("Built-In", out var bi) && bi is true;
            var (driver, third) = DescribeDriver(node, manufacturer, product);
            var connection = Connection(transport, builtIn, driver.Contains("virtual", StringComparison.OrdinalIgnoreCase) || third && vid == 0x16C0, name);
            var vendor = string.IsNullOrWhiteSpace(manufacturer) ? (VendorNames.TryGetValue(vid, out var vn) ? vn : null) : RemoveOwner(manufacturer!);
            found.Add(new InputDevice(kind, name, string.IsNullOrWhiteSpace(vendor) ? null : vendor, vid, pid, connection, driver, third));
        }
        return Merge(found);
    }

    /// <summary>A device that also has a Digitizer or Pen usage (13/1, 13/2) is a pen tablet, not a mouse. Touch pads (13/5) are not.</summary>
    static bool IsPenTablet(Dictionary<string, object?> n)
    {
        if (!n.TryGetValue("DeviceUsagePairs", out var dup) || dup is not List<object?> l) return false;
        var pairs = l.OfType<Dictionary<string, object?>>().Select(p => (Int(p, "DeviceUsagePage"), Int(p, "DeviceUsage"))).ToList();
        return pairs.Any(p => p.Item1 == 0x0D && (p.Item2 == 1 || p.Item2 == 2)) && !pairs.Contains((0x0D, 5));
    }

    /// <summary>One line per physical device and kind: keyboard+consumer-control+vendor interfaces collapse.</summary>
    static IReadOnlyList<InputDevice> Merge(List<InputDevice> list)
    {
        var order = new[] { "mouse", "trackpad", "keyboard" };
        return list.GroupBy(d => (d.Kind, d.Name, d.VendorId, d.ProductId, d.Connection, d.Driver))
            .Select(g => g.First())
            .OrderBy(d => Array.IndexOf(order, d.Kind)).ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>mouse / trackpad / keyboard from the HID usage pages, or null for anything else (pens, consumer keys only...).</summary>
    internal static string? ClassifyHid(Dictionary<string, object?> n)
    {
        var product = Str(n, "Product") ?? "";
        var pairs = new List<(int Page, int Usage)>();
        var primary = (Int(n, "PrimaryUsagePage"), Int(n, "PrimaryUsage"));
        pairs.Add(primary);
        if (n.TryGetValue("DeviceUsagePairs", out var dup) && dup is List<object?> l)
            foreach (var p in l.OfType<Dictionary<string, object?>>())
                pairs.Add((Int(p, "DeviceUsagePage"), Int(p, "DeviceUsage")));
        bool Has(int page, params int[] usages) => pairs.Any(p => p.Page == page && usages.Contains(p.Usage));
        var trackpadName = product.Contains("trackpad", StringComparison.OrdinalIgnoreCase)
            || product.Contains("touchpad", StringComparison.OrdinalIgnoreCase);
        // The primary usage decides first: "Apple Internal Keyboard / Trackpad" has a keyboard interface and a trackpad interface.
        if (primary == (0x0D, 5)) return "trackpad";                                  // Digitizer: touch pad
        if (primary.Item1 == 1 && (primary.Item2 == 6 || primary.Item2 == 7)) return "keyboard";
        if (primary.Item1 == 1 && (primary.Item2 == 2 || primary.Item2 == 1)) return trackpadName ? "trackpad" : "mouse";   // Bluetooth trackpads report as pointer
        // Vendor-defined interfaces (page 0xFF00 and up: sensors, firmware, backlight...) are not input devices, whatever the product is called.
        if (primary.Item1 >= 0xFF00) return null;
        if (Has(0x0D, 5)) return "trackpad";
        if (Has(1, 2, 1)) return trackpadName ? "trackpad" : "mouse";
        if (Has(1, 6, 7)) return "keyboard";
        return null;
    }

    static bool IsApple(string? id) => id != null && id.StartsWith("com.apple.", StringComparison.OrdinalIgnoreCase);

    /// <summary>Real macOS 27 data (Apple Silicon): Bluetooth devices are IOHIDUserDevice nodes without IOClass whose child
    /// AppleUserHIDEventService has IOUserClass AppleUserHIDEventDriver (bundle com.apple.AppleUserHIDDrivers); USB devices can be
    /// Apple DriverKit too (IOUserClass AppleUserUSBHostHIDDevice, IOUserServerName com.apple.driverkit...). So an IOUserClass alone does
    /// NOT mean a third-party driver: only a non-Apple bundle id or DriverKit server name does.</summary>
    static (string Driver, bool ThirdParty) DescribeDriver(Dictionary<string, object?> n, string? manufacturer, string product)
    {
        var drivers = new List<string>();     // event drivers below the HID device, user class preferred over kernel class
        var bundles = new List<string>();
        var servers = new List<string>();
        var userClasses = new List<string>();
        void Walk(Dictionary<string, object?> d, bool root)
        {
            var uc = Str(d, "IOUserClass");
            if (uc != null) userClasses.Add(uc);
            var c = uc ?? Str(d, "IOClass");
            if (!root && c != null && c != "IOHIDInterface" && !c.EndsWith("UserClient", StringComparison.Ordinal)) drivers.Add(c);
            if (Str(d, "CFBundleIdentifier") is { } b) bundles.Add(b);
            if (Str(d, "CFBundleIdentifierKernel") is { } bk) bundles.Add(bk);
            if (Str(d, "IOUserServerName") is { } sn) servers.Add(sn);
            if (d.TryGetValue("IORegistryEntryChildren", out var ch) && ch is List<object?> kids)
                foreach (var k in kids.OfType<Dictionary<string, object?>>()) Walk(k, false);
        }
        Walk(n, true);
        var rootClass = Str(n, "IOUserClass") ?? Str(n, "IOClass") ?? Str(n, "IOObjectClass");
        var all = string.Join(' ', new[] { manufacturer, product, rootClass }.Concat(drivers).Concat(userClasses).Concat(bundles).Concat(servers).Where(s => s != null));

        if (all.Contains("karabiner", StringComparison.OrdinalIgnoreCase) || all.Contains("pqrs", StringComparison.OrdinalIgnoreCase))
            return ("Karabiner-Elements virtual HID (DriverKit" + (userClasses.Count > 0 ? ", " + userClasses[0] : "") + ")", true);

        var thirdBundle = bundles.FirstOrDefault(b => !IsApple(b)) ?? servers.FirstOrDefault(sv => !IsApple(sv));
        if (thirdBundle != null)
            return ($"third-party: {userClasses.FirstOrDefault() ?? drivers.FirstOrDefault() ?? rootClass ?? "unknown class"} ({thirdBundle})", true);
        var apple = drivers.FirstOrDefault(c => c.Contains("HID", StringComparison.Ordinal) || c.StartsWith("AppleMultitouch", StringComparison.Ordinal))
            ?? drivers.FirstOrDefault() ?? rootClass;
        return ("Apple HID" + (apple != null ? " (" + apple + ")" : ""), false);
    }

    static string Connection(string? transport, bool builtIn, bool virtualDev, string name)
    {
        if (virtualDev || string.Equals(transport, "Virtual", StringComparison.OrdinalIgnoreCase)) return "Virtual (software)";
        if (builtIn) return "Built-in";
        return transport switch
        {
            null or "" => "unknown",
            var t when t.Equals("USB", StringComparison.OrdinalIgnoreCase) => "USB",
            var t when t.Contains("Low Energy", StringComparison.OrdinalIgnoreCase) => "Bluetooth Low Energy",
            var t when t.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase) => "Bluetooth",
            var t when t is "SPI" or "FIFO" or "I2C" or "SPU" or "SPMI" or "Apple Internal" || t.StartsWith("AID", StringComparison.OrdinalIgnoreCase) => "Built-in (" + t + ")",
            var t => t,
        };
    }

    /// <summary>Bluetooth products are often named after the owner ("Zak’s Magic Mouse"): keep only the part after it.
    /// Also drops anything that looks like a MAC address.</summary>
    internal static string RemoveOwner(string s)
    {
        s = OwnerPrefix.Replace(s.Trim(), "");
        s = MacAddress.Replace(s, "[removed]");
        return s.Trim();
    }
    static readonly Regex OwnerPrefix = new(@"^[^'’‘`]{1,40}['’‘`]s\s+(?=\S)", RegexOptions.Compiled);
    static readonly Regex MacAddress = new(@"\b[0-9A-Fa-f]{2}([-:])(?:[0-9A-Fa-f]{2}\1){4}[0-9A-Fa-f]{2}\b", RegexOptions.Compiled);

    static string? Str(Dictionary<string, object?> d, string key) => d.TryGetValue(key, out var v) && v is string s && s.Length > 0 ? s : null;
    static int Int(Dictionary<string, object?> d, string key) => d.TryGetValue(key, out var v) && v is long l ? (int)l : 0;

    // ---------- macOS: systemextensionsctl, kextstat, ps ----------

    static readonly Regex SysextLine = new(
        @"^\s*(?:\S+)\s+(?:\S+)\s+(?<team>[A-Z0-9\-]{1,10})\s+(?<id>[A-Za-z0-9._\-]+)\s+\((?<ver>[^)]*)\)\s+(?<name>.*?)\s*\[(?<state>[^\]]*)\]\s*$",
        RegexOptions.Compiled);

    /// <summary>Pure: parses <c>systemextensionsctl list</c> (driver extensions only; the other categories are not input drivers).</summary>
    internal static IReadOnlyList<InputDriverItem> ParseSystemExtensions(string text)
    {
        var list = new List<InputDriverItem>();
        var inDriver = false;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("---", StringComparison.Ordinal)) { inDriver = line.Contains("driver_extension", StringComparison.Ordinal); continue; }
            if (!inDriver) continue;
            var m = SysextLine.Match(line);
            if (!m.Success) continue;
            list.Add(new InputDriverItem("system extension", m.Groups["id"].Value, m.Groups["ver"].Value,
                m.Groups["name"].Value, m.Groups["state"].Value));
        }
        return list;
    }

    static readonly Regex KextLine = new(@"\s(?<id>[A-Za-z0-9._\-]+)\s+\((?<ver>[^)]+)\)\s+[0-9A-Fa-f\-]{36}", RegexOptions.Compiled);

    /// <summary>Pure: parses <c>kextstat -l</c> (one loaded kernel extension per line).</summary>
    internal static IReadOnlyList<InputDriverItem> ParseKextstat(string text)
    {
        var list = new List<InputDriverItem>();
        foreach (var line in text.Split('\n'))
        {
            var m = KextLine.Match(line);
            if (m.Success) list.Add(new InputDriverItem("kernel extension", m.Groups["id"].Value, m.Groups["ver"].Value, null, null));
        }
        return list;
    }

    static bool IsThirdParty(InputDriverItem i) => !i.BundleId.StartsWith("com.apple.", StringComparison.OrdinalIgnoreCase);

    internal static IReadOnlyList<string> ParseProcessNames(string? psOutput) =>
        (psOutput ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && l != "COMM").ToList();

    /// <summary>Programs that change how a mouse or keyboard behaves (button remapping, virtual devices).
    /// Matched by running process name, case-insensitive, whole-name contains.</summary>
    static readonly (string Needle, string Label)[] Tools =
    {
        ("logioptionsplus", "Logitech Options+"), ("logi options", "Logitech Options"), ("lghub", "Logitech G HUB"),
        ("logitech", "Logitech software"), ("logimgr", "Logitech Control Center"), ("steermouse", "SteerMouse"),
        ("karabiner", "Karabiner-Elements"), ("usb overdrive", "USB Overdrive"), ("usboverdrive", "USB Overdrive"),
        ("bettertouchtool", "BetterTouchTool"), ("linearmouse", "LinearMouse"), ("mac mouse fix", "Mac Mouse Fix"),
        ("scroll reverser", "Scroll Reverser"), ("smooze", "Smooze"), ("unnaturalscrollwheels", "UnnaturalScrollWheels"),
        ("hammerspoon", "Hammerspoon"), ("hyperkey", "Hyperkey"), ("raycast", "Raycast"), ("razer", "Razer Synapse"),
        ("corsair", "Corsair iCUE"), ("icue", "Corsair iCUE"), ("steelseries", "SteelSeries GG"), ("keychron", "Keychron software"),
    };

    internal static IReadOnlyList<string> KnownInputTools(IEnumerable<string> processNames)
    {
        var found = new List<string>();
        foreach (var p in processNames)
            foreach (var (needle, label) in Tools)
                if (p.Contains(needle, StringComparison.OrdinalIgnoreCase) && !found.Contains(label)) found.Add(label);
        return found;
    }

    // ---------- Linux: /proc/bus/input/devices (best effort) ----------

    /// <summary>Pure: parses /proc/bus/input/devices. Reads only the I: (bus, IDs), N: (name), H: (handlers) and B: EV= lines;
    /// never the U: unique ID line, which can hold a serial number or Bluetooth address.</summary>
    internal static IReadOnlyList<InputDevice> ParseLinuxInputDevices(string text)
    {
        var list = new List<InputDevice>();
        foreach (var block in Regex.Split(text.Replace("\r", ""), @"\n\s*\n"))
        {
            if (string.IsNullOrWhiteSpace(block)) continue;
            string? name = null, handlers = null; int bus = 0, vid = 0, pid = 0; long ev = 0;
            foreach (var line in block.Split('\n'))
            {
                if (line.StartsWith("I:", StringComparison.Ordinal))
                {
                    var m = Regex.Match(line, @"Bus=([0-9a-fA-F]+)\s+Vendor=([0-9a-fA-F]+)\s+Product=([0-9a-fA-F]+)");
                    if (m.Success) { bus = Convert.ToInt32(m.Groups[1].Value, 16); vid = Convert.ToInt32(m.Groups[2].Value, 16); pid = Convert.ToInt32(m.Groups[3].Value, 16); }
                }
                else if (line.StartsWith("N:", StringComparison.Ordinal)) name = Regex.Match(line, "Name=\"(.*)\"").Groups[1].Value;
                else if (line.StartsWith("H:", StringComparison.Ordinal)) handlers = line;
                else if (line.StartsWith("B: EV=", StringComparison.Ordinal))
                    try { ev = Convert.ToInt64(line[6..].Trim(), 16); } catch { }
            }
            if (name == null || handlers == null) continue;
            string? kind = null;
            if (name.Contains("touchpad", StringComparison.OrdinalIgnoreCase) || name.Contains("trackpad", StringComparison.OrdinalIgnoreCase)) kind = "trackpad";
            else if (Regex.IsMatch(handlers, @"\bmouse\d+")) kind = "mouse";
            else if (Regex.IsMatch(handlers, @"\bkbd\b") && (ev & 0x100000) != 0) kind = "keyboard";   // EV_REP: a real keyboard, not a power button
            if (kind == null || vid == 0x056A) continue;
            var connection = bus switch { 0x03 => "USB", 0x05 => "Bluetooth", 0x11 => "Built-in (PS/2)", 0x18 => "Built-in (I2C)", 0x19 => "Built-in", 0x06 => "Virtual (software)", _ => "bus 0x" + bus.ToString("X2") };
            list.Add(new InputDevice(kind, RemoveOwner(name), VendorNames.TryGetValue(vid, out var vn) ? vn : null, vid, pid, connection, "Linux kernel input (evdev)", false));
        }
        return Merge(list);
    }
}

/// <summary>Minimal reader for the XML property list that <c>ioreg -a</c> writes: dict, array, string, integer, true/false,
/// real, data and date. Dicts become Dictionary&lt;string, object?&gt;, arrays List&lt;object?&gt;, integers long.</summary>
internal static class PlistReader
{
    public static object? TryParse(string xml)
    {
        try
        {
            using var sr = new StringReader(xml);
            using var xr = XmlReader.Create(sr, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
            var doc = XDocument.Load(xr);
            var root = doc.Root?.Elements().FirstOrDefault();
            return root == null ? null : Read(root);
        }
        catch { return null; }
    }

    static object? Read(XElement e)
    {
        switch (e.Name.LocalName)
        {
            case "dict":
                var d = new Dictionary<string, object?>(StringComparer.Ordinal);
                var els = e.Elements().ToList();
                for (int i = 0; i + 1 < els.Count; i += 2)
                    if (els[i].Name.LocalName == "key") d[els[i].Value] = Read(els[i + 1]);
                return d;
            case "array": return e.Elements().Select(Read).ToList();
            case "string": return e.Value;
            case "integer": return long.TryParse(e.Value, out var l) ? l : 0L;
            case "true": return true;
            case "false": return false;
            case "real": return double.TryParse(e.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var r) ? r : 0d;
            default: return null;   // data, date: never needed (and data can hold serials)
        }
    }
}
