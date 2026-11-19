using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenTabletArtist.Domain;

// Shared by the app and the driver plugin (source-linked into OpenTabletArtist.Dynamics), so both read the
// same menu the same way. System.Text.Json only: the plugin can't count on Newtonsoft being loaded.

/// <summary>What a radial menu slice does when it is tapped (TASKS #42).</summary>
public static class RadialActions
{
    public const string None = "None";        // empty slice: tapping it closes the menu (as Wacom's)
    public const string Keys = "Keys";        // a keystroke, OTD multi-key string ("Application+Z" = ⌘Z)
    public const string Click = "Click";      // Left / Right / Middle / DoubleClick at the pointer
    public const string Submenu = "Submenu";  // opens another ring of 8 slices
    public const string Open = "Open";        // opens an app (bundle id or .app path)
    /// <summary>V29 #141 (Zak 19 Nov): shows or hides the floating keyboard, so the pen can type.</summary>
    public const string Keyboard = "Keyboard";

    public static readonly string[] All = { None, Keys, Click, Submenu, Open, Keyboard };
}

/// <summary>One menu ring: always <see cref="SliceCount"/> slices, slice 0 at the top, then clockwise.</summary>
public sealed class RadialMenuDef
{
    public const int SliceCount = 8;
    public const int MinSize = 1, DefaultSize = 50;
    /// <summary>TASKS #63 (Zak 21:01): the menu is never bigger than the one on his screen, which is size 50: 320 pt across
    /// (<see cref="RadialMenuGeometry.OuterRadius"/> 160). The slider's largest value is this; a saved larger size is used as this.</summary>
    public const int MaxSize = 50;
    /// <summary>v27 test (Zak 13 Nov 16:07): the user sets the menu size from 1 to 10. Step 1 is the smallest menu (size 1) and
    /// step 10 the largest (size 50); the steps in between are spread evenly over the same sizes. Menus are still saved with
    /// the size (1-50) the driver uses, so nothing saved before changes.</summary>
    public const int MinSizeStep = 1, MaxSizeStep = 10;
    /// <summary>The menu size for a step 1-10 (rounded to a whole size).</summary>
    public static int SizeForStep(int step)
    {
        int st = Math.Clamp(step, MinSizeStep, MaxSizeStep);
        return (int)Math.Round(MinSize + (st - MinSizeStep) * (double)(MaxSize - MinSize) / (MaxSizeStep - MinSizeStep), MidpointRounding.AwayFromZero);
    }
    /// <summary>The step 1-10 a menu size is shown as (the nearest one).</summary>
    public static int StepForSize(int size)
    {
        int sz = Math.Clamp(size, MinSize, MaxSize);
        return (int)Math.Round(MinSizeStep + (sz - MinSize) * (double)(MaxSizeStep - MinSizeStep) / (MaxSize - MinSize), MidpointRounding.AwayFromZero);
    }
    /// <summary>The widest a menu can be, in points (the picture in the Pen page's Radial Menu Settings is never larger).</summary>
    public const double MaxDiameter = 320;
    /// <summary>Menu transparency in percent: 0 is solid. TASKS #58 / #59 (Zak 18:21): 40 unless chosen (was 70).
    /// v27 test (Zak 13 Nov 16:01, looking at the open menu over a white web page): "60% should be the maximum" - above that
    /// the slice names can't be read, so the slider, the number box and every saved menu stop at 60.</summary>
    public const int MinTransparency = 0, MaxTransparency = 60, DefaultTransparency = 40;
    /// <summary>The percent scale the fading is measured on (unchanged by the 60% limit, so 60% looks exactly as Zak saw it).</summary>
    public const int TransparencyScale = 100;
    /// <summary>Opacity left at <see cref="TransparencyScale"/> (100%) on the fading scale: 10%. Since the v27 test the
    /// slider stops at <see cref="MaxTransparency"/> (60%), which leaves 46% of the menu visible.</summary>
    public const double MinVisibleOpacity = 0.10;
    /// <summary>Opacity of the whole menu (slices, labels, icons, ring and highlight) for a transparency 0–100.</summary>
    public static double OpacityFor(int transparency)
    {
        int t = Math.Clamp(transparency, MinTransparency, MaxTransparency);
        // Weighted from fully solid (0%) to MinVisibleOpacity (100% on the scale); the limit stops it at 60%.
        return MinVisibleOpacity + (1.0 - MinVisibleOpacity) * (TransparencyScale - t) / TransparencyScale;
    }
    /// <summary>Top menu + 2 levels of submenus.</summary>
    public const int MaxDepth = 3;

