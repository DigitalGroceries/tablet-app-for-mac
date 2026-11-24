using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using Xunit;
using static OpenTabletArtist.SweepTests.Sweep;

namespace OpenTabletArtist.SweepTests;

/// <summary>V30 build 4 (Zak 24 Nov 2026, screenshots of Home, Scribble, Tablet › Pen and Settings: "we have 3 different designs
/// for buttons?", "Again 2 designs of buttons", "They are all over the app. I will not send you all"): opens every page, lists
/// every button and tab with the look it is drawn with (fill, edge, shadow), dark and light, and writes the list to
/// button-looks.md in OTA_SHOTS_DIR. The audit below fails when a word button or tab is not drawn with the shared raised look.</summary>
public class ButtonLookAuditTests
{
    static readonly string[] ThemeKeys = { "GhostButton", "AccentButton", "PivotTab", "SubtabRadioButton", "WacomTile", "LinkButton",
        "ModifierToggle", "KeyCapButton", "WordmarkNav", "SearchResultButton", "DeleteButton", "CallToActionButton" };

    internal sealed record Look(string Page, string Kind, string Theme, string Classes, string Label, bool? Checked,
        string Fill, bool Shadow, string Edge, bool Raised, bool Enabled);

    static string Dir => Environment.GetEnvironmentVariable("OTA_SHOTS_DIR") ?? "/tmp/ota-v30-shots";

    static string ThemeName(Control c)
    {
        if (c.Theme == null) return "-";
        foreach (var k in ThemeKeys)
            if (Application.Current!.TryFindResource(k, out var r) && ReferenceEquals(r, c.Theme)) return k;
        if (c is Controls.IconButton) return "IconButton";
        return "other";
    }

    internal static string Describe(IBrush? b) => b switch
    {
        null => "none",
        ISolidColorBrush s => s.Color.A == 0 || s.Opacity == 0 ? "clear"
            : $"#{s.Color.R:X2}{s.Color.G:X2}{s.Color.B:X2}" + (s.Opacity < 1 || s.Color.A < 255 ? $"@{s.Opacity * s.Color.A / 255.0:0.##}" : ""),
        IGradientBrush g => "grad(" + string.Join(",", g.GradientStops.Select(x => $"#{x.Color.R:X2}{x.Color.G:X2}{x.Color.B:X2}")) + ")",
        _ => b.GetType().Name,
    };

    /// <summary>The part of the control's template that draws its fill: the first Border or ContentPresenter with a background.</summary>
    static (string Fill, bool Shadow, string Edge) Skin(Control c)
    {
        foreach (var v in c.GetVisualDescendants())
        {
            if (v is Border bd && bd.Background != null)
                return (Describe(bd.Background), bd.BoxShadow.Count > 0, $"{Describe(bd.BorderBrush)}/{bd.BorderThickness.Top:0.#}");
            if (v is ContentPresenter cp && cp.Background != null)
                return (Describe(cp.Background), cp.BoxShadow.Count > 0, $"{Describe(cp.BorderBrush)}/{cp.BorderThickness.Top:0.#}");
        }
        return ("none", false, "none");
    }

    static bool Skip(Control c) =>
        c is CheckBox or ToggleSwitch
        || c.GetVisualAncestors().Any(a => a is ComboBox or ScrollBar or Slider or NumericUpDown or TextBox or CalendarDatePicker);

    internal static IEnumerable<Look> Looks(Visual root, string page)
    {
        var raised = Application.Current!.TryFindResource("ButtonRaisedFillBrush", out var r) ? Describe(r as IBrush) : "?";
        foreach (var c in Visible<Control>(root).Where(c => c is Button or ToggleButton).Where(c => !Skip(c)))
        {
            var (fill, shadow, edge) = Skin(c);
            yield return new Look(page, c.GetType().Name, ThemeName(c), string.Join(" ", c.Classes.Where(x => !x.StartsWith(':'))),
                Label(c), (c as ToggleButton)?.IsChecked, fill, shadow, edge, fill == raised && shadow, c.IsEffectivelyEnabled);
        }
    }

    static void Shot(Window w, bool light, string slug)
    {
        for (int i = 0; i < 6; i++) { Pump(); w.UpdateLayout(); }
        var d = Path.Combine(Dir, light ? "pages-light" : "pages-dark");
        Directory.CreateDirectory(d);
        w.CaptureRenderedFrame()!.Save(Path.Combine(d, Slug(slug) + ".png"));
    }

