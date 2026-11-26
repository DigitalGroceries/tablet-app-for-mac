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

/// <summary>V30 build 4 (Digital Groceries 24 Nov 2026, screenshots of Home, Scribble, Tablet › Pen and Settings: "we have 3 different designs
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
            if (c.TryFindResource(k, out var r) && ReferenceEquals(r, c.Theme)) return k;   // the window's own themes too (ModifierToggle)
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

    static void Shot(Window w, string look, string slug)
    {
        for (int i = 0; i < 2; i++) { Pump(2); w.UpdateLayout(); }
        var d = Path.Combine(Dir, "pages-" + look);
        Directory.CreateDirectory(d);
        w.CaptureRenderedFrame()!.Save(Path.Combine(d, Slug(slug) + ".png"));
    }

    /// <summary>What one walk through the app saw: every button and tab with its look, every word with its measured contrast
    /// (<see cref="ReadabilityAudit"/>), and the fill of the tab you are on in that look.</summary>
    internal sealed record Seen(List<Look> Looks, List<ReadabilityAudit.Word> Words, string TabOn, List<string> SliderFaults, int Sliders);

    /// <summary>The looks the audits walk through: the grey look dark and light, and (V31, task list L7.3 / L7.12, Digital Groceries 25 Nov 2026:
    /// in Burgundy, Custom and Rose "enabled tabs and buttons lose their raised button look") each skin at the step it starts on.
    /// OTA_AUDIT_LOOKS=rose,custom walks only those (to repeat one look while fixing it).</summary>
    internal static IEnumerable<(string Theme, int Step, string Name)> AuditLooks()
    {
        var all = new[]
        {
            (ThemeService.Wacom, 0, "dark"), (ThemeService.Wacom, 8, "light"),
            (ThemeService.DarkSakura, LightnessService.DefaultStepFor(ThemeService.DarkSakura), "burgundy"),
            (ThemeService.Anime, LightnessService.DefaultStepFor(ThemeService.Anime), "rose"),
            (ThemeService.Custom, LightnessService.DefaultStepFor(ThemeService.Custom), "custom"),
        };
        var only = (Environment.GetEnvironmentVariable("OTA_AUDIT_LOOKS") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return only.Length == 0 ? all : all.Where(l => only.Contains(l.Item3));
    }

    static async Task<Seen> Walk(string theme, int step, string look)
    {
        using var mac = UiPlatform.PretendMac();   // pages laid out as on Digital Groceries' Mac (the pen cards' Change… / Default)
        Hook();
        V31ThemeShots.Choose(theme);
        var all = new List<Look>();
        var words = new List<ReadabilityAudit.Word>();
        // V31 (task list L7.23): the mockup's slider on every page, window and menu - a sunken recess with a groove, a handle
        // with a bright line, and notches
        var sliderFaults = new List<string>(); int sliders = 0;
        void See(Window win, string slug)
        {
            all.AddRange(Looks(win, slug));
            words.AddRange(ReadabilityAudit.Measure(win, $"{look} {slug}"));
            foreach (var sl in Visible<Slider>(win))
            {
                sliders++;
                string name = sl.Name ?? Avalonia.Automation.AutomationProperties.GetName(sl) ?? "(no name)";
                var parts = sl.GetVisualDescendants().OfType<Border>().Select(b => b.Name).ToHashSet();
                foreach (var part in new[] { "SliderRim", "SliderGroove", "HandleBody", "HandleLine" })
                    if (!parts.Contains(part)) sliderFaults.Add($"{look} {slug}: slider '{name}' has no {part}");
                if (sl.TickPlacement == TickPlacement.None) sliderFaults.Add($"{look} {slug}: slider '{name}' has no notches");
            }
        }

        var w = new MainWindow { Width = 1130, Height = 900 };
        w.Show(); await Settle(150);
        var vm = (MainViewModel)w.DataContext!;
        vm.Theme.SelectedTheme = vm.Theme.ThemeOptions.First(o => o.Id == theme); await Settle(60);
        vm.Theme.Lightness = step; await Settle(60);
        var tabOn = Application.Current!.TryFindResource("TabOnFillBrush", out var on) ? Describe(on as IBrush) : "?";
        foreach (var (slug, go) in vm.ScreenshotTargets())
        {
            go(); await Settle(100);
            See(w, slug);
            Shot(w, look, slug);
        }
        vm.Settings.SelectedTab = SettingsTab.Theme; vm.NavigateCommand.Execute(vm.Settings); await Settle(80);
        foreach (var sub in new[] { "ThemeSubtab", "BackdropSubtab", "ColorsSubtab" })
            if (w.GetVisualDescendants().OfType<RadioButton>().FirstOrDefault(x => x.Name == sub) is { } rb)
            { rb.IsChecked = true; await Settle(60); See(w, $"settings-theme-{sub}"); }
        // The Tablet pages inside the app window, as Digital Groceries sees them (Digital Groceries 24 Nov ~15:50: "Dont create mockup pictures that are different
        // from the app"): the nav row, the tablet switcher and a connected Wacom PTK-440 with factory settings (DigitalGroceriesTablet).
        var details = (System.Collections.IDictionary)typeof(MainViewModel)
            .GetField("_tabletDetails", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(vm)!;
        var ed = DigitalGroceriesTablet.Page();
        ed.ShowHeader = false;
        details[DigitalGroceriesTablet.Name] = ed;
        vm.TabletPage.SetTablets(new[] { (DigitalGroceriesTablet.Name, true) });
        vm.CurrentPage = vm.TabletPage;
        w.Height = 1500;
        await Settle(150);
        // This test window has no driver, so the app greys the page and the Save row (MainViewModel.PageInputEnabled,
        // CanEditSettings). Digital Groceries' app has one: for pictures that match it, they take input here as they do on his Mac.
        foreach (var host in w.GetVisualDescendants().OfType<TransitioningContentControl>()) host.IsEnabled = true;
        foreach (var name in new[] { "SaveButton", "SaveAsDefaultButton", "RevertToDefaultButton", "FactoryButton", "RestoreFactoryButton" })
            foreach (var b in w.GetVisualDescendants().OfType<Button>().Where(x => x.Name == name)) b.IsEnabled = true;
        foreach (var p in w.GetVisualDescendants().OfType<StackPanel>().Where(x => x.Children.OfType<Button>().Any(b => b.Name == "SaveButton"))) p.IsEnabled = true;
        await Settle(60);
        var view = w.GetVisualDescendants().OfType<TabletDetailView>().First();
        foreach (var tool in new[] { "ToolFunctionsButton", "ToolPenButton", "ToolMoreButton" })
        {
            if (view.FindControl<RadioButton>(tool) is not { } t) continue;
            t.IsChecked = true; await Settle(60);
            var tabs = Visible<RadioButton>(view).Where(x => x.GetVisualAncestors().OfType<Control>().Any(a => a.Name == "TabRail")).ToList();
            foreach (var tab in tabs)
            {
                tab.IsChecked = true; await Settle(80);
                See(w, $"tablet-{Label(t)}-{Label(tab)}");
                Shot(w, look, $"tablet-{Label(t)}-{Label(tab)}");
                if (Label(tab) == "Pen")
                {
                    PenPageTabs.Select(view, "EraserTab"); await Settle(60);
                    See(w, $"tablet-{Label(t)}-{Label(tab)}-eraser");
                    PenPageTabs.Select(view, "TipTab"); await Settle(40);
                }
            }
        }
        // V31 (task list L7.6, Digital Groceries 25 Nov 2026: in Rose the words of the 'Restore Factory Settings' button were almost invisible):
        // the app's own question, opened inside the app window as the app opens it, with the words MainViewModel asks it with.
        Helpers.InWindowSheet.MainOverride = () => w;
        try
        {
            var asked = Helpers.Dialogs.ShowConfirmAsync(
                Localizer.Te("save.factory.confirm_title", "Restore factory settings?"),
                Localizer.Te("save.factory.confirm_body",
                    "Your current settings will be replaced with the factory settings. "
                    + "Your own saved default is kept, so you can still use Revert to default afterwards."),
                Localizer.Te("save.factory.confirm_action", "Restore Factory Settings"),
                Localizer.Te("app.cancel", "Cancel"),
                parent: w, cancelIsDefault: true);
            await Settle(200);
            Assert.True(Helpers.InWindowSheet.IsOpen, "the 'Restore factory settings?' question did not open inside the window");
            See(w, "sheet-restore-factory");
            Shot(w, look, "sheet-restore-factory");
            var cancel = Visible<Button>(w).FirstOrDefault(b => Label(b) == Localizer.Te("app.cancel", "Cancel"));
            Assert.True(cancel != null, "no Cancel button in the 'Restore factory settings?' question");
            cancel!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await Settle(150);
            Assert.False(await asked.WaitAsync(TimeSpan.FromSeconds(5)), "Cancel did not answer No");
        }
        finally { Helpers.InWindowSheet.MainOverride = null; }

        w.AllowCloseForQuit(); w.Close();
        (vm as IDisposable)?.Dispose();
        ed.Dispose();

        // V30 build 5 (Digital Groceries 24 Nov 17:3x, his screenshot of Button 1's Change… window: "Keyboard, Media, Mouse, Scroll and Radial menu
        // have no buttons yet"): the check opened every page but no window, so it missed that. The app's windows, each audited too.
        async Task Dialog(Window d, string slug, Action? each = null)
        {
            d.Show(); await Settle(80);
            See(d, slug); Shot(d, look, slug);
            d.Close(); await Settle(20);
        }
        foreach (var (tab, set) in new (string, Action<BindingEditorViewModel>)[]
                 {
                     ("keyboard", v => v.IsKeyboardTab = true), ("media", v => v.IsMediaTab = true), ("mouse", v => v.IsMouseTab = true),
                     ("scroll", v => v.IsScrollTab = true), ("radial", v => v.IsRadialTab = true),
                 })
        {
            var bvm = new BindingEditorViewModel(AuxBinding.Unbound, "Button 1");
            set(bvm);
            await Dialog(new BindingEditorDialog(bvm) { Width = 900, Height = 600 }, $"dialog-change-{tab}");
        }
        await Dialog(new RadialMenuEditorDialog(new RadialMenuEditorViewModel(null, "Radial menu")) { Width = 900, Height = 760 }, "dialog-radial-editor");
        await Dialog(new MenuButtonChoiceDialog("Which pen button opens the radial menu?", "Press that button and the radial menu opens where the pen is.",
            new[] { "Pen button 1", "Pen button 2" }), "dialog-menu-button");
        await Dialog(new PaletteEditorDialog(RadialMenuJson.Serialize(RadialMenuDefaults.Create(Localizer.Te))), "dialog-palette-editor");   // V30 build 5
        await Dialog(new AutoStartDialog(), "dialog-autostart");
        await Dialog(new SupportedTabletsDialog { Width = 900, Height = 700 }, "dialog-supported-tablets");
        await Dialog(new HotkeyCaptureDialog(new HotkeyCaptureViewModel()) { Width = 900, Height = 600 }, "dialog-hotkey");
        await Dialog(new ReportWindow(new ReportViewModel(() => (new List<string>(), Array.Empty<string>(), null), null, "shot")) { Width = 900, Height = 800 }, "dialog-report");
        return new Seen(all, words, tabOn, sliderFaults, sliders);
    }

    static string Key(Look l) => $"{l.Fill} | shadow {(l.Shadow ? "yes" : "no")} | edge {l.Edge}";

    /// <summary>Not drawn as a raised word button on purpose: icon glyphs and text links (no fill), the touch ring's centre picture, the
    /// Wacom-style picture tiles (tablet, tool, application), keyboard key caps, the red Forget / Delete buttons.</summary>
    static bool OwnLookOnPurpose(Look l) =>
        l.Fill is "clear" or "none"
        || l.Theme is "LinkButton" or "IconButton" or "KeyCapButton" or "ModifierToggle" or "DeleteButton" or "WacomTile"
        || (l.Theme == "other" && l.Kind == "RadioButton")
        || l.Classes.Split(' ').Any(c => c is "ringCentre" or "flat" or "pathLink" or "dangerText");

    [AvaloniaFact]
    public async Task Button_look_inventory()
    {
        var lines = new List<string> { "# Every button and tab, by the look it is drawn with", "" };
        var wordLines = new List<string> { "# Every word on every page and window, measured from the pixels (the weakest of each page)", "" };
        var odd = new List<string>();
        var low = new List<string>();
        try
        {
            foreach (var (theme, step, look) in AuditLooks())
            {
                var seen = await Walk(theme, step, look);
                var looks = seen.Looks;
                // Digital Groceries 24 Nov ("They are all over the app"): every enabled word button and tab is raised; the tab you are on is lit.
                foreach (var l in looks.Where(l => l.Enabled && !l.Raised && !OwnLookOnPurpose(l)))
                    if (!(l.Checked == true && l.Fill == seen.TabOn && l.Shadow))
                        odd.Add($"{look} {l.Page}: {l.Kind} '{l.Label}' theme {l.Theme} [{l.Classes}] {l.Fill} shadow {l.Shadow}");
                lines.Add($"## {look} - {looks.Count} buttons and tabs on {looks.Select(l => l.Page).Distinct().Count()} pages");
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

                // V31 (TASKS #216): Home's "Open driver page" is a real button, not a small link
                var driverPage = looks.Where(l => l.Label == "Open driver page").ToList();
                Assert.True(driverPage.Count > 0, $"{look}: Home's 'Open driver page' button was not seen");
                Assert.All(driverPage, l => Assert.True(l.Raised, $"{look} {l.Page}: 'Open driver page' has no button look (theme {l.Theme})"));
                Assert.True(seen.Sliders >= 15, $"{look}: only {seen.Sliders} sliders were seen - were the pages drawn?");
                Assert.True(seen.SliderFaults.Count == 0, "V31 (L7.23): a slider without the mockup's look:\n" + string.Join("\n", seen.SliderFaults.Distinct()));
                // V31 (L7.12): and every word on every page and window can be read (a word on a button or tab 4.5:1, any other 3:1)
                Assert.True(seen.Words.Count > 500, $"{look}: only {seen.Words.Count} words were measured - were the pages drawn?");
                low.AddRange(seen.Words.Where(x => !x.Ok).Select(x => x.ToString()));
                wordLines.Add($"## {look} - {seen.Words.Count} words on {seen.Words.Select(x => x.Page).Distinct().Count()} pages and windows, {seen.Words.Count(x => !x.Ok)} under their line");
                foreach (var g in seen.Words.GroupBy(x => x.Page))
                {
                    var weakest = g.OrderBy(x => x.Contrast / x.Needed).First();
                    wordLines.Add($"- {g.Key}: {g.Count()} words, weakest {weakest.Contrast:0.00}:1 (needs {weakest.Needed:0.0}) '{weakest.Text}'");
                }
                wordLines.Add("");
            }
        }
        finally { V31ThemeShots.Choose(ThemeService.Wacom); LightnessService.Set(0); }
        Directory.CreateDirectory(Dir);
        File.WriteAllLines(Path.Combine(Dir, "button-looks.md"), lines);
        wordLines.AddRange(low.Distinct().Select(x => "LOW " + x));
        File.WriteAllLines(Path.Combine(Dir, "words-every-page.md"), wordLines);
        Assert.True(odd.Count == 0, "Not drawn with the shared raised look:\n" + string.Join("\n", odd.Distinct().Take(40)));
        Assert.True(low.Count == 0, $"{low.Distinct().Count()} words cannot be read well enough:\n" + string.Join("\n", low.Distinct().Take(60)));
    }
}
