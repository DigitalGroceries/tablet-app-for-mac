using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.VisualTree;
using OpenTabletArtist.Services;
using static OpenTabletArtist.SweepTests.Sweep;

namespace OpenTabletArtist.SweepTests;

/// <summary>V31 (ZAK-LISTS L7.12, Zak 25 Nov 2026: in Burgundy and Custom the tab words were dark on a dark window, in Rose "Save"
/// and a dialog button's words were almost invisible, in Custom the knob of a switch that is On could not be seen): measures what
/// is REALLY on the screen, not what the colours were meant to be. The window is drawn twice, once as it is and once with every
/// text hidden; where the two pictures differ is the ink of a word, and the second picture is what lies behind it (a shaded
/// button, a panel, the backdrop with its glows and petals). A word's contrast is its drawn colour against the worst tenth of
/// what lies behind its own ink, by the WCAG formula the palette uses (<see cref="LightnessService.Contrast"/>).</summary>
internal static class ReadabilityAudit
{
    /// <summary>One measured word. <see cref="BandTop"/> / <see cref="BandBottom"/>: where its ink starts and ends on its button, as a
    /// share of the button's height (only for a word on a button or tab).</summary>
    internal sealed record Word(string Page, string Text, string Where, bool OnControl, bool Large, double Contrast, double Needed,
        string Ink, string Behind, int InkPixels, double BandTop, double BandBottom)
    {
        public bool Ok => Contrast >= Needed;
        public override string ToString() =>
            $"{Page}: '{Text}' {Contrast:0.00}:1 (needs {Needed:0.0}) ink {Ink} on {Behind}{(OnControl ? " [on a button or tab]" : "")}{(Large ? " [large]" : "")} ({Where})";
    }

    /// <summary>A word on a button or tab, and the palette's own rule for text: 4.5:1. Large text (24 px, or 18.66 px bold: WCAG's
    /// "large") and every other word on the page: 3:1, the palette's floor for its quietest text - below that a word is not
    /// just quiet, it is hard to find.</summary>
    public const double ControlWord = 4.5, AnyWord = 3.0;

    internal sealed class Frame
    {
        public int W, H; public byte[] Px = Array.Empty<byte>(); public bool Bgra;
        public (byte R, byte G, byte B) At(int x, int y)
        {
            int i = (y * W + x) * 4;
            return Bgra ? (Px[i + 2], Px[i + 1], Px[i]) : (Px[i], Px[i + 1], Px[i + 2]);
        }
    }

    internal static Frame Capture(Window w)
    {
        Pump(2); w.UpdateLayout(); Pump(1);
        var bmp = w.CaptureRenderedFrame() ?? throw new InvalidOperationException("no frame was drawn");
        using var fb = bmp.Lock();
        var f = new Frame { W = fb.Size.Width, H = fb.Size.Height, Bgra = fb.Format != PixelFormat.Rgba8888 };
        f.Px = new byte[f.W * f.H * 4];
        for (int y = 0; y < f.H; y++) Marshal.Copy(fb.Address + y * fb.RowBytes, f.Px, y * f.W * 4, f.W * 4);
        return f;
    }

    static string TextOf(TextBlock t) =>
        t.Text ?? (t.Inlines != null ? string.Concat(t.Inlines.OfType<Avalonia.Controls.Documents.Run>().Select(r => r.Text)) : "");

    static string Short(string s) { s = s.Replace('\n', ' ').Trim(); return s.Length > 48 ? s[..48] + "…" : s; }

    static string Place(Visual v)
    {
        var names = new List<string>();
        for (Visual? p = v; p != null && names.Count < 3; p = p.GetVisualParent())
            if (p is StyledElement se && !string.IsNullOrEmpty(se.Name)) names.Add(se.Name!);
        var view = v.GetVisualAncestors().OfType<UserControl>().FirstOrDefault()?.GetType().Name ?? v.GetVisualAncestors().OfType<Window>().FirstOrDefault()?.GetType().Name ?? "?";
        return names.Count > 0 ? $"{view}/{string.Join("<", names)}" : view;
    }