    [JsonPropertyName("slices")] public List<RadialSlice> Slices { get; set; } = new();

    /// <summary>On-screen size 1–100 (same scale as the app's other sliders). Only the top menu's value is used.</summary>
    [JsonPropertyName("size")] public int Size { get; set; } = DefaultSize;

    /// <summary>How see-through the whole menu is, 0 (solid) to 100 (clear, still <see cref="MinVisibleOpacity"/> so it stays usable).
    /// Only the top menu's value is used; a menu saved before this existed reads as <see cref="DefaultTransparency"/>.</summary>
    [JsonPropertyName("transparency")] public int Transparency { get; set; } = DefaultTransparency;

    /// <summary>V29 #142: the colour palette's own size (same 1-50 scale and step slider as the menu); set on the Eraser page.
    /// The palette is always solid, so an eyedropper reads its colours exactly. Only the top menu's value is used.</summary>
    [JsonPropertyName("paletteSize")] public int PaletteSize { get; set; } = DefaultSize;

    /// <summary>V29 #142 (Zak ~20:05: the pencil changes a swatch to your own colour): swatch number to "#RRGGBB", only for the
    /// swatches the user changed; the circular arrow empties it. Only the top menu's value is used.</summary>
    [JsonPropertyName("paletteColours")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? PaletteColours { get; set; }
}

public sealed class RadialSlice
{
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("action")] public string Action { get; set; } = RadialActions.None;
    [JsonPropertyName("keys")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Keys { get; set; }
    [JsonPropertyName("click")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Click { get; set; }
    [JsonPropertyName("app")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? App { get; set; }
    [JsonPropertyName("submenu")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public RadialMenuDef? Submenu { get; set; }

    /// <summary>True when tapping it does something (an empty slice just closes the menu).</summary>
    [JsonIgnore]
    public bool IsSet => Action switch
    {
        RadialActions.Keys => !string.IsNullOrWhiteSpace(Keys),
        RadialActions.Click => !string.IsNullOrWhiteSpace(Click),
        RadialActions.Open => !string.IsNullOrWhiteSpace(App),
        RadialActions.Submenu => Submenu != null,
        RadialActions.Keyboard => true,
        _ => false,
    };

    public RadialSlice Clone() => new()
    {
        Label = Label, Action = Action, Keys = Keys, Click = Click, App = App, Submenu = Submenu?.Clone(),
    };
}

public static class RadialMenuDefExtensions
{
    public static RadialMenuDef Clone(this RadialMenuDef def) => new()
    {
        Size = def.Size,
        Transparency = def.Transparency,
        PaletteSize = def.PaletteSize,
        PaletteColours = def.PaletteColours == null ? null : new Dictionary<string, string>(def.PaletteColours),
        Slices = def.Slices.Select(s => s.Clone()).ToList(),
    };
}

/// <summary>Reading and writing a menu as the JSON string kept in the binding's "Menu" setting.</summary>
public static class RadialMenuJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
    };

    public static string Serialize(RadialMenuDef def) => JsonSerializer.Serialize(Normalize(def), Options);

    /// <summary>The menu in <paramref name="json"/>, tidied (always 8 slices, known actions, size 1–100, at most
    /// <see cref="RadialMenuDef.MaxDepth"/> levels). Empty or broken JSON gives the default menu. Never throws.</summary>
    public static RadialMenuDef Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return RadialMenuDefaults.Create();
        try
        {
            var def = JsonSerializer.Deserialize<RadialMenuDef>(json, Options);
            return def == null ? RadialMenuDefaults.Create() : Normalize(def);
        }
        catch (Exception)
        {
            return RadialMenuDefaults.Create();
        }
    }

    public static RadialMenuDef Normalize(RadialMenuDef def) => Normalize(def, 1);

    private static RadialMenuDef Normalize(RadialMenuDef? def, int depth)
    {
        var src = def?.Slices ?? new List<RadialSlice>();
        var slices = new List<RadialSlice>(RadialMenuDef.SliceCount);
        for (int i = 0; i < RadialMenuDef.SliceCount; i++)
        {
            var s = i < src.Count && src[i] != null ? src[i] : new RadialSlice();
            var action = RadialActions.All.FirstOrDefault(a => string.Equals(a, s.Action, StringComparison.OrdinalIgnoreCase))
                         ?? RadialActions.None;
            if (action == RadialActions.Submenu && depth >= RadialMenuDef.MaxDepth) action = RadialActions.None;
            slices.Add(new RadialSlice
            {
                Label = (s.Label ?? "").Trim(),
                Action = action,
                Keys = action == RadialActions.Keys ? s.Keys?.Trim() : null,
                Click = action == RadialActions.Click ? s.Click?.Trim() : null,
                App = action == RadialActions.Open ? s.App?.Trim() : null,
                Submenu = action == RadialActions.Submenu ? Normalize(s.Submenu ?? new RadialMenuDef(), depth + 1) : null,
            });
        }
        return new RadialMenuDef
        {
            Slices = slices,
            Size = ClampSize(def?.Size ?? RadialMenuDef.DefaultSize),
            Transparency = Math.Clamp(def?.Transparency ?? RadialMenuDef.DefaultTransparency, RadialMenuDef.MinTransparency, RadialMenuDef.MaxTransparency),
            PaletteSize = ClampSize(def?.PaletteSize ?? RadialMenuDef.DefaultSize),
            PaletteColours = ColourPalette.CleanColours(def?.PaletteColours),
        };
    }

    private static readonly System.Collections.Generic.HashSet<int> _loggedLargeSizes = new();

    /// <summary>Where the "saved size is too large" line goes (the app points it at its log; this file is shared with plugins that have none).</summary>
    public static Action<string>? Log { get; set; }

    /// <summary>A menu size within <see cref="RadialMenuDef.MinSize"/>..<see cref="RadialMenuDef.MaxSize"/>. A saved size above the
    /// largest (older versions allowed up to 100) is brought down to it; each such value is logged once per run.</summary>
    internal static int ClampSize(int size)
    {
        if (size > RadialMenuDef.MaxSize)
        {
            bool first; lock (_loggedLargeSizes) first = _loggedLargeSizes.Add(size);
            if (first)
                Log?.Invoke($"Radial menu: a saved menu size of {size} is larger than the largest allowed ({RadialMenuDef.MaxSize}, " +
                                     $"{RadialMenuDef.MaxDiameter:0} pt across), so it is used as {RadialMenuDef.MaxSize}; the saved value is corrected the next time the menu is saved.");
        }
        return Math.Clamp(size, RadialMenuDef.MinSize, RadialMenuDef.MaxSize);
    }

    /// <summary>The ring reached by following <paramref name="path"/> (slice numbers of submenus) from the top,
    /// or null when the path doesn't lead to a submenu.</summary>
    public static RadialMenuDef? Level(RadialMenuDef top, IReadOnlyList<int> path)
    {
        var cur = top;
        foreach (var i in path)
        {
            if (i < 0 || i >= cur.Slices.Count) return null;
            var next = cur.Slices[i].Submenu;
            if (cur.Slices[i].Action != RadialActions.Submenu || next == null) return null;
            cur = next;
        }
        return cur;
    }
}

/// <summary>The menu a new "Radial menu" binding starts with: shortcuts that work in most Mac apps.</summary>
public static class RadialMenuDefaults
{
    /// <summary>The default menu. <paramref name="te"/> (key, English) translates the slice names; the plugin
    /// passes nothing and gets English (it only needs this when a saved menu can't be read).</summary>
    public static RadialMenuDef Create(Func<string, string, string>? te = null)
    {
        string T(string key, string en) => te?.Invoke("radial.default." + key, en) ?? en;
        return new RadialMenuDef
        {
            Size = RadialMenuDef.DefaultSize,
            Transparency = RadialMenuDef.DefaultTransparency,
            Slices = new List<RadialSlice>
            {
                Keys(T("undo", "Undo"), "Application+Z"),          // top
                Keys(T("redo", "Redo"), "Application+Shift+Z"),
                Keys(T("copy", "Copy"), "Application+C"),
                Keys(T("paste", "Paste"), "Application+V"),
                Keys(T("cut", "Cut"), "Application+X"),
                Keys(T("select_all", "Select all"), "Application+A"),
                Keys(T("zoom_out", "Zoom out"), "Application+Minus"),
                Keys(T("zoom_in", "Zoom in"), "Application+Equal"),
            },
        };
    }

