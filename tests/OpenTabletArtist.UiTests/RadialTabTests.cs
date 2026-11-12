using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletArtist.Controls;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OtdInterop;
using OtdInterop.Tests;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>TASKS #63 (Zak 20:31): Tablet › Pen › "Radial menu" tab, between Eraser and Mapping: the radial menu editor of the
/// selected application inside the page, with a top line saying what opens it, saved with Save like the other tabs.</summary>
public class RadialTabTests
{
    private static PluginSettingStore Radial(string firstLabel)
    {
        var menu = RadialMenuDefaults.Create(Localizer.Te);
        menu.Slices[0].Label = firstLabel;
        return RadialMenuBinding.Make(menu);
    }

    private static Profile NewProfile(string tablet, PluginSettingStore?[] penButtons, PluginSettingStore?[]? aux = null)
    {
        var profile = new Profile { Tablet = tablet, OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)) };
        profile.BindingSettings.PenButtons = new PluginSettingStoreCollection();
        foreach (var b in penButtons) profile.BindingSettings.PenButtons.Add(b!);
        profile.BindingSettings.AuxButtons = new PluginSettingStoreCollection();
        foreach (var b in aux ?? Array.Empty<PluginSettingStore?>()) profile.BindingSettings.AuxButtons.Add(b!);
        return profile;
    }

    private static string Name() => "Radial tab " + Guid.NewGuid().ToString("N")[..8];

    private static async Task Pump(int ms = 40)
    {
        await Task.Delay(ms, TestContext.Current.CancellationToken);
        for (int i = 0; i < 3; i++) Dispatcher.UIThread.RunJobs();
    }

    private static (TabletDetailViewModel Vm, TabletDetailView View, Window Window, Profile Profile) Open(
        Profile profile, double w = 1100, double h = 720, Func<Settings, Task<SettingsApplyOutcome>>? apply = null, Settings? settings = null)
    {
        _ = new ThemeViewModel();
        settings ??= new Settings { Profiles = new ProfileCollection { profile } };
        var vm = new TabletDetailViewModel(profile, settings, applyAction: apply ?? (_ => Task.FromResult(SettingsApplyOutcome.Live)));
        vm.ShowHeader = false;
        var view = new TabletDetailView { DataContext = vm };
        // The app hosts the page under the nav row and above the footer.
        var window = new Window { Content = new Border { Child = view, Margin = new Thickness(24, 86, 24, 62) }, Width = w, Height = h };
        window.Show();
        view.FindControl<RadioButton>("RadialTab")!.IsChecked = true;
        Settle(window);
        return (vm, view, window, profile);
    }

    private static void Settle(Window w) { for (int i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); } }

    private static Rect In(Visual v, Visual root) => new Rect(v.TranslatePoint(new Point(0, 0), root) ?? default, v.Bounds.Size);

    // ── tab order and name ───────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void TheTab_SitsBetweenEraserAndMapping_AndIsCalledRadialMenu()
    {
        var (vm, view, window, _) = Open(NewProfile(Name(), new PluginSettingStore?[] { null, Radial("A") }));
        try
        {
            var tabs = view.FindControl<WrapPanel>("TabRail")!.Children.OfType<RadioButton>().ToList();
            int eraser = tabs.IndexOf(view.FindControl<RadioButton>("EraserTab")!);
            int radial = tabs.IndexOf(view.FindControl<RadioButton>("RadialTab")!);
            int mapping = tabs.IndexOf(view.FindControl<RadioButton>("MappingTab")!);
            Assert.True(eraser >= 0 && radial == eraser + 1 && mapping == radial + 1, $"order eraser={eraser} radial={radial} mapping={mapping}");
            Assert.Equal(Localizer.Te("radial.title", "Radial menu"), TitleCase(view.FindControl<RadioButton>("RadialTab")!));
            Assert.True(view.FindControl<RadioButton>("RadialTab")!.IsVisible);   // on the Pen tool
            Assert.True(view.FindControl<ScrollViewer>("RadialPage")!.IsVisible);
        }
        finally { window.Close(); }
    }

    private static string TitleCase(RadioButton tab) => OpenTabletArtist.Converters.TitleCaseConverter.Apply(tab.Content as string) ?? "";

    [AvaloniaFact]
    public void TheTab_IsHiddenOutsideThePenTool()
    {
        var (vm, view, window, _) = Open(NewProfile(Name(), new PluginSettingStore?[] { null, Radial("A") }));
        try
        {
            vm.PenToolActive = false;
            Settle(window);
            Assert.False(view.FindControl<RadioButton>("RadialTab")!.IsVisible);
        }
        finally { window.Close(); }
    }

    // ── the three top-line states ────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void OneButtonOpensTheMenu_TopLineSaysWhich_AndTheEditorShowsItsMenu()
    {
        var (vm, view, window, _) = Open(NewProfile(Name(), new PluginSettingStore?[] { null, Radial("Zoomy") }));
        try
        {
            Assert.Equal("Opens with: Pen button 2", vm.RadialTopLine);
            Assert.Equal(vm.RadialTopLine, view.FindControl<TextBlock>("RadialTopLineText")!.Text);
            Assert.False(view.FindControl<Button>("RadialUseButton")!.IsEffectivelyVisible);
            Assert.False(vm.ShowRadialChooser);
            Assert.NotNull(vm.RadialEditor);
            Assert.Equal("Zoomy", vm.RadialEditor!.Labels[0]);
            // the real editor (pie, slice settings, size, transparency box, Reset) is on the page
            var host = view.FindControl<ContentControl>("RadialEditorHost")!;
            Assert.Contains(host.GetVisualDescendants().OfType<RadialMenuPie>(), p => p.Interactive);
            Assert.Contains(host.GetVisualDescendants().OfType<TextBox>(), t => t.Name == "TransparencyBox");
            Assert.Equal(2, host.GetVisualDescendants().OfType<Slider>().Count());
            Assert.Contains(host.GetVisualDescendants().OfType<Button>(), b => b.Command == vm.RadialEditor.ResetCommand);
            Assert.DoesNotContain(host.GetVisualDescendants().OfType<Button>(), b => b.Command == vm.RadialEditor.SaveCommand);   // the page's Save is the footer's
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task NothingOpensTheMenu_OffersUsePenButton2_AndClickingItSetsIt()
    {
        var profile = NewProfile(Name(), new PluginSettingStore?[] { null, AuxKeyBinding.MakeKeyBinding("E") });
        var (vm, view, window, _) = Open(profile);
        try
        {
            Assert.Equal("No pen button opens the radial menu yet", view.FindControl<TextBlock>("RadialTopLineText")!.Text);
            var use = view.FindControl<Button>("RadialUseButton")!;
            Assert.True(use.IsEffectivelyVisible);
            Assert.Equal("Use pen button 2", use.Content);
            Assert.Null(vm.RadialEditor);

            Assert.True(use.Command!.CanExecute(null));
            use.Command.Execute(null);                   // what a click on the button runs
            await Pump(100); Settle(window);

            Assert.True(RadialMenuBinding.Is(profile.BindingSettings.PenButtons[1]));
            Assert.Equal("Opens with: Pen button 2", vm.RadialTopLine);
            Assert.False(use.IsEffectivelyVisible);
            Assert.NotNull(vm.RadialEditor);
            Assert.True(RadialMenuBinding.Is(profile.BindingSettings.PenButtons[1]) && RadialMenuBinding.ReadMenu(profile.BindingSettings.PenButtons[1]).Slices.Any(s => s.IsSet));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ApenWithOneButton_OffersButton1()
    {
        var (vm, view, window, _) = Open(NewProfile(Name(), new PluginSettingStore?[] { null }));
        try { Assert.Equal("Use pen button 1", view.FindControl<Button>("RadialUseButton")!.Content); }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task SeveralButtonsOpenMenus_ALittleChooserPicksWhichOneIsEdited()
    {
        var profile = NewProfile(Name(), new PluginSettingStore?[] { Radial("One"), Radial("Two") }, new PluginSettingStore?[] { null, null, Radial("Three") });
        var (vm, view, window, _) = Open(profile);
        try
        {
            Assert.Equal("Opens with: Pen button 1, Pen button 2, ExpressKey 3", vm.RadialTopLine);
            var chooser = view.FindControl<ComboBox>("RadialChooser")!;
            Assert.True(chooser.IsEffectivelyVisible);
            Assert.Equal(3, vm.RadialOpeners.Count);
            Assert.Equal("One", vm.RadialEditor!.Labels[0]);

            chooser.SelectedIndex = 2; await Pump(); Settle(window);
            Assert.Equal("Three", vm.RadialEditor!.Labels[0]);
            chooser.SelectedIndex = 1; await Pump(); Settle(window);
            Assert.Equal("Two", vm.RadialEditor!.Labels[0]);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ChangingTheButtonElsewhere_UpdatesTheTab()
    {
        var profile = NewProfile(Name(), new PluginSettingStore?[] { null, Radial("A") });
        var (vm, view, window, _) = Open(profile);
        try
        {
            profile.BindingSettings.PenButtons[1] = AuxKeyBinding.MakeKeyBinding("E")!;
            vm.RefreshRadialTab();
            Settle(window);
            Assert.Equal("No pen button opens the radial menu yet", vm.RadialTopLine);
            Assert.Null(vm.RadialEditor);
            await Pump();
        }
        finally { window.Close(); }
    }

    // ── content fits ─────────────────────────────────────────────────────────────────────────────────────

    [AvaloniaTheory]
    [InlineData(1100, 720)]
    [InlineData(900, 600)]
    [InlineData(1500, 1000)]
    public void TheEditorFits_ThePieNeverTouchesTheSizeAndTransparencyRow_NothingIsClippedSideways(double w, double h)
    {
        var (vm, view, window, _) = Open(NewProfile(Name(), new PluginSettingStore?[] { null, Radial("A") }), w, h);
        try
        {
            vm.RadialEditor!.Size = 100;           // the biggest ring is the worst case
            Settle(window);
            var host = view.FindControl<ContentControl>("RadialEditorHost")!;
            var page = view.FindControl<ScrollViewer>("RadialPage")!;
            var pie = host.GetVisualDescendants().OfType<RadialMenuPie>().First(p => p.Interactive);
            var row = host.GetVisualDescendants().OfType<Control>().First(c => c.Name == RadialMenuEditorDialog.FooterName);

            var pieRect = In(pie, host); var rowRect = In(row, host);
            Assert.False(pieRect.Intersects(rowRect), $"pie {pieRect} overlaps the size/transparency row {rowRect} at {w}x{h}");
            Assert.True(pieRect.Bottom <= rowRect.Top, $"pie bottom {pieRect.Bottom} is not above the row top {rowRect.Top}");
            Assert.Equal(pie.Bounds.Width, pie.Bounds.Height, 1);
            // across: the editor, the pie and the row all stay inside the page's width (no sideways clipping)
            var pageWidth = page.Viewport.Width;
            Assert.True(In(host, page).Right <= pageWidth + 0.5, $"editor {In(host, page)} wider than the page {pageWidth}");
            Assert.True(In(pie, page).Right <= pageWidth + 0.5 && In(pie, page).Left >= -0.5);
            Assert.True(pie.Bounds.Width >= RadialMenuEditorDialog.MinPieDiameter - 0.5);
            // the settings column sits beside the pie, not over it
            var settings = host.GetVisualDescendants().OfType<StackPanel>().First(s => s.MinWidth == 380);
            Assert.False(In(settings, host).Intersects(pieRect), $"settings {In(settings, host)} overlap the pie {pieRect}");
            // the page scrolls when the window is short, instead of cutting the ring off
            if (h < 1000) Assert.True(page.Extent.Height >= page.Viewport.Height);
            page.ScrollToEnd(); Settle(window);
            Assert.True(In(row, page).Bottom <= page.Viewport.Height + 0.5 + 1, $"after scrolling the row {In(row, page)} is still below the page ({page.Viewport.Height})");
        }
        finally { window.Close(); }
    }

    // ── Save persists, errors are logged ─────────────────────────────────────────────────────────────────

    private static async Task PumpUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "UI did not settle.");
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static Settings Document(string tablet, string label)
    {
        var profile = NewProfile(tablet, new PluginSettingStore?[] { null, Radial(label) });
        profile.AbsoluteModeSettings = new AbsoluteModeSettings
        {
            Display = new AreaSettings { Width = 1920, Height = 1080, X = 960, Y = 540 },
            Tablet = new AreaSettings { Width = 100, Height = 60, X = 50, Y = 30 },
        };
        return new Settings { Profiles = new ProfileCollection { profile } };
    }

    [AvaloniaFact]
    public async Task AnEdit_IsAppliedToTheTable_MarksUnsaved_AndSaveKeepsIt()
    {
        var tablet = Name();
        var daemon = new FakeDaemonTransport { Settings = Document(tablet, "Before") };
        daemon.GetSettingsHandler = async () => { await Task.Yield(); return daemon.Settings; };
        var store = new MemorySettingsFileStore { Saved = Document(tablet, "Before") };
        using var app = new AppSession(FakeSession.Over(daemon, store), new FakeLifecycle());
        daemon.Reconnect();
        await PumpUntil(() => app.CurrentSettings is not null);

        var settings = app.CurrentSettings!;
        var (vm, view, window, profile) = Open(settings.Profiles[0], apply: app.ApplySettingsAsync, settings: settings);
        try
        {
            Assert.False(app.HasUnsavedChanges);
            var editor = vm.RadialEditor!;
            editor.SliceLabel = "Edited name";            // slice 1's name, in the real editor
            editor.Transparency = 12;
            editor.Size = 77;
            await vm.FlushRadialAsync();
            await PumpUntil(() => !app.SettingsBusy && app.SaveState != SettingsSaveState.Applying);

            var stored = RadialMenuBinding.ReadMenu(app.CurrentSettings!.Profiles[0].BindingSettings.PenButtons[1]);
            Assert.Equal("Edited name", stored.Slices[0].Label);
            Assert.Equal(12, stored.Transparency);
            Assert.Equal(77, stored.Size);
            Assert.True(app.HasUnsavedChanges);           // like every other tab: kept by the footer's Save
            Assert.Equal("Before", RadialMenuBinding.ReadMenu(store.Saved!.Profiles[0].BindingSettings.PenButtons[1]).Slices[0].Label);

            Assert.True(await app.SaveSettingsAsync());
            Assert.False(app.HasUnsavedChanges);
            var saved = RadialMenuBinding.ReadMenu(store.Saved!.Profiles[0].BindingSettings.PenButtons[1]);
            Assert.Equal("Edited name", saved.Slices[0].Label);
            Assert.Equal(12, saved.Transparency);
            Assert.Equal(77, saved.Size);
            // and the tab still shows it after the save refreshed the page
            Assert.Equal("Edited name", vm.RadialEditor!.Labels[0]);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task TypingAName_IsSentOnceAfterAShortPause_NotPerLetter()
    {
        int applies = 0;
        var (vm, view, window, profile) = Open(NewProfile(Name(), new PluginSettingStore?[] { null, Radial("A") }),
            apply: _ => { applies++; return Task.FromResult(SettingsApplyOutcome.Live); });
        try
        {
            foreach (var s in new[] { "B", "Bo", "Bol", "Bold" }) vm.RadialEditor!.SliceLabel = s;
            Assert.Equal(0, applies);
            await PumpUntil(() => applies >= 1);
            await Pump(TabletDetailViewModel.RadialApplyDelay.Milliseconds + 100);
            Assert.Equal(1, applies);
            Assert.Equal("Bold", RadialMenuBinding.ReadMenu(profile.BindingSettings.PenButtons[1]).Slices[0].Label);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task APageThatCannotWrite_LogsEveryFailure_AndShowsWhatIsStoredAgain()
    {
        var lines = new List<string>();
        void Hook(string l) { lock (lines) lines.Add(l); }
        AppLog.LineWritten += Hook;
        try
        {
            var profile = NewProfile(Name(), new PluginSettingStore?[] { null, Radial("Kept") });
            _ = new ThemeViewModel();
            var settings = new Settings { Profiles = new ProfileCollection { profile } };
            var vm = new TabletDetailViewModel(profile, settings, applyAction: null);       // no live driver connection
            vm.ShowHeader = false;
            var view = new TabletDetailView { DataContext = vm };
            var window = new Window { Content = view, Width = 1100, Height = 720 };
            window.Show(); view.FindControl<RadioButton>("RadialTab")!.IsChecked = true; Settle(window);
            try
            {
                Assert.False(vm.CanEditRadial);
                Assert.False(view.FindControl<ContentControl>("RadialEditorHost")!.IsEffectivelyEnabled);   // read-only
                vm.RadialEditor!.SliceLabel = "Lost";
                await vm.FlushRadialAsync();
                await Pump(50);
                lock (lines)
                {
                    Assert.Contains(lines, l => l.Contains("[WARNING]") && l.Contains("Radial menu tab") && l.Contains("was NOT applied"));
                    Assert.Contains(lines, l => l.Contains("Settings change refused"));
                }
                Assert.Equal("Kept", vm.RadialEditor!.Labels[0]);       // back to what is stored
                Assert.Equal("Kept", RadialMenuBinding.ReadMenu(profile.BindingSettings.PenButtons[1]).Slices[0].Label);
            }
            finally { window.Close(); }
        }
        finally { AppLog.LineWritten -= Hook; }
    }

    [AvaloniaFact]
    public async Task ALaterEditIsNeverThrownAwayByARefresh()
    {
        var gate = new TaskCompletionSource<SettingsApplyOutcome>();
        var (vm, view, window, profile) = Open(NewProfile(Name(), new PluginSettingStore?[] { null, Radial("A") }));
        try
        {
            vm.RadialEditor!.SliceLabel = "Typing";
            vm.RefreshRadialTab();                       // e.g. the driver echoed settings while the user was typing
            Assert.Equal("Typing", vm.RadialEditor!.Labels[0]);
            await vm.FlushRadialAsync();
            Assert.Equal("Typing", RadialMenuBinding.ReadMenu(profile.BindingSettings.PenButtons[1]).Slices[0].Label);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheTexts_ExistInAll8Languages()
    {
        var keys = new[] { "tablet.tab.radial", "radial_tab.opens_with", "radial_tab.none", "radial_tab.use_button", "radial_tab.pen_button", "radial_tab.express_key", "radial_tab.choose" };
        var dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "OpenTabletArtist", "Assets", "i18n"));
        foreach (var code in new[] { "en", "bg", "de", "es", "fr", "ja", "ru", "zh-CN" })
        {
            var json = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(dir, code + ".json")));
            foreach (var k in keys) Assert.False(string.IsNullOrWhiteSpace((string?)json[k]), $"{code} misses {k}");
            Assert.Contains("{buttons}", (string)json["radial_tab.opens_with"]!);
            foreach (var k in new[] { "radial_tab.use_button", "radial_tab.pen_button", "radial_tab.express_key" })
                Assert.Contains("{number}", (string)json[k]!);
        }
    }
}
