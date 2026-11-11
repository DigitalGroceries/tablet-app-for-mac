using System;
using System.Collections.Generic;
using System.Linq;
using OpenTabletDriver.Desktop.Reflection;

namespace OpenTabletArtist.Domain;

/// <summary>
/// #7 / #18 (Brice, PTK-640): the small screens beside the Intuos4 ExpressKeys (M, L, XL and the wireless
/// model when wired; the Small PTK-440 has none). Each screen is 64×32 pixels, 16 grey levels.
/// <para>
/// Same USB feature reports as Linux <c>wacom_led_putimage()</c> (drivers/hid/wacom_sys.c): report
/// <c>0x21 [0x21, 1]</c> to start, four <c>0x23 [0x23, key, chunk, 256 bytes]</c> with the 1024-byte image,
/// then <c>0x21 [0x21, 0]</c> to stop. The image goes in the tablet's own interleaved order; <see cref="Scramble"/>
/// is a port of the <c>wacom_scramble()</c> / gnome-settings-daemon <c>oled_scramble_icon()</c> encoder. The
/// plain image before that is two pixels a byte, the left pixel in the high half, 0,0 at the top left.
/// </para>
/// <para>Not tested on real hardware here (Zak's PTK-440 has no screens); Brice tests it.</para>
/// </summary>
public static class Intuos4Oled
{
    public const int Width = 64;
    public const int Height = 32;
    public const int ImageBytes = Width * Height / 2;   // 1024
    public const int KeyCount = 8;
    public const byte StartStopReport = 0x21;
    public const byte TransferReport = 0x23;
    public const int ChunkBytes = ImageBytes / 4;        // 256
    public const int TransferReportLength = ChunkBytes + 3;   // 259

    /// <summary>Intuos4 models with key screens: PTK-640, PTK-840, PTK-1240, PTK-540WL (wired).</summary>
    public static readonly int[] OledProductIds = { 0xB9, 0xBA, 0xBB, 0xBC };

    /// <summary>The OpenTabletDriver names of those models, in the order of <see cref="OledProductIds"/>.</summary>
    public static readonly string[] OledNames =
        { "Wacom PTK-640", "Wacom PTK-840", "Wacom PTK-1240", "Wacom PTK-540WL" };

    public static bool HasOleds(int vendorId, int productId) =>
        vendorId == 0x056A && OledProductIds.Contains(productId);

    public static string? NameOf(int productId)
    {
        var i = Array.IndexOf(OledProductIds, productId);
        return i < 0 ? null : OledNames[i];
    }

    public static bool AnyOledTablet(IEnumerable<string> connectedTabletNames) =>
        connectedTabletNames.Any(n => OledNames.Contains(n, StringComparer.OrdinalIgnoreCase));

    /// <summary>A black screen (nothing shown).</summary>
    public static byte[] Blank() => new byte[ImageBytes];

    /// <summary>64×32 grey levels (0–255, row by row) to the plain 1024-byte image: two pixels a byte,
    /// the left pixel in the high half.</summary>
    public static byte[] Pack(ReadOnlySpan<byte> grey)
    {
        if (grey.Length != Width * Height) throw new ArgumentException("Need 64×32 pixels.", nameof(grey));
        var packed = new byte[ImageBytes];
        for (int i = 0; i < ImageBytes; i++)
            packed[i] = (byte)((grey[2 * i] & 0xF0) | (grey[2 * i + 1] >> 4));
        return packed;
    }

    /// <summary>Turned upside down, for a tablet used left-handed (the keys are then on the right).</summary>
    public static byte[] Rotate180(byte[] packed)
    {
        var o = new byte[ImageBytes];
        for (int i = 0; i < ImageBytes; i++)
        {
            var b = packed[ImageBytes - 1 - i];
            o[i] = (byte)((b << 4) | (b >> 4));
        }
        return o;
    }

