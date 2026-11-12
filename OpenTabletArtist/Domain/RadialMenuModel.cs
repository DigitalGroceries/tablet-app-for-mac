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

    public static readonly string[] All = { None, Keys, Click, Submenu, Open };
}

/// <summary>One menu ring: always <see cref="SliceCount"/> slices, slice 0 at the top, then clockwise.</summary>
public sealed class RadialMenuDef
{
    public const int SliceCount = 8;
    public const int MinSize = 1, DefaultSize = 50;
    /// <summary>TASKS #63 (Zak 21:01): the menu is never bigger than the one on his screen, which is size 50: 320 pt across
    /// (<see cref="RadialMenuGeometry.OuterRadius"/> 160). The slider's largest value is this; a saved larger size is used as this.</summary>
    public const int MaxSize = 50;
    /// <summary>The widest a menu can be, in points (the picture in the Pen page's Radial Menu Settings is never larger).</summary>
    public const double MaxDiameter = 320;
    /// <summary>Menu transparency in percent: 0 is solid, 100 is clear. TASKS #58 / #59 (Zak 18:21): 40 unless chosen (was 70).</summary>
    public const int MinTransparency = 0, MaxTransparency = 100, DefaultTransparency = 40;
    /// <summary>Top menu + 2 levels of submenus.</summary>
    public const int MaxDepth = 3;

    [JsonPropertyName("slices")] public List<RadialSlice> Slices { get; set; } = new();

    /// <summary>On-screen size 1–100 (same scale as the app's other sliders). Only the top menu's value is used.</summary>
    [JsonPropertyName("size")] public int Size { get; set; } = DefaultSize;

    /// <summary>How see-through the slices are, 0 (solid) to 100 (clear). Labels, lines and icons stay fully visible.
    /// Only the top menu's value is used; a menu saved before this existed reads as <see cref="DefaultTransparency"/>.</summary>
    [JsonPropertyName("transparency")] public int Transparency { get; set; } = DefaultTransparency;
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
        };
    }

    private static readonly System.Collections.Generic.HashSet<int> _loggedLargeSizes = new();

    /// <summary>A menu size within <see cref="RadialMenuDef.MinSize"/>..<see cref="RadialMenuDef.MaxSize"/>. A saved size above the
    /// largest (older versions allowed up to 100) is brought down to it; each such value is logged once per run.</summary>
    internal static int ClampSize(int size)
    {
        if (size > RadialMenuDef.MaxSize)
        {
            bool first; lock (_loggedLargeSizes) first = _loggedLargeSizes.Add(size);
            if (first)
                Services.AppLog.Info($"Radial menu: a saved menu size of {size} is larger than the largest allowed ({RadialMenuDef.MaxSize}, " +
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

    [JsonPropertyName("seq")] public long Seq { get; set; }
    [JsonPropertyName("op")] public string Op { get; set; } = "";
    [JsonPropertyName("slice")] public int Slice { get; set; } = -1;
    [JsonPropertyName("x")] public double X { get; set; }
    [JsonPropertyName("y")] public double Y { get; set; }
    [JsonPropertyName("menu")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Menu { get; set; }

    public string ToJson() => JsonSerializer.Serialize(this);

    /// <summary>The command in <paramref name="json"/>, or null when it can't be read. Never throws.</summary>
    public static RadialMenuCommand? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<RadialMenuCommand>(json); }
        catch (Exception) { return null; }
    }
}
