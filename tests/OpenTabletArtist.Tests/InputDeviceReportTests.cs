using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>TASKS #4: mice and keyboards in the diagnostic report. Sample data mirrors the shape of real
/// <c>ioreg -r -c IOHIDDevice -l -a</c>, <c>systemextensionsctl list</c>, <c>kextstat -l</c> output.
/// Real-Mac verification of the exact class names is still needed (see the task notes).</summary>
public class InputDeviceReportTests
{
    // ---- sample builders: plist XML in the exact structure ioreg -a writes (with DOCTYPE) ----
    static XElement Val(object? v) => v switch
    {
        string s => new XElement("string", s),
        int i => new XElement("integer", i),
        bool b => new XElement(b ? "true" : "false"),
        Dictionary<string, object?> d => Dict(d),
        List<object?> l => new XElement("array", l.Select(Val)),
        _ => new XElement("string", ""),
    };
    static XElement Dict(Dictionary<string, object?> d) =>
        new("dict", d.SelectMany(kv => new[] { new XElement("key", kv.Key), Val(kv.Value) }));
    static Dictionary<string, object?> D(params (string K, object? V)[] kv) => kv.ToDictionary(x => x.K, x => x.V);
    static List<object?> L(params object?[] items) => items.ToList();
    static Dictionary<string, object?> Pair(int page, int usage) => D(("DeviceUsagePage", page), ("DeviceUsage", usage));
    static string Plist(params Dictionary<string, object?>[] devices) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n"
        + new XElement("plist", new XAttribute("version", "1.0"), new XElement("array", devices.Select(Dict))).ToString();

    static Dictionary<string, object?> Hid(string product, string? maker, int vid, int pid, string transport,
        int page, int usage, string eventDriver, string ioClass = "IOHIDDevice", bool builtIn = false,
        params (string K, object? V)[] extra)
    {
        var d = D(("Product", product), ("VendorID", vid), ("ProductID", pid), ("Transport", transport),
            ("PrimaryUsagePage", page), ("PrimaryUsage", usage), ("IOClass", ioClass),
            ("SerialNumber", "F5K123456789ABCD"), ("LocationID", 336592896),
            ("DeviceUsagePairs", L(Pair(page, usage))),
            ("IORegistryEntryChildren", L(D(("IOClass", "IOHIDInterface")), D(("IOClass", eventDriver)))));
        if (maker != null) d["Manufacturer"] = maker;
        if (builtIn) d["Built-In"] = true;
        foreach (var (k, v) in extra) d[k] = v;
        return d;
    }

