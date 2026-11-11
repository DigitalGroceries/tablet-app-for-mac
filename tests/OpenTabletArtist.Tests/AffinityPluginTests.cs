using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Newtonsoft.Json.Linq;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>#39 (Zak): the Affinity "Smooth pen strokes" plugin and its installer.</summary>
public class AffinityPluginTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "affinity-plugin-" + Guid.NewGuid().ToString("N"));

    public AffinityPluginTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    private static string Root([CallerFilePath] string f = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(f)!, "..", ".."));

    private static string Bundle => Path.Combine(Root(), "OpenTabletArtist/Assets/affinity-plugin/SmoothPenStrokes.plugin");

    private static IEnumerable<(string, Func<Stream>)> RepoFiles() =>
        Directory.GetFiles(Bundle, "*", SearchOption.AllDirectories)
            .Select(f => (Path.GetRelativePath(Bundle, f).Replace(Path.DirectorySeparatorChar, '/'), (Func<Stream>)(() => File.OpenRead(f))));

    private AffinityPlugin Make(out string home, out string apps)
    {
        home = Path.Combine(_tmp, "home");
        apps = Path.Combine(_tmp, "Applications");
        Directory.CreateDirectory(home);
        Directory.CreateDirectory(apps);
        return new AffinityPlugin(home, apps, RepoFiles);
    }

    [Fact]
    public void Bundle_has_what_Affinity_looks_for()
    {
        var plist = File.ReadAllText(Path.Combine(Bundle, "Contents/Info.plist"));
        Assert.Contains("<string>8BFM</string>", plist);
        Assert.Contains("<string>8BIM</string>", plist);
        Assert.Contains("<key>CFBundleExecutable</key>\n  <string>SmoothPenStrokes</string>", plist);
        Assert.Equal("8BFM8BIM", File.ReadAllText(Path.Combine(Bundle, "Contents/PkgInfo")));
        Assert.True(File.Exists(Path.Combine(Bundle, "Contents/Resources/SmoothPenStrokes.rsrc")));
    }

    [Fact]
    public void Resource_file_holds_one_PiPL_with_the_filter_details()
    {
        var d = File.ReadAllBytes(Path.Combine(Bundle, "Contents/Resources/SmoothPenStrokes.rsrc"));
        int dataOff = BinaryPrimitives.ReadInt32BigEndian(d), mapOff = BinaryPrimitives.ReadInt32BigEndian(d.AsSpan(4));
        int dataLen = BinaryPrimitives.ReadInt32BigEndian(d.AsSpan(8)), mapLen = BinaryPrimitives.ReadInt32BigEndian(d.AsSpan(12));
        Assert.Equal(0x100, dataOff);
        Assert.Equal(d.Length, mapOff + mapLen);
        Assert.Equal(mapOff, dataOff + dataLen);
        Assert.Equal("PiPL", Encoding.ASCII.GetString(d, mapOff + 30, 4));          // the one resource type
        Assert.Equal(16000, BinaryPrimitives.ReadInt16BigEndian(d.AsSpan(mapOff + 38))); // its id

        var pipl = d.AsSpan(dataOff + 4, BinaryPrimitives.ReadInt32BigEndian(d.AsSpan(dataOff)));
        int count = BinaryPrimitives.ReadInt32BigEndian(pipl[4..]);
        var props = new Dictionary<string, byte[]>();
        int at = 8;
        for (int i = 0; i < count; i++)
        {
            Assert.Equal("8BIM", Encoding.ASCII.GetString(pipl.Slice(at, 4)));
            var key = Encoding.ASCII.GetString(pipl.Slice(at + 4, 4));
            int len = BinaryPrimitives.ReadInt32BigEndian(pipl[(at + 12)..]);
            props[key] = pipl.Slice(at + 16, len).ToArray();
            at += 16 + (len + 3) / 4 * 4;
        }
        Assert.Equal(pipl.Length, at);
        Assert.Equal("8BFM", Encoding.ASCII.GetString(props["kind"]));
        Assert.Equal("Smooth pen strokes", Encoding.ASCII.GetString(props["name"], 1, props["name"][0]));
        Assert.Equal("Tablet App for Mac", Encoding.ASCII.GetString(props["catg"], 1, props["catg"][0]));
        Assert.Equal("PluginMain", Encoding.ASCII.GetString(props["ma64"], 1, props["ma64"][0]));
        Assert.Equal("PluginMain", Encoding.ASCII.GetString(props["mi64"], 1, props["mi64"][0]));
    }

    [Fact]
    public void Resource_generator_matches_Rez_for_a_known_plugin()
    {
        // gmic-affinity's GmicFilter.rsrc, made by Apple's Rez, is kept next to the generator; the generator's own
        // check (make_rsrc.py) reproduced it byte for byte. Here: our file uses the same layout and reserved bytes.
        var refFile = File.ReadAllBytes(Path.Combine(Root(), "tools/affinity-plugin/gmic-reference.rsrc"));
        var ours = File.ReadAllBytes(Path.Combine(Bundle, "Contents/Resources/SmoothPenStrokes.rsrc"));
        Assert.Equal(refFile.AsSpan(16, 0x100 - 16).ToArray(), ours.AsSpan(16, 0x100 - 16).ToArray());
        int refMap = BinaryPrimitives.ReadInt32BigEndian(refFile.AsSpan(4)), ourMap = BinaryPrimitives.ReadInt32BigEndian(ours.AsSpan(4));
        Assert.Equal(refFile.AsSpan(refMap + 16, 34).ToArray(), ours.AsSpan(ourMap + 16, 34).ToArray());   // up to the name list
    }

    [Fact]
    public void Binary_is_a_Mac_bundle_for_Apple_silicon_and_Intel_with_PluginMain()
    {
        var b = File.ReadAllBytes(Path.Combine(Bundle, "Contents/MacOS/SmoothPenStrokes"));
        Assert.Equal(0xCAFEBABEu, BinaryPrimitives.ReadUInt32BigEndian(b));   // universal
        int n = BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(4));
        var cpus = new List<uint>();
        for (int i = 0; i < n; i++)
        {
            uint cpu = BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(8 + i * 20));
            int off = BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(8 + i * 20 + 8));
            cpus.Add(cpu);
            Assert.Equal(0xFEEDFACFu, BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(off)));   // 64-bit Mach-O
            Assert.Equal(8u, BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(off + 12)));        // MH_BUNDLE
        }
        Assert.Contains(0x0100000Cu, cpus);   // arm64
        Assert.Contains(0x01000007u, cpus);   // x86_64
        Assert.Contains("_PluginMain", Encoding.ASCII.GetString(b));
    }

    [Fact]
    public void Plugin_source_switches_merging_off_and_leaves_the_picture_alone()
    {
        var c = File.ReadAllText(Path.Combine(Root(), "tools/affinity-plugin/smooth_strokes.c"));
        Assert.Contains("sel_registerName(\"setMouseCoalescingEnabled:\"), false", c);
        Assert.Contains("__attribute__((constructor))", c);
        Assert.Contains("addLocalMonitorForEventsMatchingMask:handler:", c);
        Assert.Contains("*result = selector == 0 ? 0 : -128;", c);
    }

    [Fact]
    public void Install_copies_the_bundle_into_Affinity_3s_folder_executable()
    {
        var p = Make(out var home, out _);
        Assert.False(p.IsInstalled);
        var done = p.Install();
        var folder = Path.Combine(home, "Library/Application Support/Affinity/Plugins");
        Assert.Equal(new[] { folder }, done);
        var bin = Path.Combine(folder, "SmoothPenStrokes.plugin/Contents/MacOS/SmoothPenStrokes");
        Assert.Equal(File.ReadAllBytes(Path.Combine(Bundle, "Contents/MacOS/SmoothPenStrokes")), File.ReadAllBytes(bin));
        Assert.True(File.Exists(Path.Combine(folder, "SmoothPenStrokes.plugin/Contents/Resources/SmoothPenStrokes.rsrc")));
        if (!OperatingSystem.IsWindows())
            Assert.True(File.GetUnixFileMode(bin).HasFlag(UnixFileMode.UserExecute));
        Assert.True(p.IsInstalled);
    }

    [Fact]
    public void Install_again_replaces_and_also_fills_Affinity_Photo_2()
    {
        var p = Make(out var home, out _);
        Directory.CreateDirectory(Path.Combine(home, "Library/Application Support/Affinity Photo 2"));
        var stale = Path.Combine(home, "Library/Application Support/Affinity/Plugins/SmoothPenStrokes.plugin/Contents/old.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(stale)!);
        File.WriteAllText(stale, "old");
        Assert.Equal(2, p.Install().Count);
        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(Path.Combine(home, "Library/Application Support/Affinity Photo 2/Plugins/SmoothPenStrokes.plugin/Contents/Info.plist")));
    }

    [Fact]
    public void Section_shows_only_when_Affinity_is_installed()
    {
        var p = Make(out _, out var apps);
        Assert.False(p.AffinityInstalled);
        Directory.CreateDirectory(Path.Combine(apps, "Affinity.app"));
        Assert.True(p.AffinityInstalled);
        Assert.Equal(new[] { "Affinity.app" }, p.AffinityApps());
    }

    [Fact]
    public void Last_active_comes_from_the_plugins_log()
    {
        var p = Make(out var home, out _);
        Assert.Null(p.LastActive());
        var logs = Path.Combine(home, "Library/Logs");
        Directory.CreateDirectory(logs);
        File.WriteAllText(Path.Combine(logs, AffinityPlugin.LogFileName),
            "1762870000 loaded: point merging was on, now off\n1762870100 run: point merging was off, now off\n");
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1762870100), p.LastActive());
    }

    [Fact]
    public void Settings_section_and_texts_in_every_language()
    {
        var view = File.ReadAllText(Path.Combine(Root(), "OpenTabletArtist/Views/StartupView.axaml"));
        Assert.Contains("x:Name=\"AffinityInstallButton\"", view);
        var dir = Path.Combine(Root(), "OpenTabletArtist/Assets/i18n");
        var en = JObject.Parse(File.ReadAllText(Path.Combine(dir, "en.json")));
        var keys = new[] { "affinity.description", "affinity.install", "affinity.install_again", "affinity.failed", "affinity.steps", "affinity.last_active" };
        foreach (var f in Directory.GetFiles(dir, "*.json"))
        {
            var j = JObject.Parse(File.ReadAllText(f));
            Assert.False(string.IsNullOrWhiteSpace((string?)j["affinity.section"]));
            foreach (var k in keys)
            {
                Assert.False(string.IsNullOrWhiteSpace((string?)j[k]), $"{Path.GetFileName(f)} misses {k}");
                if (!f.EndsWith("en.json")) Assert.NotEqual((string?)en[k], (string?)j[k]);
            }
            Assert.Contains("{error}", (string)j["affinity.failed"]!);
            Assert.Contains("{time}", (string)j["affinity.last_active"]!);
        }
    }
}
