using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Version 29 (#157, Digital Groceries 20 Nov 08:41): the app icon showed coloured noise in Finder's list view
/// and in Force Quit while the big preview was fine. The icon file kept its 16 px and 32 px pictures as PNG
/// in the icp4/icp5/icp6 slots, which macOS does not decode. Apple's iconutil stores those two sizes as
/// ic04/ic05 "ARGB" (tools/make_icns.py writes them that way). These tests read the shipped icon files.</summary>
public class IcnsSmallSizesTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private static readonly string AppIcon = Path.Combine("OpenTabletArtist", "Assets", "macos", "AppIcon.icns");
    private static readonly string DocIcon = Path.Combine("packaging", "macos", "SettingsDocument.icns");

    private static Dictionary<string, byte[]> Chunks(string relative)
    {
        var d = File.ReadAllBytes(Path.Combine(RepoRoot(), relative));
        Assert.Equal("icns", Encoding.ASCII.GetString(d, 0, 4));
        Assert.Equal(d.Length, BigEndian(d, 4));
        var chunks = new Dictionary<string, byte[]>();
        for (var i = 8; i < d.Length;)
        {
            var type = Encoding.ASCII.GetString(d, i, 4);
            var n = BigEndian(d, i + 4);
            Assert.True(n >= 8 && i + n <= d.Length, $"{relative}: {type} runs past the end of the file");
            chunks[type] = d[(i + 8)..(i + n)];
            i += n;
        }
        return chunks;
    }

    private static int BigEndian(byte[] d, int at) => (d[at] << 24) | (d[at + 1] << 16) | (d[at + 2] << 8) | d[at + 3];

    private static bool IsPng(byte[] b) => b.Length >= 8 && b[0] == 0x89 && b[1] == (byte)'P' && b[2] == (byte)'N' && b[3] == (byte)'G';

    /// <summary>The icns run-length scheme: a control byte of 0x80 or more repeats the next byte (control − 125)
    /// times; below 0x80 it copies (control + 1) bytes as they are.</summary>
    private static byte[] UnpackPlane(byte[] data, ref int pos, int count)
    {
        var o = new List<byte>(count);
        while (o.Count < count)
        {
            Assert.True(pos < data.Length, "ARGB data ends inside a plane");
            int c = data[pos++];
            if (c >= 0x80) { o.AddRange(Enumerable.Repeat(data[pos++], c - 125)); }
            else { o.AddRange(data.AsSpan(pos, c + 1).ToArray()); pos += c + 1; }
        }
        Assert.Equal(count, o.Count);
        return o.ToArray();
    }

    [Theory]
    [InlineData("AppIcon")]
    [InlineData("SettingsDocument")]
    public void NoPngIsKeptInTheSlotsMacOsCannotRead(string which)
    {
        var chunks = Chunks(which == "AppIcon" ? AppIcon : DocIcon);
        foreach (var slot in new[] { "icp4", "icp5", "icp6" })
            Assert.False(chunks.TryGetValue(slot, out var b) && IsPng(b),
                $"{which}.icns keeps a PNG in {slot}: Finder's list view shows it as coloured noise (#157)");
    }

    [Theory]
    [InlineData("ic04", 16)]
    [InlineData("ic05", 32)]
    public void TheSmallAppIconSizesAreArgbLikeApplesIconutilWritesThem(string slot, int size)
    {
        var chunks = Chunks(AppIcon);
        Assert.True(chunks.TryGetValue(slot, out var b), $"AppIcon.icns has no {slot} ({size} px)");
        Assert.Equal("ARGB", Encoding.ASCII.GetString(b!, 0, 4));
        var pos = 4;
        var alpha = UnpackPlane(b!, ref pos, size * size);
        for (var plane = 0; plane < 3; plane++) UnpackPlane(b!, ref pos, size * size);
        Assert.Equal(b!.Length, pos);                    // exactly four planes, nothing left over
        Assert.Contains(alpha, a => a > 0);              // there is a picture, not an empty square
        Assert.Contains(alpha, a => a == 0);             // with see-through corners like the big sizes
    }

    [Fact]
    public void EveryOtherSizeIsStillPresentAsPng()
    {
        var chunks = Chunks(AppIcon);
        foreach (var slot in new[] { "ic07", "ic08", "ic09", "ic10", "ic11", "ic12", "ic13", "ic14" })
            Assert.True(chunks.TryGetValue(slot, out var b) && IsPng(b), $"AppIcon.icns: {slot} missing or not PNG");
    }
}