    static string Hex((byte R, byte G, byte B) c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    static Color Col((byte R, byte G, byte B) c) => Color.FromRgb(c.R, c.G, c.B);

    /// <summary>Every visible text of the window that takes input (a greyed control's word is quiet on purpose), measured.
    /// A word under something else (a page behind an open sheet) is left out.</summary>
    public static List<Word> Measure(Window w, string page)
    {
        var shown = Capture(w);
        var texts = w.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible && t.Bounds.Width > 0 && t.Bounds.Height > 0).ToList();
        var was = texts.Select(t => t.Opacity).ToList();
        foreach (var t in texts) t.SetCurrentValue(Visual.OpacityProperty, 0.0);
        Frame behind;
        try { behind = Capture(w); }
        finally { for (int i = 0; i < texts.Count; i++) texts[i].SetCurrentValue(Visual.OpacityProperty, was[i]); Pump(1); }

        var words = new List<Word>();
        foreach (var t in texts)
        {
            var text = TextOf(t);
            if (string.IsNullOrWhiteSpace(text) || !t.IsEffectivelyEnabled) continue;
            if (t.TranslatePoint(new Point(0, 0), w) is not { } tl) continue;
            int x0 = Math.Max(0, (int)Math.Floor(tl.X)), y0 = Math.Max(0, (int)Math.Floor(tl.Y));
            int x1 = Math.Min(shown.W, (int)Math.Ceiling(tl.X + t.Bounds.Width)), y1 = Math.Min(shown.H, (int)Math.Ceiling(tl.Y + t.Bounds.Height));
            if (x1 - x0 < 2 || y1 - y0 < 2) continue;

            // the ink: where the two pictures differ
            var ink = new List<(int X, int Y, int D)>();
            int most = 0;
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    var a = shown.At(x, y); var b = behind.At(x, y);
                    int d = Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
                    if (d > 0) { ink.Add((x, y, d)); if (d > most) most = d; }
                }
            // not drawn here (scrolled away, clipped, see-through), or only a drifting petal changed between the two pictures
            if (ink.Count < 12 || most < 12) continue;

            // something else on top of it (an open sheet's scrim)? then the word is not the page's word right now
            var mid = new Point(tl.X + t.Bounds.Width / 2, tl.Y + t.Bounds.Height / 2);
            if (w.InputHitTest(mid) is Visual hit && !ReferenceEquals(hit, t) && !t.GetVisualAncestors().Contains(hit) && !hit.GetVisualAncestors().Contains(t))
                continue;

            // the colour the word is drawn in: the pixels its ink covers fully (the strongest tenth of the differences)
            var solid = ink.Where(p => p.D >= most * 0.85).ToList();
            byte Med(Func<(byte R, byte G, byte B), byte> ch) { var v = solid.Select(p => ch(shown.At(p.X, p.Y))).OrderBy(z => z).ToList(); return v[v.Count / 2]; }
            var inkColour = (R: Med(c => c.R), G: Med(c => c.G), B: Med(c => c.B));
            var fg = Col(inkColour);

            // what lies behind the body of the letters (half covered or more), worst tenth
            var body = ink.Where(p => p.D >= most * 0.5).ToList();
            var ratios = body.Select(p => (C: LightnessService.Contrast(fg, Col(behind.At(p.X, p.Y))), p.X, p.Y)).OrderBy(z => z.C).ToList();
            var worst = ratios[Math.Min(ratios.Count - 1, ratios.Count / 10)];

            var holder = t.GetVisualAncestors().OfType<Control>().FirstOrDefault(c => c is Button or ToggleButton);
            bool large = t.FontSize >= 24 || (t.FontSize >= 18.66 && (int)t.FontWeight >= (int)FontWeight.Bold);
            double top = double.NaN, bottom = double.NaN;
            if (holder != null && holder.TranslatePoint(new Point(0, 0), w) is { } htl && holder.Bounds.Height > 0)
            {
                top = (body.Min(p => p.Y) - htl.Y) / holder.Bounds.Height;
                bottom = (body.Max(p => p.Y) + 1 - htl.Y) / holder.Bounds.Height;
            }
            words.Add(new Word(page, Short(text), Place(t), holder != null, large, worst.C,
                holder != null && !large ? ControlWord : AnyWord, Hex(inkColour), Hex(behind.At(worst.X, worst.Y)), body.Count, top, bottom));
        }
        return words;
    }
}