    /// <summary>The plain image in the tablet's interleaved order (port of the kernel's <c>wacom_scramble()</c>).</summary>
    public static byte[] Scramble(byte[] packed)
    {
        if (packed.Length != ImageBytes) throw new ArgumentException("Need a 1024-byte image.", nameof(packed));
        var o = new byte[ImageBytes];
        for (int y = 0; y < Height / 2; y++)
        {
            for (int x = 0; x < Width / 2; x++)
            {
                int i = 2 * x + Width * y;
                byte a = packed[31 - x + Width * y];
                byte b = packed[63 - x + Width * y];
                o[i] = (byte)((b << 4) | (a & 0x0F));
                o[i + 1] = (byte)((b & 0xF0) | (a >> 4));
            }
        }
        return o;
    }

    /// <summary>Start, the four image chunks for one key (0–7), stop.</summary>
    public static byte[][] BuildReports(int key, byte[] packed)
    {
        if (key is < 0 or >= KeyCount) throw new ArgumentOutOfRangeException(nameof(key));
        var raw = Scramble(packed);
        var reports = new byte[6][];
        reports[0] = new byte[] { StartStopReport, 1 };
        for (int chunk = 0; chunk < 4; chunk++)
        {
            var r = new byte[TransferReportLength];
            r[0] = TransferReport;
            r[1] = (byte)(key & 0x07);
            r[2] = (byte)chunk;
            Array.Copy(raw, chunk * ChunkBytes, r, 3, ChunkBytes);
            reports[chunk + 1] = r;
        }
        reports[5] = new byte[] { StartStopReport, 0 };
        return reports;
    }

    /// <summary>
    /// The words to show for one key's binding. A keystroke shows its modifiers on the first line and the
    /// key on the second ("Shift Cmd" / "Z"); a mouse or scroll action shows its name ("Right click"); a key that
    /// does nothing shows nothing. <paramref name="otherLabel"/> is used for a binding the editor can't model.
    /// </summary>
    public static IReadOnlyList<string> LabelLines(AuxBinding? binding, string? otherLabel = null)
    {
        if (binding == null)
            return string.IsNullOrWhiteSpace(otherLabel) ? Array.Empty<string>() : new[] { otherLabel.Trim() };
        if (!binding.IsBound) return Array.Empty<string>();
        if (binding.Kind == AuxKind.Keyboard)
        {
            var c = binding.Combo;
            // Apple's order, as Mac menus show it: Control, Option, Shift, Command.
            var mods = new List<string>();
            if (c.Ctrl) mods.Add("Ctrl");
            if (c.Alt) mods.Add(AuxKeyBinding.AltLabel);
            if (c.Shift) mods.Add("Shift");
            if (c.Cmd) mods.Add(AuxKeyBinding.CmdLabel);
            var key = AuxKeyBinding.Describe(binding with { Combo = new AuxCombo(false, false, false, c.Key) });
            return mods.Count == 0 ? new[] { key } : new[] { string.Join(" ", mods), key };
        }
        return new[] { AuxKeyBinding.Describe(binding) };
    }

    /// <summary>The label lines for all eight keys of a profile's ExpressKeys. Missing keys show nothing.</summary>
    public static IReadOnlyList<IReadOnlyList<string>> KeyLabels(IList<PluginSettingStore?>? auxButtons,
        Func<PluginSettingStore?, string>? otherName = null)
    {
        var all = new List<IReadOnlyList<string>>(KeyCount);
        for (int k = 0; k < KeyCount; k++)
        {
            var store = auxButtons != null && k < auxButtons.Count ? auxButtons[k] : null;
            var binding = AuxKeyBinding.ReadBinding(store);
            all.Add(LabelLines(binding, binding == null ? (otherName?.Invoke(store) ?? PenSwitchBinding.GetBindingLabel(store)) : null));
        }
        return all;
    }

    /// <summary>True when the tablet area is turned upside down (left-handed use).</summary>
    public static bool IsUpsideDown(float rotationDegrees)
    {
        var r = ((rotationDegrees % 360) + 360) % 360;
        return r is > 135 and < 225;
    }
}