    private static RadialSlice Keys(string label, string keys) =>
        new() { Label = label, Action = RadialActions.Keys, Keys = keys };
}

/// <summary>Where things are on screen. Points, y downwards (macOS global coordinates, as CGEventGetLocation).</summary>
public static class RadialMenuGeometry
{
    // The centre circle is four quarter buttons (TASKS #54), split by a cross:
    //   top left: the X (close)     top right: the pencil (edit a slice)
    //   bottom left: restore the default slices     bottom right: the open hand (drag the menu)
    /// <summary>Top-left quarter, the X: close.</summary>
    public const int Centre = -1;
    /// <summary>Bottom-right quarter, the open hand: press it and move to drag the menu; a press that doesn't move does nothing.</summary>
    public const int Hand = -4;
    /// <summary>Top-right quarter, the pencil: then tap a slice to choose what it does.</summary>
    public const int Edit = -5;
    /// <summary>Bottom-left quarter, the circular arrow: restore the 8 default slices (after a Yes / No).</summary>
    public const int Restore = -6;
    /// <summary>Tapped outside the ring: close.</summary>
    public const int Outside = -2;

    /// <summary>Outer radius in points for size 1–50: 50 (the largest) gives 160 pt (a 320 pt menu), 1 gives about 91. A larger
    /// number is treated as 50, so no menu is bigger than 320 pt across (TASKS #63).</summary>
    public static double OuterRadius(int size) =>
        90 + 1.4 * Math.Clamp(size, RadialMenuDef.MinSize, RadialMenuDef.MaxSize);

