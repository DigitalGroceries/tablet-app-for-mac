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
            ("DividerBrush", p.Divider), ("TextInverseBrush", p.OnAccent),
            ("PenDiagramFillBrush", p.Dark ? G(0x4A) : G(0x5A)), ("PenDiagramButtonFillBrush", p.Dark ? G(0x5E) : G(0x70)),
            ("PenDiagramStrokeBrush", p.Dark ? G(0xB0) : G(0x20)),
            ("WacomPaneBrush", p.Pane), ("WacomTileWellBrush", p.Well), ("ScribbleCanvasBrush", p.Canvas),
            ("AccentBrush", Accent), ("AccentMutedBrush", accentMuted), ("NavActiveBrush", accentMuted),
            ("AccentButtonFillBrush", accentMuted), ("AccentButtonForegroundBrush", p.Dark ? G(0xD8) : G(0x24)),
            ("AccentButtonFillHoverBrush", Accent), ("AccentButtonForegroundHoverBrush", p.OnAccent),
            ("SelectionFillBrush", SelectionFill(step)), ("SelectionTextBrush", SelectionText), ("SelectionBorderBrush", SelectionBorder(step)),
            ("CheckBoxCheckGlyphForegroundChecked", SelectionText), ("CheckBoxCheckGlyphForegroundCheckedPointerOver", SelectionText),
            ("CheckBoxCheckGlyphForegroundCheckedPressed", SelectionText),
            ("ToggleSwitchKnobFillOn", SelectionText), ("ToggleSwitchKnobFillOnPointerOver", SelectionText), ("ToggleSwitchKnobFillOnPressed", SelectionText),
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
        }
        NoNearWhite.Apply(app);   // R12
        Changed?.Invoke();
    }

    /// <summary>Scribble canvas paper for the current step (soft grey, never white).</summary>
    public static Color CanvasColor => For(Saved).Canvas;
}