    static Dictionary<string, object?> MagicMouse() => Hid("Zak’s Magic Mouse", "Apple Inc.", 1452, 617, "Bluetooth", 1, 2,
        "AppleMultitouchMouseHIDEventDriver", "AppleUserHIDDevice", false,
        ("BD_ADDR", "d4-61-9d-12-34-56"), ("DeviceAddress", "d4-61-9d-12-34-56"));
    static Dictionary<string, object?> MagicKeyboard() => Hid("Zak’s Magic Keyboard", "Apple Inc.", 1452, 615, "Bluetooth", 1, 6,
        "AppleHIDKeyboardEventDriverV2", "AppleBluetoothHIDDevice");
    static Dictionary<string, object?> MagicKeyboardConsumer() => Hid("Zak’s Magic Keyboard", "Apple Inc.", 1452, 615, "Bluetooth", 12, 1,
        "IOHIDEventDriver", "AppleBluetoothHIDDevice");
    static Dictionary<string, object?> LogiMouseIf() => Hid("USB Receiver", "Logitech", 1133, 50475, "USB", 1, 2, "IOHIDEventDriver", "AppleUserUSBHostHIDDevice");
    static Dictionary<string, object?> LogiKeyboardIf() => Hid("USB Receiver", "Logitech", 1133, 50475, "USB", 1, 6, "AppleHIDKeyboardEventDriverV2", "AppleUserUSBHostHIDDevice");
    static Dictionary<string, object?> LogiVendorIf() => Hid("USB Receiver", "Logitech", 1133, 50475, "USB", 65280, 1, "IOHIDEventDriver", "AppleUserUSBHostHIDDevice");
    static Dictionary<string, object?> BuiltInTrackpad() => Hid("Apple Internal Keyboard / Trackpad", "Apple Inc.", 1452, 835, "SPI", 13, 5,
        "AppleMultitouchTrackpadHIDEventDriver", "AppleHIDTransportHIDDevice", builtIn: true);
    static Dictionary<string, object?> BuiltInKeyboard() => Hid("Apple Internal Keyboard / Trackpad", "Apple Inc.", 1452, 835, "SPI", 1, 6,
        "AppleHIDKeyboardEventDriverV2", "AppleHIDTransportHIDDevice", builtIn: true);
    static Dictionary<string, object?> KarabinerKeyboard() => Hid("Karabiner DriverKit VirtualHIDKeyboard 1.8.0", "pqrs.org", 5824, 10203, "Virtual", 1, 6,
        "IOHIDEventDriver", "IOUserHIDDevice", false,
        ("IOUserClass", "org_pqrs_Karabiner_DriverKit_VirtualHIDKeyboard"), ("CFBundleIdentifier", "org.pqrs.Karabiner-DriverKit-VirtualHIDDevice"));
    static Dictionary<string, object?> KarabinerPointing() => Hid("Karabiner DriverKit VirtualHIDPointing 1.8.0", "pqrs.org", 5824, 10204, "Virtual", 1, 2,
        "IOHIDEventDriver", "IOUserHIDDevice", false,
        ("IOUserClass", "org_pqrs_Karabiner_DriverKit_VirtualHIDPointing"), ("CFBundleIdentifier", "org.pqrs.Karabiner-DriverKit-VirtualHIDDevice"));
    static Dictionary<string, object?> WacomPad() => Hid("Intuos Pro M", "Wacom Co.,Ltd.", 1386, 782, "USB", 13, 2, "IOHIDEventDriver", "AppleUserUSBHostHIDDevice");

    static string AllSamples() => Plist(MagicMouse(), MagicKeyboard(), MagicKeyboardConsumer(), LogiMouseIf(), LogiKeyboardIf(),
        LogiVendorIf(), BuiltInTrackpad(), BuiltInKeyboard(), KarabinerKeyboard(), KarabinerPointing(), WacomPad());

    const string SysExt = @"2 extension(s)
--- com.apple.system_extension.driver_extension
enabled	active	teamID	bundleID (version)	name	[state]
*	*	G43BCU2T37	org.pqrs.Karabiner-DriverKit-VirtualHIDDevice (1.8.0/1.8.0)	org.pqrs.Karabiner-DriverKit-VirtualHIDDevice	[activated enabled]
*	*	ABCDE12345	com.example.gamepad.driver (2.0/20)	Example Gamepad	[activated waiting for user]
--- com.apple.system_extension.network_extension
enabled	active	teamID	bundleID (version)	name	[state]
*	*	XYZ0123456	com.example.vpn.ext (1.0/1)	VPN	[activated enabled]
";
    const string Kexts = @"Index Refs Address            Size       Wired      Name (Version) UUID <Linked Against>
    1    0 0xffffff7f80000000 0x1000     0x1000     com.apple.iokit.IOHIDFamily (2.0.0) 11111111-2222-3333-4444-555555555555 <1>
  150    0 0xffffff7f8a2d1000 0x6000     0x6000     com.logitech.manager.kext (1.2.3) AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE <12 7>
";

    // ---- ioreg ----

    [Fact]
    public void Plist_with_doctype_parses_and_unwanted_entries_are_dropped()
    {
        var list = InputDeviceReport.ParseIoregHid(AllSamples());
        // Magic Mouse, Magic Keyboard (consumer-control interface merged away), Logitech mouse+keyboard (vendor interface dropped),
        // built-in trackpad+keyboard, Karabiner keyboard+pointing; the Wacom pad is not listed.
        Assert.Equal(new[] { "mouse", "mouse", "mouse", "trackpad", "keyboard", "keyboard", "keyboard", "keyboard" }, list.Select(d => d.Kind));
        Assert.DoesNotContain(list, d => d.VendorId == 1386);
    }

