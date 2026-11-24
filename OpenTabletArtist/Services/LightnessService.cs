using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace OpenTabletArtist.Services;

/// <summary>Build R2: user-adjustable lightness (Settings › Theme), from an Affinity-style near-black look
/// (default) up to a soft light grey. Applied live by overriding the surface, text, border and Fluent control
/// brushes at the application level (top-level resources shadow the theme dictionaries). Never pure white:
/// the lightest surface is #E4E4E4 and the lightest text #E0E0E0. Text is chosen per step for WCAG AA
/// (≥ 4.5:1 for primary and secondary text, ≥ 3:1 for muted) on every surface. Only the neutral themes
/// (Wacom, Light, Dark, System) are driven; Sakura/Custom keep their own palettes.</summary>
public static class LightnessService
{
    public const string Key = "UiLightness";
    /// <summary>Window background grey per step. The gap between 0x2F and 0x84 skips the mid greys where
    /// neither light nor dark text reaches 4.5:1.</summary>
    public static readonly byte[] BaseGreys = { 0x18, 0x1D, 0x22, 0x28, 0x2F, 0x84, 0x92, 0xA0, 0xAE };
    public static int Steps => BaseGreys.Length;
    public const int DefaultStep = 0;   // #181818 window (R3)
    public static readonly ThemeVariant DarkVariant = new("LightnessDark", ThemeVariant.Dark);
    public static readonly ThemeVariant LightVariant = new("LightnessLight", ThemeVariant.Light);
    public static readonly Color Accent = Color.FromRgb(0x64, 0x64, 0x64);   // R12: neutral grey accent — no blue outlines or slider knobs; 4.7:1 with its #E4E4E4 label

    /// <summary>Build R5: selected/highlighted = light grey fill with near-black text (dark text on
    /// a light highlight; no pure white). On the dark steps the fill is #D8D8D8; on the light steps, where
    /// controls are already pale, it is the lightest grey #E4E4E4 with a dark outline so it still stands out.</summary>
    public static readonly Color SelectionText = Color.FromRgb(0x14, 0x14, 0x14);
    public static Color SelectionFill(int step) => For(step).Dark ? Color.FromRgb(0xD8, 0xD8, 0xD8) : G(0xE4);
    public static Color SelectionBorder(int step) => For(step).Dark ? SelectionFill(step) : G(0x30);

    public static event Action? Changed;

    public static int Saved
    {
        get => int.TryParse(AppSettings.Get(Key), out var v) ? Math.Clamp(v, 0, Steps - 1) : DefaultStep;
    }

    public static void Set(int step)
    {
        step = Math.Clamp(step, 0, Steps - 1);
        AppSettings.Set(Key, step.ToString(System.Globalization.CultureInfo.InvariantCulture));
        ApplySaved();
    }

    public sealed record Palette(bool Dark, Color Base, Color Elevated, Color Surface, Color Pane, Color Well,
        Color Control, Color Border, Color Divider, Color Text, Color TextSecondary, Color TextMuted, Color Canvas,
        Color Ink, Color OnAccent)
    {
        /// <summary>Build R3: hover is one step lighter than a raised control.</summary>
        public Color ControlHover { get; init; }
        /// <summary>The stacked layers, back to front: window, panel, well/list row, control, control hover.</summary>
        public Color[] Layers => new[] { Base, Pane, Well, Control, ControlHover };
    }

    private static Color G(int v) { var b = (byte)Math.Clamp(v, 0, 0xE4); return Color.FromRgb(b, b, b); }

    // Build R3 ("too flat"): clear steps between layers, Wacom-panel style. Default step 0 is
    // #181818 window, #2A2A2A panels, #353535 wells/list rows, #414141 controls (#4D4D4D hover), #4A4A4A borders.
    // Each layer is at least 10 grey levels above the one behind it at every step.
    private const int PaneOff = 0x12, WellOff = 0x1D, ControlOff = 0x29, HoverOff = 0x35;