    /// <summary>The centre circle's radius as a share of the outer radius (it holds four quarter buttons, so each
    /// is a comfortable target for the pen).</summary>
    public const double InnerRatio = 0.40;

    /// <summary>Radius of the centre circle: the X on top, the hand below.</summary>
    public static double InnerRadius(int size) => OuterRadius(size) * InnerRatio;

    /// <summary>The slice at offset (<paramref name="dx"/>, <paramref name="dy"/>) from the centre: 0 = top, then
    /// clockwise; one of the four centre quarters (<see cref="Centre"/>, <see cref="Edit"/>, <see cref="Restore"/>, <see cref="Hand"/>) inside the centre circle; <see cref="Outside"/> beyond the ring.</summary>
    public static int SliceAt(double dx, double dy, int size)
    {
        var r = Math.Sqrt(dx * dx + dy * dy);
        if (r <= InnerRadius(size))
        {
            // The dividing lines belong to the top-left (X) quarter's side: x <= 0 and y <= 0 count as left / top.
            bool left = dx <= 0, top = dy <= 0;
            return top ? (left ? Centre : Edit) : (left ? Restore : Hand);
        }
        if (r > OuterRadius(size)) return Outside;
        // Angle from straight up, clockwise (screen y grows downwards).
        var deg = Math.Atan2(dx, -dy) * 180.0 / Math.PI;
        if (deg < 0) deg += 360;
        var step = 360.0 / RadialMenuDef.SliceCount;
        return (int)Math.Floor((deg + step / 2) / step) % RadialMenuDef.SliceCount;
    }