    [Fact]
    public void Magic_mouse_is_bluetooth_apple_hid_without_owner_name()
    {
        var m = InputDeviceReport.ParseIoregHid(AllSamples()).Single(d => d.Name == "Magic Mouse");
        Assert.Equal((1452, 617, "Bluetooth", false), (m.VendorId, m.ProductId, m.Connection, m.ThirdPartyDriver));
        Assert.Equal("Apple Inc.", m.Vendor);
        Assert.Equal("Apple HID (AppleMultitouchMouseHIDEventDriver)", m.Driver);
    }

    [Fact]
    public void Magic_keyboard_appears_once_even_with_several_hid_interfaces()
    {
        var k = InputDeviceReport.ParseIoregHid(AllSamples()).Where(d => d.Name == "Magic Keyboard").ToList();
        var one = Assert.Single(k);
        Assert.Equal("keyboard", one.Kind);
        Assert.Equal("Apple HID (AppleHIDKeyboardEventDriverV2)", one.Driver);
    }

    [Fact]
    public void Logitech_receiver_gives_a_mouse_and_a_keyboard_over_usb()
    {
        var l = InputDeviceReport.ParseIoregHid(AllSamples()).Where(d => d.VendorId == 0x046D).ToList();
        Assert.Equal(new[] { "mouse", "keyboard" }, l.Select(d => d.Kind));
        Assert.All(l, d => { Assert.Equal("USB", d.Connection); Assert.Equal(0xC52B, d.ProductId); Assert.Equal("USB Receiver", d.Name); });
    }

    [Fact]
    public void Built_in_trackpad_and_keyboard_are_built_in()
    {
        var b = InputDeviceReport.ParseIoregHid(AllSamples()).Where(d => d.ProductId == 835).ToList();
        Assert.Equal(new[] { "trackpad", "keyboard" }, b.Select(d => d.Kind));
        Assert.All(b, d => Assert.Equal("Built-in", d.Connection));
        Assert.Equal("Apple HID (AppleMultitouchTrackpadHIDEventDriver)", b[0].Driver);
    }

    [Fact]
    public void Karabiner_virtual_devices_are_marked_virtual_and_third_party()
    {
        var k = InputDeviceReport.ParseIoregHid(AllSamples()).Where(d => d.VendorId == 5824).ToList();
        Assert.Equal(2, k.Count);
        Assert.All(k, d =>
        {
            Assert.True(d.ThirdPartyDriver);
            Assert.Equal("Virtual (software)", d.Connection);
            Assert.StartsWith("Karabiner-Elements virtual HID (DriverKit", d.Driver);
        });
        Assert.Contains(k, d => d.Kind == "mouse");
        Assert.Contains(k, d => d.Kind == "keyboard");
    }

    [Fact]
    public void Third_party_driverkit_device_shows_class_and_bundle()
    {
        var x = Hid("Fancy Mouse", "Acme", 4660, 22136, "USB", 1, 2, "IOHIDEventDriver", "IOUserHIDDevice", false,
            ("IOUserClass", "AcmeMouseDriver"), ("CFBundleIdentifier", "com.acme.mouse.driver"));
        var d = Assert.Single(InputDeviceReport.ParseIoregHid(Plist(x)));
        Assert.True(d.ThirdPartyDriver);
        Assert.Equal("third-party: AcmeMouseDriver (com.acme.mouse.driver)", d.Driver);
    }

    [Fact]
    public void Output_never_contains_serials_addresses_location_or_owner_name()
    {
        var text = InputDeviceReport.FormatMac(AllSamples(), SysExt, Kexts, "launchd\nSteerMouse\n");
        foreach (var bad in new[] { "F5K123456789ABCD", "d4-61-9d", "336592896", "Zak" })
            Assert.DoesNotContain(bad, text);
    }

