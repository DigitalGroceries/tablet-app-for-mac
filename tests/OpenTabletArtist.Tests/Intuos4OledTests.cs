using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletDriver.Plugin.Logging;
using OtdInterop;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>#7 / #18 (Brice, PTK-640): words on the Intuos4 ExpressKey screens.</summary>
public class Intuos4OledTests
{
    // gnome-settings-daemon's oled_scramble_icon(), written out separately, as the reference.
    private static byte[] GsdScramble(byte[] image)
    {
        var buf = (byte[])image.Clone();
        var o = (byte[])image.Clone();
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 32; x++)
            {
                byte l1 = (byte)(0x0F & buf[32 - 1 - x + 64 * y]);
                byte l2 = (byte)(0x0F & (buf[32 - 1 - x + 64 * y] >> 4));
                byte h1 = (byte)(0xF0 & (buf[64 - 1 - x + 64 * y] << 4));
                byte h2 = (byte)(0xF0 & buf[64 - 1 - x + 64 * y]);
                o[2 * x + 64 * y] = (byte)(h1 | l1);
                o[2 * x + 1 + 64 * y] = (byte)(h2 | l2);
            }
        return o;
    }

    [Fact]
    public void Scramble_matches_the_linux_encoder()
    {
        var rnd = new Random(7);
        for (int n = 0; n < 5; n++)
        {
            var img = new byte[1024];
            rnd.NextBytes(img);
            Assert.Equal(GsdScramble(img), Intuos4Oled.Scramble(img));
        }
    }

    [Fact]
    public void Scramble_moves_pixels_without_losing_any()
    {
        var img = new byte[1024];
        img[0] = 0xF0;   // only the top-left pixel lit
        var raw = Intuos4Oled.Scramble(img);
        var nibbles = raw.SelectMany(b => new[] { b >> 4, b & 0x0F }).ToList();
        Assert.Equal(1, nibbles.Count(v => v != 0));
        Assert.Equal(15, nibbles.Single(v => v != 0));
    }

    [Fact]
    public void Pack_puts_the_left_pixel_in_the_high_half()
    {
        var grey = new byte[64 * 32];
        grey[0] = 0xFF; grey[1] = 0x10; grey[64 * 32 - 1] = 0x80;
        var p = Intuos4Oled.Pack(grey);
        Assert.Equal(1024, p.Length);
        Assert.Equal(0xF1, p[0]);
        Assert.Equal(0x08, p[1023]);
    }

    [Fact]
    public void Rotate180_moves_top_left_to_bottom_right_and_back()
    {
        var img = new byte[1024];
        img[0] = 0xA0;
        var r = Intuos4Oled.Rotate180(img);
        Assert.Equal(0x0A, r[1023]);
        Assert.Equal(img, Intuos4Oled.Rotate180(r));
    }

    [Fact]
    public void Reports_are_start_four_chunks_stop_as_in_linux()
    {
        var img = new byte[1024];
        new Random(3).NextBytes(img);
        var reports = Intuos4Oled.BuildReports(5, img);
        Assert.Equal(6, reports.Length);
        Assert.Equal(new byte[] { 0x21, 1 }, reports[0]);
        Assert.Equal(new byte[] { 0x21, 0 }, reports[5]);
        var raw = Intuos4Oled.Scramble(img);
        for (int c = 0; c < 4; c++)
        {
            var r = reports[c + 1];
            Assert.Equal(259, r.Length);
            Assert.Equal(0x23, r[0]);
            Assert.Equal(5, r[1]);
            Assert.Equal(c, r[2]);
            Assert.Equal(raw.Skip(c * 256).Take(256), r.Skip(3));
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => Intuos4Oled.BuildReports(8, img));
    }

    [Theory]
    [InlineData(0x056A, 0xB8, false)]   // PTK-440: no screens
    [InlineData(0x056A, 0xB9, true)]
    [InlineData(0x056A, 0xBA, true)]
    [InlineData(0x056A, 0xBB, true)]
    [InlineData(0x056A, 0xBC, true)]
    [InlineData(0x28BD, 0xB9, false)]
    public void Only_models_with_screens(int vid, int pid, bool expected) =>
        Assert.Equal(expected, Intuos4Oled.HasOleds(vid, pid));

    [Fact]
    public void Names_match_the_driver_configs()
    {
        var dir = Path.Combine(Root(), "external/OpenTabletDriver/OpenTabletDriver.Configurations/Configurations/Wacom");
        foreach (var pid in Intuos4Oled.OledProductIds)
        {
            var name = Intuos4Oled.NameOf(pid)!;
            var file = Path.Combine(dir, name.Replace("Wacom ", "") + ".json");
            var j = JObject.Parse(File.ReadAllText(file));
            Assert.Equal(name, (string)j["Name"]!);
            Assert.Contains(((JArray)j["DigitizerIdentifiers"]!).Select(i => (int)i["ProductID"]!), id => id == pid);
        }
        Assert.True(Intuos4Oled.AnyOledTablet(new[] { "Wacom PTK-640" }));
        Assert.False(Intuos4Oled.AnyOledTablet(new[] { "Wacom PTK-440" }));
    }

    [Fact]
    public void Keystroke_shows_modifiers_then_key()
    {
        var b = new AuxBinding(AuxKind.Keyboard, new AuxCombo(false, true, false, "Z", Cmd: true), AuxKeyBinding.None, AuxKeyBinding.None);
        Assert.Equal(new[] { $"Shift {AuxKeyBinding.CmdLabel}", "Z" }, Intuos4Oled.LabelLines(b));
        var plain = new AuxBinding(AuxKind.Keyboard, new AuxCombo(false, false, false, "E"), AuxKeyBinding.None, AuxKeyBinding.None);
        Assert.Equal(new[] { "E" }, Intuos4Oled.LabelLines(plain));
    }

    [Fact]
    public void Mouse_action_shows_its_name_and_nothing_shows_nothing()
    {
        var right = new AuxBinding(AuxKind.Mouse, AuxCombo.Unbound, "Right", AuxKeyBinding.None);
        Assert.Equal(new[] { AuxKeyBinding.Describe(right) }, Intuos4Oled.LabelLines(right));
        Assert.Empty(Intuos4Oled.LabelLines(AuxBinding.Unbound));
        Assert.Equal(new[] { "Preset" }, Intuos4Oled.LabelLines(null, "Preset"));
    }

    [Theory]
    [InlineData(0f, false)]
    [InlineData(90f, false)]
    [InlineData(180f, true)]
    [InlineData(-180f, true)]
    [InlineData(540f, true)]
    public void Upside_down_means_about_180_degrees(float rotation, bool expected) =>
        Assert.Equal(expected, Intuos4Oled.IsUpsideDown(rotation));

    [Fact]
    public void Render_draws_words_and_leaves_empty_keys_dark()
    {
        Assert.Equal(Intuos4Oled.Blank(), Intuos4OledLabels.Render(Array.Empty<string>()));
        var undo = Intuos4OledLabels.Render(new[] { "Shift Cmd", "Z" });
        Assert.Equal(1024, undo.Length);
        Assert.Contains(undo, b => b != 0);
        // A long name still fits: nothing is drawn in the outer columns.
        var longName = Intuos4OledLabels.Render(new[] { "Move pen to pan and zoom" });
        Assert.Contains(longName, b => b != 0);
    }

    [Fact]
    public void Compose_is_dark_when_off_or_without_a_profile()
    {
        var images = Intuos4OledLabels.Compose(null, true, _ => Full());
        Assert.All(images, i => Assert.Equal(Intuos4Oled.Blank(), i));
        images = Intuos4OledLabels.Compose(ProfileWith("Z"), false, _ => Full());
        Assert.All(images, i => Assert.Equal(Intuos4Oled.Blank(), i));
        images = Intuos4OledLabels.Compose(ProfileWith("Z"), true, _ => Full());
        Assert.Equal(Full(), images[0]);
        Assert.Equal(Intuos4Oled.Blank(), images[1]);
    }

    [Fact]
    public async Task Sends_once_then_only_the_key_that_changed()
    {
        var daemon = new FakeOledDaemon();
        var conn = new FakeConnectionState { IsConnected = true };
        var profile = ProfileWith("Z");
        var svc = new Intuos4OledLabels(daemon, conn, n => n == "Wacom PTK-640" ? profile : null, _ => Full());
        await svc.RefreshAsync();
        Assert.Single(daemon.Sent);
        Assert.Equal(8 * 6, daemon.Sent[0].Length);   // first time: all eight keys

        await svc.RefreshAsync();
        Assert.Single(daemon.Sent);                     // nothing changed: nothing sent

        profile.BindingSettings.AuxButtons[3] = AuxKeyBinding.MakeKeyBinding("E");
        await svc.RefreshAsync();
        Assert.Equal(2, daemon.Sent.Count);
        Assert.Equal(6, daemon.Sent[1].Length);         // just key 3
        Assert.All(daemon.Sent[1].Where(r => r[0] == 0x23), r => Assert.Equal(3, r[1]));
    }

    [Fact]
    public async Task A_refusal_stops_further_tries_until_replugged()
    {
        var daemon = new FakeOledDaemon { Accept = false };
        var conn = new FakeConnectionState { IsConnected = true };
        var svc = new Intuos4OledLabels(daemon, conn, _ => ProfileWith("Z"), _ => Full());
        await svc.RefreshAsync();
        await svc.RefreshAsync();
        Assert.Single(daemon.Sent);
        daemon.Accept = true;
        daemon.RaiseTabletsChanged();
        await svc.RefreshAsync();
        Assert.Equal(2, daemon.Sent.Count);
    }

    [Fact]
    public async Task A_PTK440_gets_nothing()
    {
        var daemon = new FakeOledDaemon { Pid = 0xB8 };
        var conn = new FakeConnectionState { IsConnected = true };
        var svc = new Intuos4OledLabels(daemon, conn, _ => ProfileWith("Z"), _ => Full());
        await svc.RefreshAsync();
        Assert.Empty(daemon.Sent);
    }

    [Fact]
    public void Daemon_sends_the_sequence_through_one_stream_and_IOKit_for_long_reports()
    {
        var src = File.ReadAllText(Path.Combine(Root(), "external/OpenTabletDriver/OpenTabletDriver.Daemon/DriverDaemon.cs"));
        Assert.Contains("public Task<int> SendFeatureSequence(", src);
        Assert.Contains("using var stream = device.Open();", src);
        Assert.Contains("MacHidFeature.SendSequence(device.DevicePath, reports);", src);
        var contract = File.ReadAllText(Path.Combine(Root(), "external/OpenTabletDriver/OpenTabletDriver.Desktop/Contracts/IDriverDaemon.cs"));
        Assert.Contains("Task<int> SendFeatureSequence(int vendorID, int productID, string strInterface, byte[][] reports);", contract);
        var mac = File.ReadAllText(Path.Combine(Root(), "external/OpenTabletDriver/OpenTabletDriver.Daemon/MacHidFeature.cs"));
        Assert.Contains("IOHIDDeviceSetReport(device, kIOHIDReportTypeFeature, (IntPtr)r[0], r, (IntPtr)r.Length)", mac);
    }

    [Fact]
    public void Setting_is_on_the_settings_page_in_every_language()
    {
        var view = File.ReadAllText(Path.Combine(Root(), "OpenTabletArtist/Views/StartupView.axaml"));
        Assert.Contains("x:Name=\"OledLabelsToggle\"", view);
        var dir = Path.Combine(Root(), "OpenTabletArtist/Assets/i18n");
        var en = JObject.Parse(File.ReadAllText(Path.Combine(dir, "en.json")));
        foreach (var f in Directory.GetFiles(dir, "*.json"))
        {
            var j = JObject.Parse(File.ReadAllText(f));
            foreach (var k in new[] { "oled.section", "oled.toggle", "oled.description" })
            {
                Assert.False(string.IsNullOrWhiteSpace((string?)j[k]), $"{Path.GetFileName(f)} misses {k}");
                if (!f.EndsWith("en.json")) Assert.NotEqual((string?)en[k], (string?)j[k]);
            }
        }
    }

    private static byte[] Full() => Enumerable.Repeat((byte)0x77, 1024).ToArray();

    private static Profile ProfileWith(string key0)
    {
        var p = new Profile { Tablet = "Wacom PTK-640", OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)), BindingSettings = new BindingSettings() };
        p.BindingSettings.AuxButtons = new PluginSettingStoreCollection();
        p.BindingSettings.AuxButtons.Add(AuxKeyBinding.MakeKeyBinding(key0));
        for (int i = 1; i < 8; i++) p.BindingSettings.AuxButtons.Add(null);
        return p;
    }

    private static string Root([CallerFilePath] string f = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(f)!, "..", ".."));

    private sealed class FakeOledDaemon : IDaemonCapabilities
    {
        public int Pid { get; set; } = 0xB9;
        public bool Accept { get; set; } = true;
        public List<byte[][]> Sent { get; } = new();
        public event Action? TabletsChanged;
        public void RaiseTabletsChanged() => TabletsChanged?.Invoke();
        public event Action<JObject>? DeviceReport { add { } remove { } }
        public event Action<LogMessage>? LogReceived { add { } remove { } }
        public Task SetTabletDebugAsync(bool enabled) => Task.CompletedTask;
        public Task<List<LogMessage>> GetCurrentLogAsync() => Task.FromResult(new List<LogMessage>());
        public Task<bool> DownloadPluginAsync(OpenTabletDriver.Desktop.Reflection.Metadata.PluginMetadata metadata) => Task.FromResult(false);
        public Task<bool> UninstallPluginAsync(string directory) => Task.FromResult(false);
        public Task LoadPluginsAsync() => Task.CompletedTask;
        public Task<AppInfo?> GetAppInfoAsync() => Task.FromResult<AppInfo?>(null);
        public Task<JArray> GetTabletsAsync() => Task.FromResult(new JArray());
        public Task<JArray> GetDevicesAsync() => Task.FromResult(new JArray(new JObject
        {
            ["VendorID"] = 1386, ["ProductID"] = Pid, ["FeatureReportLength"] = 33,
            ["DeviceAttributes"] = new JObject { ["USB_INTERFACE_NUMBER"] = "0" },
        }));
        public Task<bool> SendFeatureSequenceAsync(int vendorId, int productId, string usbInterface, byte[][] reports)
        {
            Sent.Add(reports);
            return Task.FromResult(Accept);
        }
    }
}