    /// <summary>Middle of slice <paramref name="i"/>'s arc, in degrees clockwise from the top.</summary>
    public static double SliceAngle(int i) => i * 360.0 / RadialMenuDef.SliceCount;
}

/// <summary>What the app tells the driver when the menu is used with the Mac mouse (TASKS #54). The app can't
/// press keys in other apps (the driver can), so a click is written to a small file the plugin polls while the
/// menu is open; same folder as the state file. <see cref="Seq"/> only ever grows, so a command is acted on once.</summary>
public sealed class RadialMenuCommand
{
    /// <summary>Run slice <see cref="Slice"/> of the open ring (a submenu slice opens it); the menu stays open.</summary>
    public const string Tap = "tap";
    /// <summary>The X (top-left centre quarter) was clicked: close.</summary>
    public const string Centre = "centre";
    /// <summary>Esc: close.</summary>
    public const string Close = "close";
    /// <summary>The menu was dragged: its centre is now (<see cref="X"/>, <see cref="Y"/>). It is then "parked": it stays until closed.</summary>
    public const string Move = "move";
    /// <summary>The pencil was clicked: the next slice tapped is edited instead of run.</summary>
    public const string Edit = "edit";
    /// <summary>The circular arrow was clicked: ask whether to restore the default slices.</summary>
    public const string Restore = "restore";
    /// <summary>The app finished an edit or a restore: <see cref="Menu"/> is the menu to use from now on (the same one when nothing changed).</summary>
    public const string MenuChanged = "menu";
    /// <summary>V29 #141: a key on the floating keyboard was tapped: press <see cref="Keys"/> (OTD multi-key string) in the app in front.
    /// Written to <see cref="FloatingKeyboardFiles.CommandFile"/>, not to the menu's command file.</summary>
    public const string Key = "key";

    [JsonPropertyName("seq")] public long Seq { get; set; }
    [JsonPropertyName("op")] public string Op { get; set; } = "";
    [JsonPropertyName("slice")] public int Slice { get; set; } = -1;
    [JsonPropertyName("x")] public double X { get; set; }
    [JsonPropertyName("y")] public double Y { get; set; }
    [JsonPropertyName("menu")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Menu { get; set; }
    [JsonPropertyName("keys")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Keys { get; set; }

    public string ToJson() => JsonSerializer.Serialize(this);

    /// <summary>The command in <paramref name="json"/>, or null when it can't be read. Never throws.</summary>
    public static RadialMenuCommand? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<RadialMenuCommand>(json); }
        catch (Exception) { return null; }
    }
}

/// <summary>V29 #141 (Zak 19 Nov): the floating keyboard the radial menu's Keyboard slice shows. The app draws it; the driver presses
/// the keys (the app can't press keys in other apps). They talk through two small files next to the menu's files: the app keeps
/// <see cref="OpenFlag"/> while the keyboard is on screen, and writes each tapped key to <see cref="CommandFile"/>.</summary>
public static class FloatingKeyboardFiles
{
    public static string Folder => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OpenTabletArtist");
    /// <summary>There while the keyboard is on screen; the driver reads <see cref="CommandFile"/> only then.</summary>
    public static string OpenFlag => System.IO.Path.Combine(Folder, "keyboard-open");
    /// <summary>The last few tapped keys, one <see cref="RadialMenuCommand"/> (op "key") per line, oldest first. Keeping a few
    /// means two quick taps are never lost between two looks by the driver.</summary>
    public static string CommandFile => System.IO.Path.Combine(Folder, "keyboard-cmd");
    /// <summary>How many recent keys the file keeps.</summary>
    public const int KeepLast = 20;