    [Theory]
    [InlineData("Zak’s Magic Mouse", "Magic Mouse")]
    [InlineData("Helgar's Magic Trackpad 2", "Magic Trackpad 2")]
    [InlineData("MX Master 3S", "MX Master 3S")]
    [InlineData("Logi Pebble Mouse d4:61:9d:12:34:56", "Logi Pebble Mouse [removed]")]
    public void Owner_names_and_mac_addresses_are_removed(string input, string expected) =>
        Assert.Equal(expected, InputDeviceReport.RemoveOwner(input));

    [Fact]
    public void Garbage_or_empty_ioreg_gives_no_devices_and_does_not_throw()
    {
        Assert.Empty(InputDeviceReport.ParseIoregHid("not xml"));
        Assert.Empty(InputDeviceReport.ParseIoregHid(""));
        Assert.Empty(InputDeviceReport.ParseIoregHid(Plist()));
    }

    // ---- systemextensionsctl / kextstat / ps ----

    [Fact]
    public void System_extensions_lists_only_driver_extensions()
    {
        var e = InputDeviceReport.ParseSystemExtensions(SysExt);
        Assert.Equal(new[] { "org.pqrs.Karabiner-DriverKit-VirtualHIDDevice", "com.example.gamepad.driver" }, e.Select(x => x.BundleId));
        Assert.Equal("1.8.0/1.8.0", e[0].Version);
        Assert.Equal("activated waiting for user", e[1].State);
        Assert.Equal("Example Gamepad", e[1].Name);
    }

    [Fact]
    public void Kextstat_is_parsed_and_apple_kexts_are_filtered_in_the_report()
    {
        var k = InputDeviceReport.ParseKextstat(Kexts);
        Assert.Equal(new[] { "com.apple.iokit.IOHIDFamily", "com.logitech.manager.kext" }, k.Select(x => x.BundleId));
        Assert.Equal("1.2.3", k[1].Version);
        var text = InputDeviceReport.FormatMac(Plist(), "", Kexts, "");
        Assert.Contains("com.logitech.manager.kext 1.2.3", text);
        Assert.DoesNotContain("IOHIDFamily", text);
    }

    [Fact]
    public void Known_mouse_tools_are_found_by_process_name()
    {
        var tools = InputDeviceReport.KnownInputTools(InputDeviceReport.ParseProcessNames(
            "COMM\nlaunchd\nlogioptionsplus_agent\nkarabiner_grabber\nkarabiner_observer\nSteerMouse\nFinder\n"));
        Assert.Equal(new[] { "Logitech Options+", "Karabiner-Elements", "SteerMouse" }, tools);
    }

    // ---- full section: example output ----

    [Fact]
    public void Mac_section_example()
    {
        var text = InputDeviceReport.FormatMac(AllSamples(), SysExt, Kexts, "logioptionsplus_agent\nkarabiner_grabber\n");
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "input-devices-example.txt"), text);
        Assert.Contains("mouse    Magic Mouse (Apple Inc.) · VID 0x05AC PID 0x0269 · Bluetooth · driver: Apple HID (AppleMultitouchMouseHIDEventDriver)", text);
        Assert.Contains("trackpad Apple Internal Keyboard / Trackpad (Apple Inc.) · VID 0x05AC PID 0x0343 · Built-in · driver: Apple HID", text);
        Assert.Contains("Mouse/keyboard tools running: Logitech Options+, Karabiner-Elements", text);
        Assert.Contains("    org.pqrs.Karabiner-DriverKit-VirtualHIDDevice 1.8.0/1.8.0 [activated enabled]", text);
    }

    [Fact]
    public void Unreadable_commands_say_so_instead_of_throwing()
    {
        var text = InputDeviceReport.FormatMac(null, null, null, null);
        Assert.Contains("ioreg could not be read", text);
        Assert.Contains("(could not be read)", text);
        Assert.Contains("(none found)", text);
    }

    // ---- Linux ----

    const string ProcInput = @"I: Bus=0003 Vendor=046d Product=c52b Version=0111
