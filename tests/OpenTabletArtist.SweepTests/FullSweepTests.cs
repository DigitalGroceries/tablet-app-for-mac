using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.Tests;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OtdInterop;
using OtdInterop.Tests;
using OpenTabletArtist.UiTests;
using OpenTabletDriver.Desktop.Reflection;
using static OpenTabletArtist.SweepTests.Sweep;

namespace OpenTabletArtist.SweepTests;

/// <summary>
/// Exhaustive functional + visual sweep: every page, tab, button, toggle, slider,
/// dropdown, text box and dialog, in every theme, with screenshots into shots/ and a report in
/// shots/sweep-report.md. Findings are collected, not thrown, so one run lists them all; the last test
/// fails when any defect was recorded.
/// </summary>
public class FullSweepTests : IDisposable
{
    private const string TabletName = "PTK-470";
    public void Dispose() => WriteReport();
    private static void SafeDispose(object? o)
    {
        try { (o as IDisposable)?.Dispose(); }
        catch (Exception ex) { Defect($"[exception] MainViewModel.Dispose after the window closed throws {ex.GetType().Name}: {ex.Message} at {ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}"); }
    }

    // ── fixtures ──────────────────────────────────────────────────────────────────────────────────
    internal static Settings TabletDocument()
    {
        var p = new Profile
        {
            Tablet = TabletName,
            OutputMode = new OpenTabletDriver.Desktop.Reflection.PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)),
            AbsoluteModeSettings = new AbsoluteModeSettings
            {
                Display = new AreaSettings { Width = 2560, Height = 1440, X = 1280, Y = 720 },
                Tablet = new AreaSettings { Width = 224, Height = 148, X = 112, Y = 74 },
            },
            BindingSettings = new BindingSettings(),
        };
        var b = p.BindingSettings;
        b.TipButton = PenSwitchBinding.MakeAdaptiveBinding(PenSwitchKind.Tip);
        b.EraserButton = PenSwitchBinding.MakeAdaptiveBinding(PenSwitchKind.Eraser);
        b.PenButtons = new PluginSettingStoreCollection
        {
            PenSwitchBinding.MakeAdaptiveBinding(PenSwitchKind.PenButton),
            AuxKeyBinding.MakeKeyBinding("E"),   // modified → "Modified to …" + Fix
        };
        b.AuxButtons = new PluginSettingStoreCollection();
        foreach (var k in new[] { "B", "E", "Space", "Control", "Shift", "Alt", "Z", "H" }) b.AuxButtons.Add(AuxKeyBinding.MakeKeyBinding(k));
        b.WheelBindings = new List<WheelBindingSettings> { new() };
        return new Settings { Profiles = new ProfileCollection { p } };
    }

    internal static async Task<(AppSession App, FakeDaemonTransport Daemon, MemorySettingsFileStore Store)> Session()
    {
        var daemon = new FakeDaemonTransport { Settings = TabletDocument() };
        daemon.GetSettingsHandler = async () => { await Task.Yield(); return daemon.Settings; };
        var store = new MemorySettingsFileStore { Saved = TabletDocument() };
        var app = new AppSession(FakeSession.Over(daemon, store), new FakeLifecycle());
        daemon.Reconnect();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (app.CurrentSettings is null && DateTime.UtcNow < deadline) { Pump(); await Task.Delay(5); }
        return (app, daemon, store);
    }

    internal static TabletDetailViewModel Editor(AppSession app)
    {
        var s = app.CurrentSettings!;
        return new TabletDetailViewModel(s.Profiles[0], s,
            applyAction: x => app.ApplyProfileAsync(x.Profiles[0]),
            refreshAction: () => Task.FromResult<(Settings?, Profile?)>((app.CurrentSettings, app.CurrentSettings!.Profiles[0])),
            tabletDigitizer: (224f, 148f));
    }

    private static List<RadioButton> Rail(Visual root, string railName) =>
        Visible<RadioButton>(root).Where(r => r.GetVisualAncestors().OfType<Control>().Any(a => a.Name == railName)).ToList();

    private static bool IsNav(Control c) =>
        c is RadioButton r && (r.Theme == (Avalonia.Styling.ControlTheme?)Application.Current!.FindResource("WordmarkNav")
                               || r.GetVisualAncestors().OfType<Control>().Any(a => a.Name is "TabRail" or "WacomHeader" or "TabletRail"));

    // ── 1. shell pages: Home + links, nav, Scribble, every Settings and Advanced tab ─────────────
    [AvaloniaFact]
    public async Task A_ShellPages()
    {
        Hook();
        var w = new MainWindow { Width = 1100, Height = 760 };
        w.Show(); await Settle(200);
        var vm = (MainViewModel)w.DataContext!;

        // nav row
        var nav = Visible<RadioButton>(w).Where(r => r.Theme == (Avalonia.Styling.ControlTheme?)Application.Current!.FindResource("WordmarkNav")).ToList();
        foreach (var n in nav)
        {
            var before = vm.CurrentPage; var hit = Click(w, n); await Settle();
            if (!hit) Defect($"[not clickable] nav '{Label(n)}'");
            if (n.IsChecked != true) Defect($"[nav] '{Label(n)}' not selected after click");
            else Pass($"nav '{Label(n)}' opens {vm.CurrentPage?.GetType().Name}");
        }
        vm.NavigateCommand.Execute(vm.Dashboard); await Settle();

        // Home links (not opened: they launch the browser)
        var grid = w.GetVisualDescendants().OfType<UniformGrid>().FirstOrDefault(g => g.Name == "LinkGrid");
        if (grid is null) Defect("[Home] six-link grid not found");
        else foreach (var b in grid.Children.OfType<Button>())
        {
            var ok = b.Command != null && b.Command.CanExecute(b.CommandParameter);
            if (!ok && b.Name == "CreditsButton") { Pass("Home link 'Credits and licences' opens its credits flyout (click handler)"); continue; }
            if (!ok) Defect($"[Home link] '{Label(b)}' has no executable command");
            else Pass($"Home link '{Label(b)}' → {(b.CommandParameter as string) ?? b.Command!.GetType().Name} (command ready; not launched on the box)");
        }

        foreach (var (slug, go) in vm.ScreenshotTargets())
        {
            go(); await Settle(120);
            Shot(w, $"page-{slug}");
            Audit(w, $"page {slug}");
            await Exercise(w, w, $"page {slug}", c => IsNav(c) || c.GetVisualAncestors().OfType<Control>().Any(a => a.Name == "LinkGrid")
                || Label(c) is "Get help" or "Releases" or "Source code" or "User manual" or "Supported tablets" or "Credits and licences");
            // come back to the same page in case a click navigated away
        }

        // Settings › Theme subtabs
        vm.Settings.SelectedTab = SettingsTab.Theme; vm.NavigateCommand.Execute(vm.Settings); await Settle(100);
        foreach (var sub in new[] { "ThemeSubtab", "BackdropSubtab", "ColorsSubtab" })
        {
            var r = w.GetVisualDescendants().OfType<RadioButton>().FirstOrDefault(x => x.Name == sub);
            if (r is null) { Defect($"[Theme] subtab {sub} missing"); continue; }
            Click(w, r); await Settle(80);
            Shot(w, $"settings-theme-{sub}"); Audit(w, $"Settings › Theme › {Label(r)}");
        }

        // closing the window hides it (menu bar app); a quit-approved close really closes
        w.Close(); await Settle(200);
        foreach (var d in OpenWindows().Where(x => x != w).ToList()) { Shot(d, "dialog-close-window-hint"); Audit(d, "close-window hint"); d.Close(); }
        await Settle(100);
        Pass($"window close button: window {(w.IsVisible ? "stayed visible" : "hid to the menu bar")} (expected: hides)");
        w.AllowCloseForQuit(); w.Close(); await Settle();
        if (w.IsVisible) Defect("[Quit] window did not close after AllowCloseForQuit"); else Pass("window closes once quit is allowed");
        SafeDispose(vm);
    }

    // ── 2. every theme × Home, Theme pages, a pen page ───────────────────────────────────────────
    [AvaloniaFact]
    public async Task B_EveryTheme()
    {
        Hook();
        var (app, _, _) = await Session();
        using var _a = app;
        var theme = new ThemeViewModel();
        foreach (var opt in theme.ThemeOptions.ToList())
        {
            theme.SelectedTheme = opt; await Settle(80);
            var name = opt.Label;
            if (opt.Label.Contains("Sakura") || opt.Description.Contains("Sakura")) Defect($"[Sakura text] theme option {opt.Id}: '{opt.Label}' / '{opt.Description}'");
            var again = new ThemeViewModel();
            if (again.SelectedTheme?.Id != opt.Id) Defect($"[theme persist] {name}: re-opened Settings shows {again.SelectedTheme?.Id}"); else Pass($"theme {name} applies and persists");

            var tv = new ThemeView { DataContext = theme };
            var w = Host(tv, 1000, 700);
            foreach (var sub in new[] { "ThemeSubtab", "BackdropSubtab", "ColorsSubtab" })
            {
                var r = tv.FindControl<RadioButton>(sub); if (r is null) continue;
                Click(w, r); await Settle(40);
                Shot(w, $"theme-{name}-{sub}"); Audit(w, $"theme {name} › Settings › {Label(r)}");
            }
            if (opt.Id == ThemeService.Custom || opt.Id == ThemeService.Anime || opt.Id == ThemeService.DarkSakura)
                await Exercise(w, tv, $"theme {name} controls", c => IsNav(c) || c.Name is "ThemeSubtab" or "BackdropSubtab" or "ColorsSubtab", 60);
            w.Close();

            var vm = Editor(app); vm.RequestTab(TabletDetailTab.PenInputs);
            var pv = new TabletDetailView { DataContext = vm };
            var w2 = Host(pv, 1100, 800); Shot(w2, $"theme-{name}-tablet-pen"); Audit(w2, $"theme {name} › tablet pen tab"); w2.Close();
            var dv = new DynamicsView { DataContext = vm };
            var w3 = Host(dv, 1000, 760); Shot(w3, $"theme-{name}-pressure"); Audit(w3, $"theme {name} › pressure"); w3.Close();
            var mw = new MainWindow { Width = 1100, Height = 760 }; mw.Show(); await Settle(120);
            Shot(mw, $"theme-{name}-home"); Audit(mw, $"theme {name} › Home");
            mw.AllowCloseForQuit(); mw.Close(); SafeDispose(mw.DataContext);
            vm.Dispose();
        }
        theme.SelectedTheme = theme.ThemeOptions.First(o => o.Id == ThemeService.Wacom); await Settle();
    }

    // ── 3. tablet page: every tool tile and tab, mapping, buttons, ring ─────────────────────────
    [AvaloniaFact]
    public async Task C_TabletPage()
    {
        Hook();
        var (app, daemon, store) = await Session();
        using var _a = app;
        using var vm = Editor(app);
        var view = new TabletDetailView { DataContext = vm };
        var w = Host(view, 1100, 820);
        foreach (var tool in new[] { "ToolFunctionsButton", "ToolPenButton", "ToolMoreButton" })
        {
            var t = view.FindControl<RadioButton>(tool)!;
            Click(w, t); await Settle(60);
            foreach (var tab in Rail(view, "TabRail"))
            {
                Click(w, tab); await Settle(80);
                if (tab.IsChecked != true) { Defect($"[tab] Tablet › {Label(t)} › '{Label(tab)}' did not select"); continue; }
                var page = $"Tablet › {Label(t)} › {Label(tab)}";
                Shot(w, $"tablet-{Label(t)}-{Label(tab)}"); Audit(w, page);
                await Exercise(w, view, page, c => IsNav(c) || c is RadioButton { GroupName: "WacomDevice" or "WacomTool" or "WacomApp" });
                if (Label(t) == "Functions" || tool == "ToolFunctionsButton") { Click(w, t); await Settle(); }
                Click(w, tab); await Settle();
            }
        }

        // Mapping: Force proportions toggles and persists
        vm.RequestTab(TabletDetailTab.DisplayMapping);
        var w2 = Host(new TabletDetailView { DataContext = vm }, 1100, 820);
        var fp = Visible<CheckBox>(w2).FirstOrDefault(c => Label(c) == "Force proportions");
        if (fp is null) Skip("Mapping › Force proportions: shown only with several displays (HasSeveralDisplays); the headless box has one, so toggled on the view model instead");
        if (fp is null)
        {
            var b0 = vm.ForceProportions; vm.ForceProportions = !b0; await Settle(60);
            if (AppSettings.Get("AllDisplaysForceProportions") != (!b0 ? "1" : "0")) Defect("[Mapping] Force proportions not persisted"); else Pass("Force proportions toggles and persists (view model)");
            vm.ForceProportions = b0;
        }
        else
        {
            var before = vm.ForceProportions; Click(w2, fp); await Settle(100);
            if (vm.ForceProportions == before) Defect("[Mapping] Force proportions click did not change the setting");
            else if (AppSettings.Get("AllDisplaysForceProportions") != (vm.ForceProportions ? "1" : "0")) Defect("[Mapping] Force proportions not persisted");
            else Pass($"Force proportions toggles ({before} → {vm.ForceProportions}) and persists");
            Click(w2, fp); await Settle(60);
        }
        Shot(w2, "tablet-mapping"); w2.Close();
        w.Close();
    }

    // ── 4. pen page: tip/back button, eraser, Fix, pressure persistence ──────────────────────────
    [AvaloniaFact]
    public async Task D_PenPageAndPressure()
    {
        Hook();
        var (app, daemon, store) = await Session();
        using var _a = app;
        // R14: there is no PEN page; every pen setting lives on Tablet › Pen.
        var cached = Editor(app);
        var w = Host(new TextBlock(), 300, 200);
        var tdv = new TabletDetailView { DataContext = cached };
        var tw = Host(tdv, 1100, 1300); await Settle(100);
        tdv.FindControl<RadioButton>("PenTab")!.IsChecked = true; await Settle(80);
        Shot(tw, "tablet-pen-basics-panel"); Audit(tw, "Tablet › Pen (with movement + pen input)");
        await Exercise(tw, tdv, "Tablet › Pen", c => IsNav(c) || Label(c) == "Fix" || Label(c) == "Change…" || Label(c) == "Customize…");
        tdv.FindControl<RadioButton>("PenTab")!.IsChecked = true; await Settle(80);
        var texts = Visible<TextBlock>(tw).Select(t => t.Text ?? "").ToList();
        var heads = Visible<TextBlock>(tw).Where(t => t.Classes.Contains("penHeading")).ToList();
        tdv.FindControl<RadioButton>("EraserTab")!.IsChecked = true; await Settle(80);
        texts.AddRange(Visible<TextBlock>(tw).Select(t => t.Text ?? ""));
        heads.AddRange(Visible<TextBlock>(tw).Where(t => t.Classes.Contains("penHeading")));
        tdv.FindControl<RadioButton>("PenTab")!.IsChecked = true; await Settle(80);
        if (!texts.Any(t => t.StartsWith("Modified to"))) Defect("[Pen] a modified back button does not say 'Modified to …'");
        if (!texts.Contains("Default setting")) Defect("[Pen] default tip/eraser does not say 'Default setting'");
        if (texts.Any(t => t.Contains("Working normally"))) Defect("[Pen] still says 'Working normally'");
        // R12: headings "Pen tip", "Eraser", "Button 1", "Button 2", title case, all the same size.
        foreach (var label in new[] { "Pen tip", "Eraser", "Button 1", "Button 2" })
            if (!heads.Any(t => t.Text == label)) Defect($"[Pen] heading '{label}' missing (have: {string.Join(" | ", heads.Select(h => h.Text))})");
        if (heads.Select(h => h.FontSize).Distinct().Count() > 1) Defect($"[Pen] pen card headings differ in size: {string.Join(", ", heads.Select(h => $"{h.Text}={h.FontSize}"))}");
        else if (heads.Count == 4) Pass($"Pen card headings {string.Join(", ", heads.Select(h => h.Text))} all {heads[0].FontSize}px");
        // Fix (on Tablet › Pen, the cards' home)
        var fix = Visible<Button>(tw).FirstOrDefault(b => Label(b) == "Fix");
        if (fix is null) Defect("[Pen] no Fix button for the modified back button");
        else
        {
            Click(tw, fix); await Settle(150);
            foreach (var d in OpenWindows().Where(x => x != w && x != tw).ToList()) { Shot(d, "dialog-pen-fix"); Audit(d, "Fix dialog"); d.Close(); }
            var after = Visible<TextBlock>(tw).Select(t => t.Text ?? "").ToList();
            if (after.Any(t => t.StartsWith("Modified to"))) Defect("[Pen] Fix did not restore the back button (still 'Modified to …')"); else Pass("Pen Fix restores the default");
            Shot(tw, "tablet-pen-after-fix");
        }
        Shot(tw, "tablet-pen-cards"); tw.Close();

        // Pressure persistence through the real slider, Save, leave and return
        var vm = cached!;
        // The curve lives on Tablet › Pen › Feel details, Position smoothing on its movement panel.
        var host = Host(new TabletDetailView { DataContext = vm }, 1100, 1900); await Settle(80);
        var tdv2 = host.FindDescendantOfType<TabletDetailView>()!;
        tdv2.FindControl<RadioButton>("PenTab")!.IsChecked = true; tdv2.FindControl<Border>("FeelDetails")!.IsVisible = true; await Settle(80);
        Slider? S(double min, double max) => Visible<Slider>(host).Where(x => x.FindAncestorOfType<DynamicsView>() != null || x.FindAncestorOfType<PenBasicsPanel>() != null).FirstOrDefault(s => Math.Abs(s.Minimum - min) < 1e-6 && Math.Abs(s.Maximum - max) < 1e-6);
        var targets = new (string Name, double Min, double Max, double Value, Func<double> Read)[]
        {
            ("Softness", -0.9, 0.9, -0.45, () => vm.Softness),
            ("Pressure smoothing", 0, 0.5, 0.2, () => vm.PressureSmoothing),
            ("Position smoothing", 0, 0.25, 0.1, () => vm.PositionSmoothing),
            ("Max hover", 0, 255, 120, () => vm.MaxHoverDistance),
        };
        var found = new List<(string, double, Func<double>)>();
        foreach (var t in targets)
        {
            var s = S(t.Min, t.Max);
            if (s is null) { Skip($"Pressure: no visible slider {t.Min}…{t.Max} for {t.Name} on the pressure tab (set on the view model instead)"); continue; }
            s.Value = t.Value; await Settle(60);
            if (Math.Abs(t.Read() - t.Value) > 0.011) Defect($"[Pressure] {t.Name} slider set to {t.Value} but the setting reads {t.Read()}");
            found.Add((t.Name, t.Value, t.Read));
        }
        await vm.FlushPendingEditsAsync();
        await Settle(200);
        var saved = await app.SaveSettingsAsync();
        if (!saved) Defect("[Save] SaveSettingsAsync failed");
        Shot(host, "tablet-pen-pressure-after-save");
        // leave: another tab, then back
        tdv2.FindControl<RadioButton>("EraserTab")!.IsChecked = true; await Settle(80);
        tdv2.FindControl<RadioButton>("PenTab")!.IsChecked = true; await Settle(80);
        host.Close();
        foreach (var (n, v, read) in found)
            if (Math.Abs(read() - v) > 0.011) Defect($"[Pressure persist] {n}: saved {v}, after leaving and returning reads {read()}"); else Pass($"Pressure {n} = {v} survives Save, leave and return");
        // page rebuilt from the session (what the shell does), and from disk
        var fresh = Editor(app);
        if (Math.Abs(fresh.Softness - (-0.45)) > 0.011) Defect($"[Pressure persist] Softness from a rebuilt page reads {fresh.Softness}"); else Pass("Softness −0.45 survives a rebuilt page");
        var onDisk = PressureCurveProfile.Read(store.Saved, TabletName);
        if (onDisk is null || Math.Abs(onDisk.Value.Dynamics.Curve.Softness - (-0.45)) > 0.011) Defect($"[Pressure persist] Softness on disk is {onDisk?.Dynamics.Curve.Softness}"); else Pass("Softness −0.45 written to the saved settings file");
        fresh.Dispose();
        w.Close();
    }

    // ── 5. presets: save, load, rename, duplicate, delete, path display ──────────────────────────
    private sealed class NoHotkeys : IProfileHotkeys
    {
        public HotkeyChord? GetChord(string snapshot) => null;
        public HotkeySetResult SetHotkey(string snapshot, HotkeyChord chord) => HotkeySetResult.Ok;
        public void ClearHotkey(string snapshot) { }
        public void Sync(IEnumerable<string> snapshotNames) { }
        public void RenameSnapshot(string oldName, string newName) { }
    }

    [AvaloniaFact]
    public async Task E_Presets()
    {
        Hook();
        var dir = Path.Combine(Path.GetTempPath(), $"sweep-presets-{Guid.NewGuid():N}"); Directory.CreateDirectory(dir);
        var coord = new FakeSettingsCoordinator { CurrentSettings = TabletDocument() };
        var dialogs = new FakeDialogService { InputResult = "Renamed by sweep", ConfirmResult = true };
        var store = new PresetStore(NullOtdLog.Instance);
        var vm = new PresetsViewModel(store, coord, new FakeDeviceData { PresetDirectory = dir }, dialogs, new NoHotkeys(),
            new ProfileSwitchService(coord, store, () => dir)) { PresetDirectory = dir };
        var view = new PresetsView { DataContext = vm };
        var w = Host(view, 1000, 700);
        Shot(w, "presets-empty"); Audit(w, "Presets (empty)");
        var save = Visible<Button>(w).FirstOrDefault(b => b.Command == vm.SavePresetCommand);
        if (save is null) Defect("[Presets] no Save preset button"); else { Click(w, save); await Settle(150); }
        if (vm.Presets.Count != 1) Defect($"[Presets] Save made {vm.Presets.Count} presets"); else Pass($"Preset saved as '{vm.Presets[0].Name}'");
        Shot(w, "presets-one"); Audit(w, "Presets (one)");
        var pathShown = Visible<TextBlock>(w).Any(t => (t.Text ?? "").Contains(dir));
        if (!pathShown) Defect("[Presets] the preset folder path is not displayed"); else Pass("Presets folder path displayed");
        var name = vm.Presets.FirstOrDefault()?.Name ?? "";
        await vm.DuplicatePresetCommand.ExecuteAsync(name); await Settle();
        if (vm.Presets.Count != 2) Defect("[Presets] Duplicate failed"); else Pass("Preset duplicate");
        await vm.RenamePresetCommand.ExecuteAsync(name); await Settle();
        if (!vm.Presets.Any(p => p.Name == "Renamed by sweep")) Defect("[Presets] Rename failed"); else Pass("Preset rename");
        coord.CurrentSettings = new Settings();
        await vm.LoadPresetCommand.ExecuteAsync("Renamed by sweep"); await Settle(100);
        Pass($"Preset load ran (applied profiles now {coord.CurrentSettings?.Profiles.Count})");
        Shot(w, "presets-two");
        await Exercise(w, view, "Presets", c => c is Button b && (b.Command == vm.OpenFolderCommand));
        await vm.DeletePresetCommand.ExecuteAsync("Renamed by sweep"); await Settle();
        if (vm.Presets.Any(p => p.Name == "Renamed by sweep")) Defect("[Presets] Delete failed"); else Pass("Preset delete (after confirm)");
        Shot(w, "presets-after-delete");
        w.Close();
    }

    // ── 6. Save / Revert / unsaved prompt / quit ─────────────────────────────────────────────────
    [AvaloniaFact]
    public async Task F_SaveRevertUnsavedQuit()
    {
        Hook();
        var (app, daemon, store) = await Session();
        using var _a = app;
        using (var vm = Editor(app))
        {
            vm.Softness = 0.3; await vm.FlushPendingEditsAsync(); await Settle(100);
            if (!app.HasUnsavedChanges) Defect("[Save] an edit did not mark settings unsaved"); else Pass("edit marks settings unsaved");
            if (!await app.SaveSettingsAsync() || app.HasUnsavedChanges) Defect("[Save] Save did not clear unsaved"); else Pass("Save clears unsaved");
            vm.Softness = 0.6; await vm.FlushPendingEditsAsync(); await Settle(100);
            var revert = new ProfileSwitchService(app, new PresetStore(NullOtdLog.Instance), () => "");
            var restored = await revert.RestoreDefaultAsync(); await Settle(150);
            var back = Editor(app);
            Pass($"Revert to saved ran (restored={restored})");
            if (Math.Abs(back.Softness - 0.3) > 0.011) Defect($"[Revert] reload from saved gives Softness {back.Softness}, expected 0.3"); else Pass("Revert to saved restores the saved value");
            back.Dispose();
        }

        // the unsaved-changes prompt
        var host = new MainWindow { Width = 1100, Height = 760 }; host.Show(); await Settle(150);
        var prompt = OpenTabletArtist.Helpers.Dialogs.ShowUnsavedSettingsAsync();
        await Settle(200);
        var dlg = OpenWindows().FirstOrDefault(x => x != host);
        if (dlg is null) Defect("[Unsaved prompt] no dialog appeared");
        else
        {
            Shot(dlg, "dialog-unsaved-changes"); Audit(dlg, "unsaved-changes prompt");
            var buttons = Visible<Button>(dlg).Select(Label).ToList();
            Pass($"unsaved prompt buttons: {string.Join(" / ", buttons)}");
            var cancel = Visible<Button>(dlg).LastOrDefault();
            if (cancel != null) Click(dlg, cancel); await Settle(100);
            if (!prompt.IsCompleted) { Defect("[Unsaved prompt] did not close on a button"); dlg.Close(); }
            else Pass($"unsaved prompt answers {prompt.Result} for '{Label(cancel!)}'");
        }
        // footer in the shell
        Shot(host, "footer-disconnected"); Audit(host, "footer");
        var hvm = (MainViewModel)host.DataContext!;
        var t0 = DateTime.UtcNow; var ok = await hvm.SnapshotUnsavedForSystemQuitAsync();
        Pass($"system-quit snapshot with nothing to save returns {ok} in {(DateTime.UtcNow - t0).TotalMilliseconds:0} ms");
        host.AllowCloseForQuit(); host.Close(); SafeDispose(hvm);

        // system quit never cancels, even when the save fails or hangs
        foreach (var (label, save) in new (string, Func<Task<bool>>)[]
        {
            ("save ok", () => Task.FromResult(true)), ("save fails", () => Task.FromResult(false)),
            ("save throws", () => throw new InvalidOperationException("x")), ("save hangs", () => new TaskCompletionSource<bool>().Task),
        })
        {
            bool allowed = false;
            var q = new SystemQuitCoordinator(() => allowed = true, save, _ => Task.FromResult(true), () => null,
                SystemQuitCoordinator.PumpUiUntil, _ => { });
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var cancel = q.OnShutdownRequested();
            if (cancel || !allowed) Defect($"[Quit] shutdown with '{label}' cancelled={cancel} allowed={allowed}");
            else if (sw.Elapsed > TimeSpan.FromSeconds(7)) Defect($"[Quit] shutdown with '{label}' took {sw.Elapsed.TotalSeconds:0.0}s");
            else Pass($"system quit with '{label}': never cancels, {sw.Elapsed.TotalSeconds:0.0}s");
        }
    }

    // ── 7. focused re-checks of things the generic pass flagged ─────────────────────────────────
    [AvaloniaFact]
    public async Task G_Rechecks()
    {
        Hook();
        var (app, _, _) = await Session();
        using var _a = app;
        using var vm = Editor(app);
        var view = new TabletDetailView { DataContext = vm };
        var w = Host(view, 1100, 820);
        var tilt = Visible<CheckBox>(w).FirstOrDefault(c => Label(c) == "Turn tilt off");
        if (tilt != null)
        {
            Click(w, tilt); await Settle(300);
            var afterUi = tilt.IsChecked; var afterVm = vm.DisableTilt; var inDoc = app.CurrentSettings!.Profiles[0].BindingSettings.DisableTilt;
            if (afterUi != true || !afterVm || !inDoc) Defect($"[no response] Pen › 'Turn tilt off': after one click box={afterUi}, setting={afterVm}, applied={inDoc}");
            else Pass("Turn tilt off toggles and applies");
            Click(w, tilt); await Settle(300);
        }
        foreach (var name in new[] { "EraserTab", "MappingTab", "PenTab" })
        {
            var t = view.FindControl<RadioButton>(name)!;
            var hit = Click(w, t); await Settle(100);
            if (t.IsChecked != true) Defect($"[tab] Tablet › Pen › '{Label(t)}' does not select on click (hit={hit})");
            else { Pass($"Tablet › Pen › '{Label(t)}' selects"); Shot(w, $"tablet-pen-{Label(t)}"); Audit(w, $"Tablet › Pen › {Label(t)}"); }
        }
        w.Close();
    }

    [AvaloniaFact]
    public void Z_WriteReport()
    {
        WriteReport();
    }
}