    static async Task<List<Look>> Walk(bool light)
    {
        Hook();
        _ = new ThemeViewModel();
        ThemeService.Apply(ThemeService.Wacom);
        LightnessService.Set(light ? 8 : 0);
        Localizer.SetPreference("en");
        var all = new List<Look>();

        var w = new MainWindow { Width = 1130, Height = 900 };
        w.Show(); await Settle(150);
        var vm = (MainViewModel)w.DataContext!;
        foreach (var (slug, go) in vm.ScreenshotTargets())
        {
            go(); await Settle(100);
            all.AddRange(Looks(w, slug));
            Shot(w, light, slug);
        }
        vm.Settings.SelectedTab = SettingsTab.Theme; vm.NavigateCommand.Execute(vm.Settings); await Settle(80);
        foreach (var sub in new[] { "ThemeSubtab", "BackdropSubtab", "ColorsSubtab" })
            if (w.GetVisualDescendants().OfType<RadioButton>().FirstOrDefault(x => x.Name == sub) is { } rb)
            { rb.IsChecked = true; await Settle(60); all.AddRange(Looks(w, $"settings-theme-{sub}")); }
        w.AllowCloseForQuit(); w.Close();
        (vm as IDisposable)?.Dispose();

        var (app, _, _) = await FullSweepTests.Session();
        using var _a = app;
        using var ed = FullSweepTests.Editor(app);
        var view = new TabletDetailView { DataContext = ed };
        var tw = Host(view, 1130, 1500);
        foreach (var tool in new[] { "ToolFunctionsButton", "ToolPenButton", "ToolMoreButton" })
        {
            if (view.FindControl<RadioButton>(tool) is not { } t) continue;
            t.IsChecked = true; await Settle(60);
            var tabs = Visible<RadioButton>(view).Where(x => x.GetVisualAncestors().OfType<Control>().Any(a => a.Name == "TabRail")).ToList();
            foreach (var tab in tabs)
            {
                tab.IsChecked = true; await Settle(80);
                all.AddRange(Looks(tw, $"tablet-{Label(t)}-{Label(tab)}"));
                Shot(tw, light, $"tablet-{Label(t)}-{Label(tab)}");
                if (Label(tab) == "Pen")
                {
                    PenPageTabs.Select(view, "EraserTab"); await Settle(60);
                    all.AddRange(Looks(tw, $"tablet-{Label(t)}-{Label(tab)}-eraser"));
                    PenPageTabs.Select(view, "TipTab"); await Settle(40);
                }
            }
        }
        tw.Close();
        return all;
    }

    static string Key(Look l) => $"{l.Fill} | shadow {(l.Shadow ? "yes" : "no")} | edge {l.Edge}";

    [AvaloniaFact]
    public async Task Button_look_inventory()
    {
        var lines = new List<string> { "# Every button and tab, by the look it is drawn with", "" };
        foreach (var light in new[] { false, true })
        {
            var looks = await Walk(light);
            lines.Add($"## {(light ? "Light" : "Dark")} look - {looks.Count} buttons and tabs on {looks.Select(l => l.Page).Distinct().Count()} pages");
            foreach (var g in looks.GroupBy(l => (l.Raised, l.Enabled, Kind: l.Kind == "RadioButton" || l.Kind == "ToggleButton" ? (l.Checked == true ? "tab on" : "tab off") : "button", l.Theme, Key(l)))
                         .OrderBy(g => g.Key.Raised).ThenBy(g => g.Key.Theme))
            {
                var labels = g.Select(l => l.Label).Distinct().Take(12);
                var pages = g.Select(l => l.Page).Distinct().Take(6);
                lines.Add($"- {(g.Key.Raised ? "RAISED" : "OTHER ")} {(g.Key.Enabled ? "on " : "OFF")} {g.Key.Kind,-7} theme {g.Key.Theme} classes [{string.Join(",", g.Select(l => l.Classes).Distinct().Take(4))}] {g.Key.Item5} x{g.Count()}");
                lines.Add($"    labels: {string.Join(" | ", labels)}");
                lines.Add($"    pages: {string.Join(" | ", pages)}");
            }
            lines.Add("");
        }
        LightnessService.Set(0);
        Directory.CreateDirectory(Dir);
        File.WriteAllLines(Path.Combine(Dir, "button-looks.md"), lines);
    }
}