N: Name=""Logitech USB Receiver""
P: Phys=usb-0000:00:14.0-2/input0
U: Uniq=SERIAL-SHOULD-NOT-APPEAR
H: Handlers=sysrq kbd event3 leds
B: PROP=0
B: EV=120013

I: Bus=0003 Vendor=046d Product=c52b Version=0111
N: Name=""Logitech USB Receiver Mouse""
H: Handlers=mouse0 event4
B: EV=17

I: Bus=0019 Vendor=0000 Product=0001 Version=0000
N: Name=""Power Button""
H: Handlers=kbd event0
B: EV=3

I: Bus=0011 Vendor=0002 Product=0007 Version=01b1
N: Name=""SynPS/2 Synaptics TouchPad""
H: Handlers=mouse1 event7
B: EV=b

I: Bus=0005 Vendor=05ac Product=0269 Version=0001
N: Name=""Zak's Magic Mouse""
U: Uniq=d4:61:9d:12:34:56
H: Handlers=mouse2 event9
B: EV=17
";

    [Fact]
    public void Linux_proc_input_devices_is_parsed_best_effort_without_uniq()
    {
        var list = InputDeviceReport.ParseLinuxInputDevices(ProcInput);
        Assert.Equal(new[] { "mouse", "mouse", "trackpad", "keyboard" }, list.Select(d => d.Kind));
        Assert.DoesNotContain(list, d => d.Name == "Power Button");
        Assert.Equal("Magic Mouse", list.First(d => d.VendorId == 0x05AC).Name);
        Assert.Equal("Bluetooth", list.First(d => d.VendorId == 0x05AC).Connection);
        var text = InputDeviceReport.FormatLinux(ProcInput);
        Assert.DoesNotContain("SERIAL-SHOULD-NOT-APPEAR", text);
        Assert.DoesNotContain("d4:61", text);
        Assert.Contains("Built-in (PS/2)", text);
    }

    [Fact]
    public void Linux_without_proc_file_says_not_available() =>
        Assert.Contains("not available", InputDeviceReport.FormatLinux(null));

    // ---- in the zip ----

    [Fact]
    public void Summary_has_the_mice_and_keyboards_section()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var zip = DiagnosticBundle.Create(dir, new[] { "Intuos" }, Array.Empty<string>(), "1.0",
            inputDevices: () => InputDeviceReport.FormatMac(AllSamples(), SysExt, Kexts, "SteerMouse"));
        using var z = ZipFile.OpenRead(zip);
        using var r = new StreamReader(z.GetEntry("summary.txt")!.Open());
        var s = r.ReadToEnd();
        Assert.Contains("Mice, trackpads and keyboards:", s);
        Assert.Contains("Magic Mouse", s);
        Assert.Contains("Mouse/keyboard tools running: SteerMouse", s);
        Assert.True(s.IndexOf("Other tablet drivers found:", StringComparison.Ordinal) < s.IndexOf("Mice, trackpads and keyboards:", StringComparison.Ordinal));
    }

    [Fact]
    public void Live_collect_never_throws_on_this_machine() =>
        Assert.False(string.IsNullOrWhiteSpace(InputDeviceReport.Collect()));
}

/// <summary>Real output from an Apple Silicon Mac on macOS 27 (ioreg -r -c IOHIDDevice -l -a, systemextensionsctl list),
/// trimmed to the keys the parser reads, with serials, addresses, location IDs, report descriptors and the owner's name removed.</summary>
public class InputDeviceReportRealMacTests
{
    static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
    static string Ioreg => Fixture("ioreg-hid-apple-silicon-macos27.plist");
    static string Sysext => Fixture("systemextensionsctl-macos27.txt");

