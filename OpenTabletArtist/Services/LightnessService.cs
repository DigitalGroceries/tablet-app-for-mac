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

    // ── V31 (ZAK-LISTS L7.1-L7.11, Zak 25 Nov 2026: "The theme sliders dont work properly if any other than the default factory
    // theme is tried. I cant add any value in their boxes too."; Burgundy, Custom and Rose had unreadable tab words, buttons
    // without their raised look, an invisible Save, washed-out switches) ──
    // Until Version 30 only the grey themes were driven; Rose, Burgundy and Custom kept their own fixed colours, so the Lightness
    // slider was greyed there and every colour added since (raised buttons, tabs, switches, the pen picture) was missing. Now
    // every theme goes through the same palette. A skin keeps what makes it that skin - its backdrop, petals, cards and accent -
    // and stays on its own side: Burgundy and Custom are dark (nine dark steps), Rose is light (nine light steps), because their
    // backdrops are dark and light and the words on the window must stay readable. Their greys are tinted with the skin's accent.

    /// <summary>Rose, Burgundy and Custom: the themes with a backdrop of their own.</summary>
    public static bool IsSkin(string choice) => choice is ThemeService.Anime or ThemeService.DarkSakura or ThemeService.Custom;

    /// <summary>Nine dark steps for the dark skins (Burgundy, Custom) and nine light ones for Rose; the light ones stop where
    /// a control's hover grey reaches the lightest grey allowed (#E4).</summary>
    public static readonly byte[] DarkSkinGreys = { 0x10, 0x14, 0x18, 0x1C, 0x20, 0x24, 0x28, 0x2C, 0x30 };
    public static readonly byte[] LightSkinGreys = { 0x84, 0x89, 0x8E, 0x94, 0x99, 0x9E, 0xA4, 0xA9, 0xAE };

    public static byte[] GreysFor(string theme) => theme switch
    {
        ThemeService.Anime => LightSkinGreys,
        ThemeService.DarkSakura or ThemeService.Custom => DarkSkinGreys,
        _ => BaseGreys,
    };

    /// <summary>The step a theme starts on: the grey themes on their darkest, Burgundy and Custom on the same window grey
    /// (#181818), Rose on its lightest.</summary>
    public static int DefaultStepFor(string theme) => theme switch
    {
        ThemeService.Anime => LightSkinGreys.Length - 1,
        ThemeService.DarkSakura or ThemeService.Custom => 2,
        _ => DefaultStep,
    };

    /// <summary>Each skin remembers its own step; the grey themes share one (the key since build R2).</summary>
    public static string KeyFor(string theme) => IsSkin(theme) ? Key + ":" + theme : Key;

    static string Theme => ThemeService.SavedChoice;

    public static int Saved
    {
        get => int.TryParse(AppSettings.Get(KeyFor(Theme)), out var v) ? Math.Clamp(v, 0, Steps - 1) : DefaultStepFor(Theme);
    }

    public static void Set(int step)
    {
        Store(step);
        ApplySaved();
    }

    /// <summary>Remembers the step for the theme in use without drawing it: for a skin, whose own refresh (accent and cards over
    /// the palette, ThemeViewModel.RefreshSkin) draws the palette once itself.</summary>
    public static void Store(int step)
    {
        step = Math.Clamp(step, 0, Steps - 1);
        AppSettings.Set(KeyFor(Theme), step.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>How strongly a skin's greys take its accent's hue (0 = plain grey).</summary>
    public static double TintStrength(string theme) => theme switch
    {
        ThemeService.Anime => 0.20,
        ThemeService.DarkSakura => 0.16,
        ThemeService.Custom => 0.14,
        _ => 0,
    };

    /// <summary>The colour a skin's greys lean towards: the accent chosen for that skin (Settings › Theme › Colours).</summary>
    public static Color TintHue(string theme)
    {
        string hex = theme switch
        {
            ThemeService.Anime => SkinColorSettings.SakuraAccentHex,
            ThemeService.DarkSakura => SkinColorSettings.DarkSakuraAccentHex,
            ThemeService.Custom => CustomThemeSettings.AccentHex,
            _ => "#808080",
        };
        return Color.TryParse(hex, out var c) ? c : Color.FromRgb(0xE0, 0x21, 0x8A);
    }

    /// <summary>A grey, leaned towards the skin's hue and brought back to the grey's own brightness (WCAG luminance), so every
    /// contrast the palette was picked for still holds. Colours that are not grey (yellow, green, red) and the grey themes are
    /// returned as they are. No channel goes above #E4.</summary>
    public static Color Tint(Color c, string theme)
    {
        double k = TintStrength(theme);
        if (k <= 0 || c.R != c.G || c.G != c.B) return c;
        var h = TintHue(theme);
        double r = c.R + (h.R - c.R) * k, g = c.G + (h.G - c.G) * k, b = c.B + (h.B - c.B) * k;
        double want = Luminance(c);
        double lo = 0, hi = 4;
        Color best = c;
        for (int i = 0; i < 24; i++)
        {
            double f = (lo + hi) / 2;
            best = Color.FromArgb(c.A, Ch(r * f), Ch(g * f), Ch(b * f));
            if (Luminance(best) < want) lo = f; else hi = f;
        }
        return best;
        static byte Ch(double v) => (byte)Math.Clamp(Math.Round(v), 0, NoNearWhite.Max);
    }
    static Color T(Color c) => Tint(c, Theme);

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
        int g = GreysFor(Theme)[Math.Clamp(step, 0, Steps - 1)];
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
    /// <summary>V30 build 4 (Zak 24 Nov 2026: "change the word CONNECTED to be in green"): Home's CONNECTED, in bold. Green, made
    /// lighter (dark steps) or darker (light steps) until it reads on the window and on a pane: 4.5:1 on the dark steps; on the
    /// mid-grey light steps 4.5:1 is only reached by a green so dark it reads as black, so there it is large bold text (22 px
    /// bold, WCAG "large text") at 3:1 or better.</summary>
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
            if (new[] { p.Base, p.Pane }.All(b => Contrast(c, b) >= (p.Dark ? 4.5 : 3.0))) return c;
        }
        return c;
        static byte Mix(byte a, byte b, double f) => (byte)Math.Round(a + (b - a) * f);
    }

    /// <summary>V30 build 4 (Zak 24 Nov 2026: "Again 2 designs of buttons"): tabs are drawn as raised buttons; the tab you are on
    /// is lit. Dark steps: the light selection fill with dark text, as before. Light steps: dark with light text, because a
    /// light tab would not stand out among light raised buttons.</summary>
    /// <summary>V30 build 4 (Zak 24 Nov 2026, the light-look picture of Tablet › Pen: "All the text in the black frame is not
    /// readable"): the Movement / Pen input inset was a fixed #262626 on every look. Dark steps keep it; light steps use the window
    /// grey (darker than the pane around it, and a surface the text colours are picked for), with the step's border.</summary>
    public static Color PenBasicsInset(int step) => For(step).Dark ? G(0x26) : For(step).Base;
    public static Color PenBasicsInsetBorder(int step) => For(step).Dark ? G(0x4A) : For(step).Border;

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
        var p = For(step);
        var r = RaisedAsDrawnFor(step, variant);
        if (!p.Dark) return r;
        // V31 (ZAK-LISTS L7.12): a word must be readable where it really sits. Measured on the real pages (ReadabilityAudit):
        // one line of text sits around the middle of its button, but the three lines of Home's big button start a fifth of the
        // way down, where a shaded fill is still near its light top. On the darkest steps the light word had its 4.5:1 there
        // (4.55 at step 1); on the higher dark steps the fill's top grew lighter while the word could not (nothing is brighter
        // than #E4), down to 3.5:1 at Lightness 5. So the top of the shade is held back until the word has 4.5:1 a fifth of
        // the way down. The darkest step is drawn exactly as before.
        var top = r.Top;
        double need = IsSkin(Theme) ? WordContrast + 0.1 : WordContrast;   // a skin's greys are tinted afterwards: room for the rounding
        while (top.R > p.Control.R + 8 && Contrast(p.Text, ShadeAt(top, r.Bottom, WordBandTop)) < need) top = Shift(top, -1);
        return (top, r.Bottom, r.Outline, r.Edge, r.Shadow, r.PressedShadow);
    }

    /// <summary>Where the words of a button can sit on its shaded fill, as a share of its height from the top: measured on the
    /// real pages, the ink of a one-line button runs from about 0.27 to 0.75, that of Home's three-line button from 0.20.</summary>
    public const double WordBandTop = 0.20, WordBandBottom = 0.80;
    /// <summary>The palette's rule for text (WCAG AA). Checked a fifth of the way down the shade, which is a little above
    /// where the worst of a word's ink really lies (measured: 4.84:1 for Home's big button at the darkest step, where this point
    /// gives 4.55), so a skin's tint and rounding have room.</summary>
    public const double WordContrast = 4.5;

    /// <summary>The colour of a top-to-bottom shade at a share of its height.</summary>
    public static Color ShadeAt(Color top, Color bottom, double share)
    {
        byte M(byte a, byte b) => (byte)Math.Round(a + (b - a) * share);
        return Color.FromRgb(M(top.R, bottom.R), M(top.G, bottom.G), M(top.B, bottom.B));
    }

    static (Color Top, Color Bottom, Color Outline, Thickness Edge, string Shadow, string PressedShadow) RaisedAsDrawnFor(int step, int variant)
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
        GradientStops = { new GradientStop(T(top), 0), new GradientStop(T(bottom), 1) },
    };

    /// <summary>The button's outline. Dark steps (Zak 24 Nov on Version 3: "In the dark GUI perhaps add a highlight line on the top
    /// line of the buttons"): the top edge is a light line that fades into the dark outline down the sides, so the button's top
    /// stands out from a dark window. An inset BoxShadow was tried first and is not drawn by the renderer (pixels unchanged).</summary>
    static IBrush Edge(int step, (Color Top, Color Bottom, Color Outline, Thickness Edge, string Shadow, string PressedShadow) r)
    {
        if (!For(step).Dark) return new SolidColorBrush(T(r.Outline));
        var hi = T(Shift(r.Top, 0x4C));
        r.Outline = T(r.Outline);
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
            ("ButtonRaisedPressedBrush", new SolidColorBrush(T(r.Bottom))),
            ("ButtonRaisedBorderBrush", Edge(step, r)),
            ("ButtonRaisedBorderThickness", r.Edge),
            ("TabOnFillBrush", new SolidColorBrush(T(TabOnFill(step)))), ("TabOnTextBrush", new SolidColorBrush(TabOnText(step))),
            ("TabOffTextBrush", new SolidColorBrush(For(step).Text)),
            ("DangerFillBrush", Shade(Danger(step).Top, Danger(step).Bottom)),
            ("DangerFillHoverBrush", Shade(Danger(step).HoverTop, Danger(step).HoverBottom)),
            ("DangerPressedBrush", new SolidColorBrush(Danger(step).Bottom)),
            ("DangerBorderBrush", new SolidColorBrush(Danger(step).Outline)),
            ("TextControlPlaceholderOpacity", 1.0),
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
            ("PenBasicsInsetBrush", PenBasicsInset(step)), ("PenBasicsInsetBorderBrush", PenBasicsInsetBorder(step)),
            ("DangerTextBrush", DangerText(step)),
            // V31 (L7.8, Zak 25 Nov: in Rose "the pen picture's Eraser and Tip circles are missing"): the circles were a fixed light
            // grey, invisible on a light page. Light on the dark steps (as before), dark on the light ones; the selected end stronger.
            ("PenEndCircleBrush", p.Dark ? Color.FromArgb(0x3B, 0xE4, 0xE4, 0xE4) : Color.FromArgb(0x55, 0x14, 0x14, 0x14)),
            ("PenEndCircleSelectedBrush", p.Dark ? Color.FromArgb(0xD9, 0xE4, 0xE4, 0xE4) : Color.FromArgb(0xE6, 0x14, 0x14, 0x14)),
            // V31 (L7.9, Rose: "the radial menu is flat grey with pale words"): the radial menu is dark and see-through, so on a light
            // page its picture washed out. On the light steps the picture sits on a dark disc, as the menu does over dark artwork.
            ("RadialPictureBackingBrush", p.Dark ? Color.FromArgb(0, 0, 0, 0) : Color.FromRgb(0x14, 0x14, 0x14)),
            ("AttentionBubbleBrush", Attention), ("AttentionBubbleTextBrush", AttentionInk),   // L6.3: the same in both looks
            ("PenDiagramFillBrush", p.Dark ? G(0x4A) : G(0x5A)), ("PenDiagramButtonFillBrush", p.Dark ? G(0x5E) : G(0x70)),
            ("PenDiagramStrokeBrush", p.Dark ? G(0xB0) : G(0x20)),
            ("WacomPaneBrush", p.Pane), ("WacomTileWellBrush", p.Well), ("ScribbleCanvasBrush", p.Canvas),
            ("AccentBrush", Accent), ("AccentMutedBrush", accentMuted), ("NavActiveBrush", accentMuted),
            // V31 (L7.5 / L7.12): Save is drawn as a raised button like the others, so on the dark steps its word is the text colour
            // (it was a slightly dimmer #D8, 3.6:1 on the raised fill at Lightness 5).
            ("AccentButtonFillBrush", accentMuted), ("AccentButtonForegroundBrush", p.Dark ? p.Text : G(0x24)),
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
            // V31 (L7.12): the grey hint inside an empty box ("Setting search") measured 2.1:1 on every look; it is the quiet text
            // colour now (3:1 or more on every surface), drawn at full strength (TextControlPlaceholderOpacity, RaisedResources).
            ("TextControlPlaceholderForeground", p.TextMuted), ("TextControlPlaceholderForegroundPointerOver", p.TextMuted),
            ("TextControlPlaceholderForegroundFocused", p.TextMuted),
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

    /// <summary>What a skin keeps for itself: its accent (sliders, ticks, the accent button) and its translucent cards. The
    /// Theme page sets these (ThemeViewModel.RefreshSkin); the palette leaves them alone.</summary>
    static readonly System.Collections.Generic.HashSet<string> SkinOwned = new()
    {
        "Accent", "AccentBrush", "AccentMutedBrush", "NavActiveBrush",
        "SystemAccentColor", "SystemAccentColorLight1", "SystemAccentColorDark1",
        "GlassBg", "GlassBgBrush",
    };

    /// <summary>Apply the saved step to the running app. Live; call on change. The grey themes also get their light or dark
    /// variant; a skin keeps its own variant and what is in <see cref="SkinOwned"/>.</summary>
    public static void ApplySaved()
    {
        if (Application.Current is not { } app) return;
        foreach (var k in Applied) app.Resources.Remove(k);
        Applied.Clear();
        string theme = Theme;
        bool skin = IsSkin(theme);
        if (DrivesTheme(theme) || skin)
        {
            int step = Saved;
            if (!skin) app.RequestedThemeVariant = For(step).Dark ? DarkVariant : LightVariant;
            foreach (var (k, c) in Resources(step))
            {
                if (skin && SkinOwned.Contains(k)) continue;
                app.Resources[k] = T(c); Applied.Add(k);
            }
            foreach (var (k, c) in BrushResources(step))
            {
                if (skin && SkinOwned.Contains(k)) continue;
                app.Resources[k] = new SolidColorBrush(T(c)); Applied.Add(k);
            }
            foreach (var (k, v) in RaisedResources(step, RaisedVariant)) { app.Resources[k] = v; Applied.Add(k); }
        }
        NoNearWhite.Apply(app);   // R12
        Changed?.Invoke();
    }

    /// <summary>Scribble canvas paper for the current step (soft grey, never white).</summary>
    public static Color CanvasColor => For(Saved).Canvas;
}