    public static Palette For(int step)
    {
        int g = BaseGreys[Math.Clamp(step, 0, Steps - 1)];
        bool dark = g < 0x80;
        var pane = G(g + PaneOff);
        var p = new Palette(dark,
            Base: G(g), Elevated: pane, Surface: pane, Pane: pane, Well: G(g + WellOff), Control: G(g + ControlOff),
            Border: dark ? G(g + 0x32) : G(g - 0x1E), Divider: dark ? G(g + 0x2C) : G(g - 0x16),
            Text: default, TextSecondary: default, TextMuted: default,
            Canvas: G(dark ? 0xA8 : Math.Min(g + 0x18, 0xE4)), Ink: G(0x22), OnAccent: G(0xE4))
        { ControlHover = G(g + HoverOff) };
        Color[] surfaces = p.Layers;
        return p with
        {
            Text = Pick(surfaces, dark, 4.5, dark ? 0xE0 : 0x10),
            TextSecondary = Pick(surfaces, dark, 4.5, dark ? 0xC8 : 0x30),
            TextMuted = Pick(surfaces, dark, 3.0, dark ? 0xA0 : 0x50),
        };
    }

    /// <summary>Start at the preferred grey and move towards the extreme until every surface passes.</summary>
    private static Color Pick(Color[] surfaces, bool dark, double ratio, int start)
    {
        for (int v = start; dark ? v <= 0xE4 : v >= 0x00; v += dark ? 4 : -4)
        {
            var c = G(v); bool ok = true;
            foreach (var s in surfaces) if (Contrast(c, s) < ratio) { ok = false; break; }
            if (ok) return c;
        }
        return G(dark ? 0xE4 : 0);
    }