    /// <summary>The key commands in the file's text, oldest first; unreadable lines are skipped. Never throws.</summary>
    public static List<RadialMenuCommand> ParseLines(string? text)
    {
        var list = new List<RadialMenuCommand>();
        if (string.IsNullOrEmpty(text)) return list;
        foreach (var line in text.Split('\n'))
            if (RadialMenuCommand.Parse(line.Trim()) is { Op: RadialMenuCommand.Key } c) list.Add(c);
        return list;
    }
}

/// <summary>
/// V29 #142 (Zak 19 Nov 18:50-19:35): the colour palette, a second radial menu "like a painter uses, where different shades of the
/// paint are already mixed and ready to choose from", drawn as an artist's colour wheel: 12 hues round the edge, each with 4 ready
/// mixed shades towards the centre. The same pen button opens it with a quick double press. Shared by the app (drawing, the mouse)
/// and the driver plugin (pen taps), so both find the same swatch at the same place.
/// </summary>
public static class ColourPalette
{
    public const int Hues = 12, Shades = 4;

    /// <summary>The artist's 12-hue wheel, clockwise from yellow at the top.</summary>
    public static readonly string[] HueKeys =
        { "yellow", "yellow_orange", "orange", "red_orange", "red", "red_violet", "violet", "blue_violet", "blue", "blue_green", "green", "yellow_green" };
    public static readonly string[] HueNames =
        { "Yellow", "Yellow-orange", "Orange", "Red-orange", "Red", "Red-violet", "Violet", "Blue-violet", "Blue", "Blue-green", "Green", "Yellow-green" };

    private static readonly (byte R, byte G, byte B)[] Pure =
    {
        (0xF4, 0xE5, 0x00), (0xFD, 0xC6, 0x0B), (0xF1, 0x8E, 0x1C), (0xEA, 0x62, 0x1F), (0xE3, 0x23, 0x22), (0xC4, 0x03, 0x7D),
        (0x6D, 0x39, 0x8B), (0x44, 0x4E, 0x99), (0x2A, 0x71, 0xB0), (0x06, 0x96, 0xBB), (0x00, 0x8E, 0x5B), (0x8C, 0xBB, 0x26),
    };

    /// <summary>The 4 rings from the outer edge inwards: the pure colour, a tint (white mixed in), a tone (grey mixed in) and a
    /// shade (black mixed in), as a painter mixes them.</summary>
    public static readonly string[] ShadeKeys = { "pure", "tint", "tone", "shade" };
    public static readonly string[] ShadeNames = { "pure", "light", "muted", "dark" };

    public static (byte R, byte G, byte B) Colour(int hue, int shade)
    {
        var (r, g, b) = Pure[((hue % Hues) + Hues) % Hues];
        static byte Mix(byte c, byte with, double amount) => (byte)Math.Round(c + (with - c) * amount);
        return Math.Clamp(shade, 0, Shades - 1) switch
        {
            1 => (Mix(r, 255, 0.45), Mix(g, 255, 0.45), Mix(b, 255, 0.45)),
            2 => (Mix(r, 128, 0.40), Mix(g, 128, 0.40), Mix(b, 128, 0.40)),
            3 => (Mix(r, 0, 0.45), Mix(g, 0, 0.45), Mix(b, 0, 0.45)),
            _ => (r, g, b),
        };
    }

