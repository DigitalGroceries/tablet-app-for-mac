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
    public const int MinSize = 1, MaxSize = 100, DefaultSize = 50;
    /// <summary>Top menu + 2 levels of submenus.</summary>
    public const int MaxDepth = 3;

    [JsonPropertyName("slices")] public List<RadialSlice> Slices { get; set; } = new();

    /// <summary>On-screen size 1–100 (same scale as the app's other sliders). Only the top menu's value is used.</summary>
    [JsonPropertyName("size")] public int Size { get; set; } = DefaultSize;
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
            Size = Math.Clamp(def?.Size ?? RadialMenuDef.DefaultSize, RadialMenuDef.MinSize, RadialMenuDef.MaxSize),
        };
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
    /// <summary>Tapped the centre (the X): close.</summary>
    public const int Centre = -1;
    /// <summary>Tapped outside the ring: close.</summary>
    public const int Outside = -2;

    /// <summary>Outer radius in points for size 1–100: 50 gives 160 pt (a 320 pt menu), 1 gives 90, 100 gives 230.</summary>
    public static double OuterRadius(int size) =>
        90 + 1.4 * Math.Clamp(size, RadialMenuDef.MinSize, RadialMenuDef.MaxSize);

    /// <summary>Radius of the centre circle (the X).</summary>
    public static double InnerRadius(int size) => OuterRadius(size) * 0.26;

    /// <summary>The slice at offset (<paramref name="dx"/>, <paramref name="dy"/>) from the centre: 0 = top, then
    /// clockwise; <see cref="Centre"/> inside the centre circle; <see cref="Outside"/> beyond the ring.</summary>
    public static int SliceAt(double dx, double dy, int size)
    {
        var r = Math.Sqrt(dx * dx + dy * dy);
        if (r <= InnerRadius(size)) return Centre;
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