    public static double Luminance(Color c)
    {
        static double L(byte b) { double s = b / 255.0; return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * L(c.R) + 0.7152 * L(c.G) + 0.0722 * L(c.B);
    }

    public static double Contrast(Color a, Color b)
    {
        double x = Luminance(a), y = Luminance(b);
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    public static readonly Color Warning = Color.FromRgb(0xF5, 0x9E, 0x0B);

    /// <summary>Item 14 (#23): warning TEXT (Setup's hints, Wacom removal errors) in the warning hue, scaled
    /// darker (light steps) or towards white (dark steps) until it has 4.5:1 on the sheet background
    /// (BgElevated) and the window (BgBase). The plain amber had about 1.6:1 on the light steps.</summary>
    public static Color WarningText(int step) => Readable(Warning, step);

    /// <summary>V30 (Zak 23 Nov 2026: "These warning texts must be in bold yellow"): the yellow of a control with nothing on
    /// it ("Not set up"), kept readable on every step the same way as <see cref="WarningText"/>.</summary>
    public static readonly Color Attention = Color.FromRgb(0xFA, 0xCC, 0x15);
    /// <summary>The dark ink on the yellow highlight (light steps).</summary>
    public static readonly Color AttentionInk = Color.FromRgb(0x1C, 0x16, 0x00);
    /// <summary>Dark steps: yellow text. Light steps: yellow cannot be read as text on a light window (made readable it turned
    /// dark brown - the fresh check of test build 2's pictures, 23 Nov 2026), so the text is dark ink on a yellow highlight
    /// (<see cref="AttentionFill"/>): still yellow, still bold, and 4.5:1 or better.</summary>
    public static Color AttentionText(int step) => For(step).Dark ? Readable(Attention, step) : AttentionInk;
    /// <remarks>Transparent black, not Colors.Transparent (whose R, G and B are full white): the no-white check reads R, G and B only (CI, 23 Nov 2026).</remarks>
    public static Color AttentionFill(int step) => For(step).Dark ? Color.FromArgb(0, 0, 0, 0) : Attention;

    /// <summary>A switch that is On. Dark steps: a light track with a black knob. Light steps: the light track vanished into
    /// the light window (only the knob showed, test build 2 check, 23 Nov 2026), so the track is dark and the knob light.</summary>
    /// <summary>V30 build 4 (Zak 24 Nov 2026: "change the word CONNECTED to be in green"): Home's CONNECTED. Green, made lighter
    /// (dark steps) or darker (light steps) until it reads at 4.5:1 or better on every surface it can sit on.</summary>
    public static readonly Color Connected = Color.FromRgb(0x22, 0xC5, 0x5E);
    public static Color ConnectedText(int step)
    {
        var p = For(step);
        var c = Connected;
        for (int i = 0; i <= 100; i++)
        {
            double f = i / 100.0;
            c = p.Dark
                ? Color.FromRgb(Mix(Connected.R, 255, f), Mix(Connected.G, 255, f), Mix(Connected.B, 255, f))
                : Color.FromRgb(Mix(Connected.R, 0, f), Mix(Connected.G, 0, f), Mix(Connected.B, 0, f));
            if (new[] { p.Base, p.Elevated, p.Surface, p.Pane }.All(b => Contrast(c, b) >= 4.5)) return c;
        }
        return c;
        static byte Mix(byte a, byte b, double f) => (byte)Math.Round(a + (b - a) * f);
    }

    /// <summary>V30 build 4 (Zak 24 Nov 2026: "Again 2 designs of buttons"): tabs are drawn as raised buttons; the tab you are on
    /// is lit. Dark steps: the light selection fill with dark text, as before. Light steps: dark with light text, because a
    /// light tab would not stand out among light raised buttons.</summary>
    public static Color TabOnFill(int step) => For(step).Dark ? SelectionFill(step) : G(0x30);
    public static Color TabOnText(int step) => For(step).Dark ? SelectionText : G(0xE4);

    /// <summary>V30 (ZAK-LISTS L6.5, Zak 24 Nov 2026: "All of our app buttons need to look a bit more 3D now they all look like
    /// speech bubbles"): a raised button - an outline, a soft top-to-bottom shade and a small shadow; pressed, it looks pushed in.
    /// Three strengths were drawn for Zak to choose from; <see cref="RaisedVariant"/> is the one in use.</summary>
    public static int RaisedVariant { get; set; } = 3;   // Zak 24 Nov: Version 3 for light and dark

    static Color Shift(Color c, int by) => G(Math.Clamp(c.R + by, 0, 0xE4));

    /// <summary>A raised button for a step and strength 1-3: fill top and bottom, outline, outline thickness, shadow, pressed shadow.
    /// 1 Outline: a clear edge and a light shade. 2 Raised: a stronger shade, a light top edge and a shadow. 3 Key: like a keyboard key,
    /// a deep shade, a light top edge, a thick dark bottom edge and a deeper shadow.</summary>
    public static (Color Top, Color Bottom, Color Outline, Thickness Edge, string Shadow, string PressedShadow) Raised(int step, int variant)
    {
        var p = For(step); var c = p.Control;
        string A(string dark, string light) => p.Dark ? dark : light;
        return variant switch
        {
            1 => (Shift(c, 0x10), Shift(c, -0x06), p.Dark ? Shift(c, 0x30) : Shift(c, -0x58), new Thickness(1),
                  $"0 1 1 0 #{A("50", "30")}000000", "inset 0 1 3 0 #73000000"),
            3 => (Shift(c, 0x30), Shift(c, -0x14), p.Dark ? Shift(c, -0x22) : Shift(c, -0x6A), new Thickness(1, 1, 1, 3),
                  $"inset 0 1 0 0 #{A("59", "C0")}E4E4E4, 0 3 6 0 #{A("C0", "66")}000000", "inset 0 2 4 0 #8C000000"),
            _ => (Shift(c, 0x22), Shift(c, -0x0C), p.Dark ? Shift(c, 0x2C) : Shift(c, -0x60), new Thickness(1),
                  $"inset 0 1 0 0 #{A("40", "99")}E4E4E4, 0 2 4 0 #{A("A0", "55")}000000", "inset 0 1 3 0 #80000000"),
        };
    }

    static LinearGradientBrush Shade(Color top, Color bottom) => new()
    {
        StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(top, 0), new GradientStop(bottom, 1) },
    };

    /// <summary>The button's outline. Dark steps (Zak 24 Nov on Version 3: "In the dark GUI perhaps add a highlight line on the top
    /// line of the buttons"): the top edge is a light line that fades into the dark outline down the sides, so the button's top
    /// stands out from a dark window. An inset BoxShadow was tried first and is not drawn by the renderer (pixels unchanged).</summary>
    static IBrush Edge(int step, (Color Top, Color Bottom, Color Outline, Thickness Edge, string Shadow, string PressedShadow) r)
    {
        if (!For(step).Dark) return new SolidColorBrush(r.Outline);
        var hi = Shift(r.Top, 0x4C);
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(hi, 0), new GradientStop(hi, 0.03), new GradientStop(r.Outline, 0.22), new GradientStop(r.Outline, 1) },
        };
    }

    /// <summary>The raised-button resources (brushes and shadows) for a step, applied with the other colours.</summary>
    public static (string Key, object Value)[] RaisedResources(int step, int variant)
    {
        var r = Raised(step, variant); var h = For(step).ControlHover;
        int d = r.Top.R - For(step).Control.R;
        return new (string, object)[]
        {
            ("ButtonRaisedFillBrush", Shade(r.Top, r.Bottom)),
            ("ButtonRaisedFillHoverBrush", Shade(Shift(h, d), Shift(h, -(For(step).Control.R - r.Bottom.R)))),
            ("ButtonRaisedPressedBrush", new SolidColorBrush(r.Bottom)),
            ("ButtonRaisedBorderBrush", Edge(step, r)),
            ("ButtonRaisedBorderThickness", r.Edge),
            ("TabOnFillBrush", new SolidColorBrush(TabOnFill(step))), ("TabOnTextBrush", new SolidColorBrush(TabOnText(step))),
            ("TabOffTextBrush", new SolidColorBrush(For(step).Text)),
            ("DangerFillBrush", Shade(Danger(step).Top, Danger(step).Bottom)),
            ("DangerFillHoverBrush", Shade(Danger(step).HoverTop, Danger(step).HoverBottom)),
            ("DangerPressedBrush", new SolidColorBrush(Danger(step).Bottom)),
            ("DangerBorderBrush", new SolidColorBrush(Danger(step).Outline)),
            ("ButtonRaisedShadow", BoxShadows.Parse(r.Shadow)),
            ("ButtonPressedShadow", BoxShadows.Parse(r.PressedShadow)),
        };
    }

    public static Color SwitchOnFill(int step) => For(step).Dark ? SelectionFill(step) : G(0x30);
    public static Color SwitchOnKnob(int step) => For(step).Dark ? SelectionText : G(0xE4);

    /// <summary>V30 build 3 (ZAK-LISTS L6.2, Zak 24 Nov: "Forget this tablet" as its own smaller, quieter red button): a deep red
    /// raised fill (top, bottom), its outline and hover fill. Red WORDS on the grey raised button could not reach 4.5:1 (the
    /// button's top is a mid grey), so the button is red and its word is the light on-accent grey (5:1 or more on both looks).</summary>
    public static (Color Top, Color Bottom, Color Outline, Color HoverTop, Color HoverBottom) Danger(int step) => For(step).Dark
        ? (Color.FromRgb(0x8A, 0x28, 0x28), Color.FromRgb(0x5C, 0x18, 0x18), Color.FromRgb(0x3A, 0x0E, 0x0E), Color.FromRgb(0x9C, 0x30, 0x30), Color.FromRgb(0x6A, 0x1C, 0x1C))
        : (Color.FromRgb(0xA8, 0x30, 0x30), Color.FromRgb(0x7A, 0x1E, 0x1E), Color.FromRgb(0x5A, 0x14, 0x14), Color.FromRgb(0xB4, 0x36, 0x36), Color.FromRgb(0x86, 0x22, 0x22));
    public static Color DangerText(int step) => For(step).OnAccent;

    /// <summary>The hue, scaled darker (light steps) or towards white (dark steps) until it has 4.5:1 on BgElevated and BgBase.</summary>
    private static Color Readable(Color hue, int step)
    {
        var p = For(step);
        var c = hue;
        for (int i = 0; i <= 100; i++)
        {
            double f = i / 100.0;
            c = p.Dark
                ? Color.FromRgb(Mix(hue.R, 255, f), Mix(hue.G, 255, f), Mix(hue.B, 255, f))
                : Color.FromRgb(Mix(hue.R, 0, f), Mix(hue.G, 0, f), Mix(hue.B, 0, f));
            if (Contrast(c, p.Elevated) >= 4.5 && Contrast(c, p.Base) >= 4.5) return c;
        }
        return c;
        static byte Mix(byte a, byte b, double f) => (byte)Math.Round(a + (b - a) * f);
    }

    public static bool DrivesTheme(string choice) =>
        choice is ThemeService.Wacom or ThemeService.Light or ThemeService.Dark or ThemeService.System;

    /// <summary>The keys this service sets, with their colours, for the given step.</summary>
    public static (string Key, Color Color)[] Resources(int step)
    {
        var p = For(step);
        return new[]
        {
            ("BgBase", p.Base), ("BgElevated", p.Elevated), ("BgSurface", p.Surface),
            ("GlassBg", p.Surface), ("GlassBgHover", p.ControlHover), ("GlassBorder", p.Border),
            ("TextPrimary", p.Text), ("TextSecondary", p.TextSecondary), ("TextMuted", p.TextMuted), ("Divider", p.Divider),
            ("TextInverse", p.OnAccent), ("Accent", Accent), ("SystemAccentColor", Accent),
            ("SystemAccentColorLight1", Color.FromRgb(0x84, 0x84, 0x84)), ("SystemAccentColorDark1", Color.FromRgb(0x58, 0x58, 0x58)),
        };
    }

    public static (string Key, Color Color)[] BrushResources(int step)
    {
        var p = For(step);
        var accentMuted = Color.FromArgb(0x40, Accent.R, Accent.G, Accent.B);
        return new[]
        {
            ("BgBaseBrush", p.Base), ("BgElevatedBrush", p.Elevated), ("BgSurfaceBrush", p.Surface),
            ("GlassBgBrush", p.Surface), ("GlassBgHoverBrush", p.ControlHover), ("GlassBorderBrush", p.Border),
            ("TextPrimaryBrush", p.Text), ("TextSecondaryBrush", p.TextSecondary), ("TextMutedBrush", p.TextMuted),
            ("DividerBrush", p.Divider), ("TextInverseBrush", p.OnAccent), ("WarningTextBrush", WarningText(step)), ("AttentionTextBrush", AttentionText(step)),
            ("AttentionFillBrush", AttentionFill(step)), ("SwitchOnFillBrush", SwitchOnFill(step)), ("ConnectedTextBrush", ConnectedText(step)),
            ("DangerTextBrush", DangerText(step)),
            ("AttentionBubbleBrush", Attention), ("AttentionBubbleTextBrush", AttentionInk),   // L6.3: the same in both looks
            ("PenDiagramFillBrush", p.Dark ? G(0x4A) : G(0x5A)), ("PenDiagramButtonFillBrush", p.Dark ? G(0x5E) : G(0x70)),
            ("PenDiagramStrokeBrush", p.Dark ? G(0xB0) : G(0x20)),
            ("WacomPaneBrush", p.Pane), ("WacomTileWellBrush", p.Well), ("ScribbleCanvasBrush", p.Canvas),
            ("AccentBrush", Accent), ("AccentMutedBrush", accentMuted), ("NavActiveBrush", accentMuted),
            ("AccentButtonFillBrush", accentMuted), ("AccentButtonForegroundBrush", p.Dark ? G(0xD8) : G(0x24)),
            ("AccentButtonFillHoverBrush", Accent), ("AccentButtonForegroundHoverBrush", p.OnAccent),
            ("SelectionFillBrush", SelectionFill(step)), ("SelectionTextBrush", SelectionText), ("SelectionBorderBrush", SelectionBorder(step)),
            ("CheckBoxCheckGlyphForegroundChecked", SelectionText), ("CheckBoxCheckGlyphForegroundCheckedPointerOver", SelectionText),
            ("CheckBoxCheckGlyphForegroundCheckedPressed", SelectionText),
            ("ToggleSwitchKnobFillOn", SwitchOnKnob(step)), ("ToggleSwitchKnobFillOnPointerOver", SwitchOnKnob(step)), ("ToggleSwitchKnobFillOnPressed", SwitchOnKnob(step)),
            // Fluent controls: text fields, lists, combo boxes, buttons, dialogs.
            ("TextControlBackground", p.Well), ("TextControlBackgroundPointerOver", p.Control),
            ("TextControlBackgroundFocused", p.Well), ("TextControlForeground", p.Text),
            ("TextControlForegroundPointerOver", p.Text), ("TextControlForegroundFocused", p.Text),
            ("TextControlBorderBrush", p.Border), ("TextControlBorderBrushPointerOver", p.Border), ("ButtonBorderBrush", p.Border), ("ComboBoxBorderBrush", p.Border),
            ("ComboBoxBackground", p.Control), ("ComboBoxBackgroundPointerOver", p.ControlHover),
            ("ComboBoxDropDownBackground", p.Elevated), ("ComboBoxForeground", p.Text),
            ("ButtonBackground", p.Control), ("ButtonBackgroundPointerOver", p.ControlHover), ("ButtonForeground", p.Text),
            ("ListBoxBackground", p.Pane), ("ListBoxItemBackground", p.Well), ("ListBoxItemBackgroundPointerOver", p.Control), ("ListBoxItemForeground", p.Text),
            ("FlyoutPresenterBackground", p.Elevated), ("MenuFlyoutPresenterBackground", p.Elevated),
            ("SystemControlBackgroundAltHighBrush", p.Base), ("SystemControlPageBackgroundAltHighBrush", p.Base),
            ("SystemControlForegroundBaseHighBrush", p.Text), ("ToolTipBackground", p.Elevated), ("ToolTipForeground", p.Text),
        };
    }

    private static readonly System.Collections.Generic.List<string> Applied = new();

    /// <summary>Apply the saved step to the running app (or remove it for Sakura/Custom). Live; call on change.</summary>
    public static void ApplySaved()
    {
        if (Application.Current is not { } app) return;
        foreach (var k in Applied) app.Resources.Remove(k);
        Applied.Clear();
        if (DrivesTheme(ThemeService.SavedChoice))
        {
            int step = Saved;
            app.RequestedThemeVariant = For(step).Dark ? DarkVariant : LightVariant;
            foreach (var (k, c) in Resources(step)) { app.Resources[k] = c; Applied.Add(k); }
            foreach (var (k, c) in BrushResources(step)) { app.Resources[k] = new SolidColorBrush(c); Applied.Add(k); }
            foreach (var (k, v) in RaisedResources(step, RaisedVariant)) { app.Resources[k] = v; Applied.Add(k); }
        }
        NoNearWhite.Apply(app);   // R12
        Changed?.Invoke();
    }

    /// <summary>Scribble canvas paper for the current step (soft grey, never white).</summary>
    public static Color CanvasColor => For(Saved).Canvas;
}
