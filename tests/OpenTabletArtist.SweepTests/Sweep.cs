using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace OpenTabletArtist.SweepTests;

/// <summary>Shared driver + auditor. Records every finding into <see cref="Report"/> rather than failing
/// on the first, so one run lists everything.</summary>
internal static class Sweep
{
    public static readonly string Root = FindRoot();
    public static readonly string Shots = Path.Combine(Root, "shots");
    public static readonly List<string> Defects = new();
    public static readonly List<string> Passes = new();
    public static readonly List<string> Skipped = new();
    public static readonly List<string> ShotFiles = new();
    public static readonly List<string> Exceptions = new();
    private static bool _hooked;

    private static string FindRoot()
    {
        var d = AppContext.BaseDirectory;
        while (d != null && !File.Exists(Path.Combine(d, "OpenTabletArtist.slnx"))) d = Path.GetDirectoryName(d);
        return d ?? Directory.GetCurrentDirectory();
    }

    public static void Hook()
    {
        Directory.CreateDirectory(Shots);
        if (_hooked) return; _hooked = true;
        Window.WindowOpenedEvent.AddClassHandler(typeof(Window), (sender, _) => { if (sender is Window w) lock (Open) { Open.Add(w); w.Closed += (_, _) => { lock (Open) Open.Remove(w); }; } });
        Dispatcher.UIThread.UnhandledException += (_, e) => { Exceptions.Add($"UI thread: {e.Exception.GetType().Name}: {e.Exception.Message}"); e.Handled = true; };
        TaskScheduler.UnobservedTaskException += (_, e) => { Exceptions.Add($"Unobserved task: {e.Exception.InnerException?.GetType().Name}: {e.Exception.InnerException?.Message}"); e.SetObserved(); };
    }

    public static void Defect(string s) { lock (Defects) if (!Defects.Contains(s)) Defects.Add(s); }
    public static void Pass(string s) { lock (Passes) Passes.Add(s); }
    public static void Skip(string s) { lock (Skipped) if (!Skipped.Contains(s)) Skipped.Add(s); }