    [Fact]
    public void Real_mac_lists_the_built_in_pair_the_bluetooth_keyboard_and_the_mx_master_only()
    {
        var list = InputDeviceReport.ParseIoregHid(Ioreg);
        Assert.Equal(new[]
        {
            "mouse|MX Master 3S M|Bluetooth Low Energy",
            "trackpad|Apple Internal Keyboard / Trackpad|Built-in",
            "keyboard|Apple Internal Keyboard / Trackpad|Built-in",
            "keyboard|Magic Keyboard|Bluetooth",
        }, list.Select(d => $"{d.Kind}|{d.Name}|{d.Connection}"));
    }

    [Fact]
    public void Real_mac_wacom_tablet_motion_sensors_audio_and_vendor_interfaces_are_skipped()
    {
        var list = InputDeviceReport.ParseIoregHid(Ioreg);
        Assert.DoesNotContain(list, d => d.VendorId == 0x056A);                       // PTK-440 pen tablet shows as a mouse (1,2) + digitizer (13,1)
        Assert.DoesNotContain(list, d => d.Name is "accel" or "gyro" or "als" or "Headset" or "Keyboard Backlight" or "BTM");
    }

    [Fact]
    public void Real_mac_drivers_are_apple_hid_and_none_is_called_third_party()
    {
        var list = InputDeviceReport.ParseIoregHid(Ioreg);
        Assert.All(list, d => Assert.False(d.ThirdPartyDriver));
        Assert.Equal("Apple HID (AppleUserHIDEventDriver)", list.Single(d => d.Name == "MX Master 3S M").Driver);
        Assert.Equal("Apple HID (AppleMultitouchTrackpadHIDEventDriver)", list.Single(d => d.Kind == "trackpad").Driver);
        Assert.Equal("Apple HID (AppleHIDKeyboardEventDriverV2)", list.Single(d => d.Name.StartsWith("Apple Internal") && d.Kind == "keyboard").Driver);
    }

    [Fact]
    public void Real_mac_bluetooth_keyboard_vendor_id_is_the_bluetooth_one_and_owner_name_is_gone()
    {
        var k = InputDeviceReport.ParseIoregHid(Ioreg).Single(d => d.Name == "Magic Keyboard");
        Assert.Equal(0x004C, k.VendorId);     // Apple's Bluetooth SIG company ID, not 0x05AC
        Assert.Equal(801, k.ProductId);
        Assert.Equal("Apple (Bluetooth vendor ID)", k.Vendor is "Apple Inc." ? "Apple (Bluetooth vendor ID)" : k.Vendor);
    }

    [Fact]
    public void Real_mac_system_extensions_list_the_motu_driver_extension_only()
    {
        var e = InputDeviceReport.ParseSystemExtensions(Sysext);
        var one = Assert.Single(e);   // the network extensions (LuLu, Proton VPN) are not input drivers
        Assert.Equal("com.motu.driver.gen5.dext", one.BundleId);
        Assert.Equal("2.0.2 b72db03fb/2.0.2 b72db03fb", one.Version);
        Assert.Equal("activated enabled", one.State);
    }

    [Fact]
    public void Real_mac_report_section_is_clean_and_complete()
    {
        var text = InputDeviceReport.FormatMac(Ioreg, Sysext, "", "launchd\nFinder\n");
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "input-devices-real-mac.txt"), text);
        Assert.Contains("Mouse/keyboard tools running: (none found)", text);
        Assert.Contains("Third-party kernel extensions loaded: (none)", text);
        Assert.Contains("com.motu.driver.gen5.dext", text);
        foreach (var bad in new[] { "Zak", "erial", "DeviceAddress", "PhysicalDeviceUniqueID" })
            Assert.DoesNotContain(bad, text);
        Assert.DoesNotMatch(@"[0-9A-Fa-f]{2}([-:])([0-9A-Fa-f]{2}\1){4}[0-9A-Fa-f]{2}", text);
    }

    [Fact]
    public void Fixture_files_contain_no_serial_address_location_or_owner()
    {
        foreach (var bad in new[] { "Zak", "SerialNumber", "DeviceAddress", "LocationID", "PhysicalDeviceUniqueID", "ReportDescriptor" })
            Assert.DoesNotContain(bad, Ioreg);
    }
}