    /// <summary>"#RRGGBB" for a swatch.</summary>
    public static string Hex(int hue, int shade)
    {
        var (r, g, b) = Colour(hue, shade);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    public static int Swatch(int hue, int shade) => hue * Shades + shade;
    public static (int Hue, int Shade) Split(int swatch) => (swatch / Shades, swatch % Shades);

    /// <summary>Zak ~20:05: a ring of greys, white to black, round the outside edge (12 steps, white at the top, clockwise).
    /// Swatch numbers 48 to 59.</summary>
    public const int Greys = 12;
    public const int FirstGrey = Hues * Shades;
    public const int Count = FirstGrey + Greys;
    /// <summary>The grey ring's width as a share of the coloured part of the ring.</summary>
    public const double GreyBand = 0.16;

    public static bool IsGrey(int swatch) => swatch >= FirstGrey && swatch < Count;

    /// <summary>The built-in colour of any swatch (0 to 59).</summary>
    public static (byte R, byte G, byte B) Colour(int swatch)
    {
        if (IsGrey(swatch))
        {
            var v = (byte)Math.Round(255.0 * (Greys - 1 - (swatch - FirstGrey)) / (Greys - 1));
            return (v, v, v);
        }
        var (h, s) = Split(Math.Clamp(swatch, 0, FirstGrey - 1));
        return Colour(h, s);
    }

    /// <summary>"#RRGGBB" of a swatch: the user's own colour when the pencil changed it, else the built-in one.</summary>
    public static string HexFor(RadialMenuDef? def, int swatch)
    {
        if (def?.PaletteColours != null && def.PaletteColours.TryGetValue(swatch.ToString(System.Globalization.CultureInfo.InvariantCulture), out var own)
            && IsHex(own)) return own.ToUpperInvariant();
        var (r, g, b) = Colour(swatch);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    /// <summary>The swatch's name for the log and the bubble: "Red, light" or "Grey 4 of 12".</summary>
    public static string NameOf(int swatch)
    {
        if (IsGrey(swatch)) return $"Grey {swatch - FirstGrey + 1} of {Greys}";
        var (h, s) = Split(swatch);
        return HueNames[h] + ", " + ShadeNames[s];
    }

    public static bool IsHex(string? s) => s != null && s.Length == 7 && s[0] == '#'
        && s.Skip(1).All(Uri.IsHexDigit);

    /// <summary>Only valid swatch numbers and colours are kept; null when nothing is left.</summary>
    public static Dictionary<string, string>? CleanColours(Dictionary<string, string>? colours)
    {
        if (colours == null) return null;
        var clean = new Dictionary<string, string>();
        foreach (var (k, v) in colours)
            if (int.TryParse(k, out var n) && n >= 0 && n < Count && IsHex(v)) clean[n.ToString(System.Globalization.CultureInfo.InvariantCulture)] = v.ToUpperInvariant();
        return clean.Count == 0 ? null : clean;
    }

    /// <summary>What is at offset (<paramref name="dx"/>, <paramref name="dy"/>) from the centre of a palette of this size: a swatch
    /// number (hue * 4 + shade, 0 to 47), one of the centre quarters (<see cref="RadialMenuGeometry.Centre"/> = the X,
    /// <see cref="RadialMenuGeometry.Hand"/> = drag, the other two do nothing), or <see cref="RadialMenuGeometry.Outside"/>.
    /// The palette is the same size and shape as the radial menu.</summary>
    public static int SwatchAt(double dx, double dy, int size)
    {
        double r = Math.Sqrt(dx * dx + dy * dy), outer = RadialMenuGeometry.OuterRadius(size), inner = RadialMenuGeometry.InnerRadius(size);
        if (r <= inner || r > outer) return RadialMenuGeometry.SliceAt(dx, dy, size);
        var deg = Math.Atan2(dx, -dy) * 180.0 / Math.PI;
        if (deg < 0) deg += 360;
        const double step = 360.0 / Hues;
        int sector = (int)Math.Floor((deg + step / 2) / step) % Hues;
        double greyEdge = outer - (outer - inner) * GreyBand;
        if (r > greyEdge) return FirstGrey + sector;   // the greys ring has 12 steps, at the same angles as the hues
        int shade = Math.Clamp((int)((greyEdge - r) / ((greyEdge - inner) / Shades)), 0, Shades - 1);
        return Swatch(sector, shade);
    }

    /// <summary>The action name a palette tap is reported with (the colour itself travels as "#RRGGBB").</summary>
    public const string Action = "Colour";
}