    public static void Pump(int n = 6)
    {
        for (int i = 0; i < n; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    }

    public static async Task Settle(int ms = 60)
    {
        Pump(); await Task.Delay(ms); Pump();
    }

    public static Window Host(Control content, double w = 1100, double h = 800)
    {
        var win = new Window { Width = w, Height = h, Content = content };
        win.Bind(Window.BackgroundProperty, win.GetResourceObservable("BgBaseBrush"));
        win.Show(); Pump(10); win.UpdateLayout(); Pump();
        return win;
    }

    public static string Slug(string s) => Regex.Replace(s.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');

    /// <summary>Capture the window to shots/name.png and scan the frame for pixels brighter than #E4E4E4.</summary>
    public static void Shot(TopLevel win, string name)
    {
        Pump(10);
        var bmp = win is Window w ? w.CaptureRenderedFrame() : null;
        if (bmp is null) { Defect($"[shot] {name}: no frame rendered"); return; }
        var file = Path.Combine(Shots, Slug(name) + ".png");
        bmp.Save(file);
        lock (ShotFiles) ShotFiles.Add(file);
        if (bmp is WriteableBitmap wb) ScanWhite(wb, name);
    }

    private static void ScanWhite(WriteableBitmap wb, string name)
    {
        using var fb = wb.Lock();
        int w = fb.Size.Width, h = fb.Size.Height, stride = fb.RowBytes;
        var row = new byte[stride];
        int count = 0, minX = int.MaxValue, minY = int.MaxValue, maxX = 0, maxY = 0;
        for (int y = 0; y < h; y++)
        {
            Marshal.Copy(fb.Address + y * stride, row, 0, stride);
            for (int x = 0; x < w; x++)
            {
                byte b = row[x * 4], g = row[x * 4 + 1], r = row[x * 4 + 2], a = row[x * 4 + 3];
                if (a > 200 && r > 0xE4 && g > 0xE4 && b > 0xE4)
                { count++; minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y); }
            }
        }
        if (count > 20) Defect($"[near-white] {name}: {count} px brighter than #E4E4E4 within x{minX}-{maxX}, y{minY}-{maxY} (frame {w}x{h})");
    }

    // ── audit ──────────────────────────────────────────────────────────────────────────────────────

    private static readonly HashSet<string> Acronyms = new(StringComparer.Ordinal)
    { "OTD", "OTA", "USB", "JSON", "HID", "CW", "CCW", "ID", "OK", "PNG", "URL", "CPU", "UI", "PTK", "DPI", "LPI", "EXE", "XML", "API", "BST", "UTC", "MM", "PX", "OSD", "PDF", "RGB", "HEX", "MB", "KB" };

    private static string Where(Visual v)
    {
        var names = new List<string>();
        for (Visual? p = v; p != null && names.Count < 4; p = p.GetVisualParent())
            if (p is StyledElement se && !string.IsNullOrEmpty(se.Name)) names.Add(se.Name!);
        var view = v.GetVisualAncestors().OfType<UserControl>().FirstOrDefault()?.GetType().Name ?? "?";
        return names.Count > 0 ? $"{view}/{string.Join("<", names)}" : view;
    }

    public static IEnumerable<T> Visible<T>(Visual root) where T : Visual =>
        root.GetVisualDescendants().OfType<T>().Where(c => c.IsEffectivelyVisible && c.Bounds.Width > 0 && c.Bounds.Height > 0);

    private static double Lum(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;

    private static Color? SolidOf(IBrush? b) => b is ISolidColorBrush s && s.Opacity > 0.5 && s.Color.A > 128 ? s.Color : null;

    public static void Audit(Visual root, string page)
    {
        foreach (var t in Visible<TextBlock>(root))
        {
            var text = t.Text ?? (t.Inlines != null ? string.Concat(t.Inlines.OfType<Avalonia.Controls.Documents.Run>().Select(r => r.Text)) : "");
            if (string.IsNullOrWhiteSpace(text)) continue;
            CheckWords(text, page, $"text '{Trim(text)}' ({Where(t)})");
            // cut-off: laid-out text wider/taller than the box, or trimmed with an ellipsis
            var lay = t.TextLayout;
            if (lay.TextLines.Any(l => l.HasCollapsed))
                Defect($"[cut-off] {page}: '{Trim(text)}' is trimmed with an ellipsis ({Where(t)}, {t.Bounds.Width:0}px wide)");
            else if (lay.WidthIncludingTrailingWhitespace > t.Bounds.Width + 1.5 && t.TextWrapping == TextWrapping.NoWrap)
                Defect($"[cut-off] {page}: '{Trim(text)}' needs {lay.Width:0}px, has {t.Bounds.Width:0}px ({Where(t)})");
            // clipped by an ancestor
            var clip = t.GetVisualAncestors().OfType<Control>().FirstOrDefault(a => a.ClipToBounds && a is not ScrollContentPresenter && a is not ScrollViewer);
            if (clip != null)
            {
                var tl = t.TranslatePoint(new Point(0, 0), clip);
                if (tl is { } p && (p.X + t.Bounds.Width > clip.Bounds.Width + 2 || p.Y + t.Bounds.Height > clip.Bounds.Height + 2) && p.X < clip.Bounds.Width && p.Y < clip.Bounds.Height)
                    Defect($"[cut-off] {page}: '{Trim(text)}' runs past its clipping parent {clip.GetType().Name} ({Where(t)})");
            }
        }
        foreach (var v in Visible<Visual>(root))
        {
            Color? bright = v switch
            {
                TextBlock tbk => SolidOf(tbk.Foreground),
                Avalonia.Controls.Shapes.Shape sh => SolidOf(sh.Fill) ?? SolidOf(sh.Stroke),
                Avalonia.Controls.Presenters.ContentPresenter cp when cp.Content is string => SolidOf(cp.Foreground),
                _ => null,
            };
            if (bright is { } bc && bc.R > 0xE4 && bc.G > 0xE4 && bc.B > 0xE4 && Lum(bc) > 0.85)
                Defect($"[bright brush] {page}: {v.GetType().Name} '{(v is TextBlock tx ? Trim(tx.Text ?? "") : Label(v as Control ?? new Border()))}' is {bc} ({Where(v)})");
        }
        foreach (var c in root.GetVisualDescendants().OfType<Control>().Where(c => c.IsEffectivelyVisible))
        {
            if (ToolTip.GetTip(c) is string tip) CheckWords(tip, page, $"tooltip '{Trim(tip)}' ({Where(c)})", upper: false);
            var an = Avalonia.Automation.AutomationProperties.GetName(c);
            if (!string.IsNullOrEmpty(an)) CheckWords(an, page, $"accessible name '{Trim(an)}' ({Where(c)})", upper: false);
            if (c is ComboBox cb && cb.ItemsSource != null)
                foreach (var it in cb.ItemsSource) { var s = it?.ToString() ?? ""; if (!s.Contains('.') ) CheckWords(s, page, $"dropdown item '{Trim(s)}' ({Where(c)})", upper: false); }
        }
        foreach (var b in Visible<TemplatedControl>(root).Where(x => x is Button or ToggleButton && x is not ScrollBar))
        {
            if (b.GetVisualAncestors().Any(a => a is ScrollBar or Slider or ComboBox or NumericUpDown && a != b)) continue;
            var cr = b.CornerRadius;
            var presenter = b.GetVisualDescendants().OfType<ContentPresenter>().FirstOrDefault(p => p.Name == "PART_ContentPresenter");
            var bord = b.GetVisualDescendants().OfType<Border>().FirstOrDefault(x => x.Background != null || x.BorderThickness != default);
            var effective = presenter?.CornerRadius ?? bord?.CornerRadius ?? cr;
            var hasFill = (presenter?.Background ?? bord?.Background ?? b.Background) is ISolidColorBrush f && f.Color.A > 40 && f.Opacity > 0.1;
            var label = Label(b);
            if (hasFill && effective.TopLeft < 0.5 && effective.BottomRight < 0.5)
                Defect($"[square corners] {page}: {b.GetType().Name} '{label}' has 0 px corners, rule is 6 px ({Where(b)})");
            else if (hasFill && b is Button or ToggleButton && Math.Abs(effective.TopLeft - 6) > 0.5 && effective.TopLeft < 20)
                Defect($"[corner radius] {page}: {b.GetType().Name} '{label}' has {effective.TopLeft:0.#} px corners, rule is 6 px ({Where(b)})");
            if (b is ToggleButton { IsChecked: true } tg && tg is not CheckBox && tg is not ToggleSwitch)
            {
                var bg = SolidOf(presenter?.Background ?? bord?.Background);
                var fg = SolidOf(presenter?.Foreground ?? tg.Foreground);
                var tb = tg.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(x => x.IsEffectivelyVisible);
                if (tb != null) fg = SolidOf(tb.Foreground) ?? fg;
                if (bg is { } bgc && fg is { } fgc && Lum(fgc) > Lum(bgc) + 0.25)
                    Defect($"[light-on-highlight] {page}: selected '{label}' has light text {fgc} on {bgc}; rule is #141414 on #D8D8D8 ({Where(tg)})");
                if (bg is { } bb && bb.B > bb.R + 40)
                    Defect($"[blue selection] {page}: selected '{label}' fill is {bb} (rule #D8D8D8) ({Where(tg)})");
            }
        }
        foreach (var cb in Visible<CheckBox>(root).Where(c => c.IsChecked == true))
        {
            var glyph = cb.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().FirstOrDefault(p => p.Name == "CheckGlyph");
            var box = cb.GetVisualDescendants().OfType<Border>().FirstOrDefault(p => p.Name == "NormalRectangle");
            var tick = SolidOf(glyph?.Fill); var fill = SolidOf(box?.Background);
            if (tick is { } tc && Lum(tc) > 0.6) Defect($"[white tick] {page}: '{Label(cb)}' tick is {tc} ({Where(cb)})");
            if (fill is { } fc && (fc.B > fc.R + 40 || Lum(fc) < 0.5)) Defect($"[tick box] {page}: '{Label(cb)}' checked box is {fc}, rule light grey #D8D8D8 ({Where(cb)})");
        }
        // alignment: sibling buttons in one horizontal row whose tops differ by >1.5px
        foreach (var panel in Visible<Panel>(root).Where(p => p is StackPanel { Orientation: Avalonia.Layout.Orientation.Horizontal } or WrapPanel))
        {
            var btns = panel.Children.OfType<Button>().Where(x => x.IsEffectivelyVisible).ToList();
            if (btns.Count < 2) continue;
            var tops = btns.Select(x => x.Bounds.Y).ToList(); var hs = btns.Select(x => x.Bounds.Height).ToList();
            if (tops.Max() - tops.Min() > 1.5 && panel is StackPanel)
                Defect($"[misaligned] {page}: buttons {string.Join(", ", btns.Select(Label))} tops differ by {tops.Max() - tops.Min():0.#}px ({Where(panel)})");
            if (hs.Max() - hs.Min() > 1.5 && panel is StackPanel)
                Defect($"[uneven heights] {page}: buttons {string.Join(", ", btns.Select(Label))} heights {string.Join("/", hs.Select(x => x.ToString("0")))} ({Where(panel)})");
        }
    }

    private static void CheckWords(string text, string page, string what, bool upper = true)
    {
        if (text.Contains("Sakura", StringComparison.OrdinalIgnoreCase)) Defect($"[Sakura text] {page}: {what}");
        if (text.Contains("Wacom", StringComparison.OrdinalIgnoreCase)) Defect($"[Wacom text] {page}: {what}");
        if (Regex.IsMatch(text, @"\bCtrl\b")) Defect($"[Windows key name on Mac] {page}: {what}");
        if (!upper) return;
        foreach (Match m in Regex.Matches(text, @"\b[A-Z]{3,}\b"))
            if (!Acronyms.Contains(m.Value)) { Defect($"[uppercase label] {page}: {what}"); break; }
    }

    private static string Trim(string s) { s = s.Replace('\n', ' '); return s.Length > 70 ? s[..70] + "…" : s; }

    public static string Label(Control c)
    {
        string? s = c switch
        {
            ContentControl cc when cc.Content is string str => str,
            ContentControl cc when cc.Content is TextBlock tb => tb.Text,
            _ => null,
        };
        s ??= c.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => !string.IsNullOrWhiteSpace(t.Text))?.Text;
        s ??= Avalonia.Automation.AutomationProperties.GetName(c);
        s ??= ToolTip.GetTip(c) as string;
        s ??= c.Name ?? c.GetType().Name;
        return Trim(s);
    }

    // ── driving ────────────────────────────────────────────────────────────────────────────────────

    public static bool Click(Window win, Control c)
    {
        c.BringIntoView(); Pump(3);
        var pt = c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), win);
        if (pt is not { } p || p.X < 0 || p.Y < 0 || p.X > win.Bounds.Width || p.Y > win.Bounds.Height) return false;
        var hit = win.InputHitTest(p) as Visual;
        bool ok = hit != null && (hit == c || hit.GetVisualAncestors().Contains(c));
        win.MouseMove(p); win.MouseDown(p, MouseButton.Left); win.MouseUp(p, MouseButton.Left); Pump(4);
        return ok;
    }

    private static readonly Regex Danger = new(
        @"\b(stop|restart|start daemon|install|uninstall|remove|clean|purge|kill|quit|browse|choose (file|folder|image)|import|export|reveal|open (folder|log|config|settings folder)|display settings|login|calibrat|start$|record|screenshot|capture|copy|reset all|forget|delete driver|disable driver|run|send|report|launch)",
        RegexOptions.IgnoreCase);

    /// <summary>Click every visible, enabled button/toggle, drive sliders/combos/text boxes, and record what
    /// responded. Buttons whose label suggests a system side effect are listed, not clicked.</summary>
    public static async Task Exercise(Window win, Visual root, string page, Func<Control, bool>? skip = null, int maxClicks = 80)
    {
        int before = Exceptions.Count, clicks = 0;
        var done = new HashSet<Control>();
        var hostWindows = new HashSet<Window>(OpenWindows());
        for (int round = 0; round < 3 && clicks < maxClicks; round++)
        {
            var targets = Visible<Control>(root).Where(x => x.IsEffectivelyEnabled && !done.Contains(x) &&
                x is Button or ToggleButton or CheckBox or ToggleSwitch && x is not RepeatButton
                && !x.GetVisualAncestors().Any(a => a is ScrollBar or ComboBox or Slider or NumericUpDown or Calendar)).ToList();
            if (targets.Count == 0) break;
            foreach (var t in targets)
            {
                if (clicks >= maxClicks) break;
                if (!t.IsEffectivelyVisible || !t.IsEffectivelyEnabled) continue;
                done.Add(t);
                var label = Label(t);
                if (skip?.Invoke(t) == true || Danger.IsMatch(label) || (t is Button bt && bt.Command?.GetType().Name.Contains("Url") == true))
                { Skip($"{page}: '{label}' not clicked (system side effect or external link)"); continue; }
                var wasChecked = (t as ToggleButton)?.IsChecked;
                var hitOk = Click(win, t); clicks++;
                await Settle(30);
                if (!hitOk) Defect($"[not clickable] {page}: '{label}' is covered by another element or off-screen at its centre ({t.GetType().Name})");
                if (t is ToggleButton tb && tb is not RadioButton && wasChecked == tb.IsChecked && hitOk)
                    Defect($"[no response] {page}: toggle '{label}' did not change when clicked");
                foreach (var dlg in OpenWindows().Where(w => !hostWindows.Contains(w)).ToList())
                {
                    Shot(dlg, $"dialog-{page}-{label}");
                    Audit(dlg, $"dialog '{dlg.Title}' from {page} › {label}");
                    try { if (dlg is Window d) d.Close(); } catch (Exception ex) { Exceptions.Add($"closing dialog {dlg.Title}: {ex.Message}"); }
                    Pump();
                }
                if (t is ToggleButton tb2 and not RadioButton && tb2.IsChecked != wasChecked && tb2.IsEffectivelyVisible) { Click(win, tb2); await Settle(20); }
            }
        }
        foreach (var s in Visible<Slider>(root).Where(s => s.IsEffectivelyEnabled).ToList())
        {
            var old = s.Value; var target = s.Minimum + (s.Maximum - s.Minimum) * 0.7;
            s.Value = target; await Settle(20);
            if (Math.Abs(s.Value - target) > (s.Maximum - s.Minimum) * 0.06 && !s.IsSnapToTickEnabled) Defect($"[slider] {page}: slider {Where(s)} jumped from {target:0.###} to {s.Value:0.###}");
            s.Value = old; await Settle(10);
        }
        foreach (var cb in Visible<ComboBox>(root).Where(c => c.IsEffectivelyEnabled).ToList())
        {
            var old = cb.SelectedIndex; int n = cb.ItemCount;
            for (int i = 0; i < Math.Min(n, 12); i++) { cb.SelectedIndex = i; await Settle(10); }
            if (n > 0) cb.SelectedIndex = old; await Settle(10);
            if (n == 0) Defect($"[empty dropdown] {page}: {Where(cb)} has no items");
        }
        foreach (var nud in Visible<NumericUpDown>(root).Where(n => n.IsEffectivelyEnabled).ToList())
        {
            var old = nud.Value; var target = (old ?? 0) + 1;
            nud.Value = target; await Settle(20);
            if (nud.Value != target && nud.Value != nud.Maximum) Defect($"[number box] {page}: {Where(nud)} set to {target} reads back {nud.Value}");
            nud.Value = old; await Settle(10);
        }
        foreach (var tbx in Visible<TextBox>(root).Where(t => t.GetVisualAncestors().OfType<NumericUpDown>().Any()).Take(0)) { }
        foreach (var tbx in Visible<TextBox>(root).Where(t => !t.GetVisualAncestors().OfType<NumericUpDown>().Any()).Where(t => t.IsEffectivelyEnabled && !t.IsReadOnly).ToList())
        {
            var old = tbx.Text; tbx.Text = (old ?? "") + "7"; await Settle(10); tbx.Text = old; await Settle(10);
        }
        if (Exceptions.Count > before) foreach (var e in Exceptions.Skip(before)) Defect($"[exception] {page}: {e}");
        Pass($"{page}: {clicks} controls clicked; sliders/dropdowns/text boxes driven");
    }

    private static readonly List<Window> Open = new();
    public static IEnumerable<Window> OpenWindows() { lock (Open) return Open.Where(w => w.IsVisible).ToList(); }

    public static void WriteReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Sweep report ({DateTime.Now:yyyy-MM-dd HH:mm})\n");
        sb.AppendLine($"## Defects ({Defects.Count})"); foreach (var d in Defects.OrderBy(x => x)) sb.AppendLine("- " + d);
        sb.AppendLine($"\n## Passed ({Passes.Count})"); foreach (var d in Passes) sb.AppendLine("- " + d);
        sb.AppendLine($"\n## Not clicked ({Skipped.Count})"); foreach (var d in Skipped) sb.AppendLine("- " + d);
        sb.AppendLine($"\n## Screenshots ({ShotFiles.Count})"); foreach (var d in ShotFiles) sb.AppendLine("- " + d);
        File.WriteAllText(Path.Combine(Shots, "sweep-report.md"), sb.ToString());
    }
}
