using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletArtist.Controls;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Helpers;
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

/// <summary>TASKS #63 (Zak 20:53 / 21:00 / 21:02): Tablet › Pen › "Radial Menu Settings": the radial menu's picture on the right (label above it,
/// nothing else above), the line + "Use pen button 2" and the Menu transparency / Menu size sliders below it; the pen buttons in the middle next to
/// the pen drawing. Clicking the picture opens the radial menu editor; the sliders change the picture live and are kept by Save.</summary>
public class RadialSpotTests
{
    private static PluginSettingStore Radial(string firstLabel)
    {
        var menu = RadialMenuDefaults.Create(Localizer.Te);
        menu.Slices[0].Label = firstLabel;
        return RadialMenuBinding.Make(menu);
    }

    private static Profile NewProfile(string tablet, PluginSettingStore?[] penButtons)
    {
        var profile = new Profile { Tablet = tablet, OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)) };
        profile.BindingSettings.PenButtons = new PluginSettingStoreCollection();
        foreach (var b in penButtons) profile.BindingSettings.PenButtons.Add(b!);
        profile.BindingSettings.AuxButtons = new PluginSettingStoreCollection();
        return profile;
    }

    private static string Name() => "Radial spot " + Guid.NewGuid().ToString("N")[..8];

    private static async Task Pump(int ms = 40)
    {
        await Task.Delay(ms, TestContext.Current.CancellationToken);
        for (int i = 0; i < 3; i++) Dispatcher.UIThread.RunJobs();
    }

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

    private static (TabletDetailViewModel Vm, TabletDetailView View, Window Window, Profile Profile) Open(
        Profile profile, double w = 1100, double h = 720, Func<Settings, Task<SettingsApplyOutcome>>? apply = null, Settings? settings = null, bool readOnly = false)
    {
        _ = new ThemeViewModel();
        settings ??= new Settings { Profiles = new ProfileCollection { profile } };
        var vm = new TabletDetailViewModel(profile, settings, applyAction: readOnly ? null : apply ?? (_ => Task.FromResult(SettingsApplyOutcome.Live)));
        vm.ShowHeader = false;
        var view = new TabletDetailView { DataContext = vm };
        var window = new Window { Content = new Border { Child = view, Margin = new Thickness(24, 86, 24, 62) }, Width = w, Height = h };
        window.Show();
        Settle(window);
        return (vm, view, window, profile);
    }

    private static void Settle(Window w) { for (int i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); } }

    private static Rect In(Visual v, Visual root) => new Rect(v.TranslatePoint(new Point(0, 0), root) ?? default, v.Bounds.Size);

    private static T Find<T>(TabletDetailView v, string name) where T : Control => v.FindControl<T>(name)!;

    /// <summary>Log lines written while <paramref name="body"/> runs.</summary>
    private static async Task<List<string>> Logged(Func<Task> body)
    {
        var lines = new List<string>();
        void H(string l) { lock (lines) lines.Add(l); }
        AppLog.LineWritten += H;
        try { await body(); } finally { AppLog.LineWritten -= H; }
        lock (lines) return lines.ToList();
    }

    // ── what is on the page ──────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void ThePictureIsAlwaysThere_LabelAbove_LineSlidersBelow_AndNoTab()
    {
        var (vm, view, window, _) = Open(NewProfile(Name(), new PluginSettingStore?[] { null, Radial("Zoomy") }));
        try
        {
            var spot = Find<StackPanel>(view, "RadialSpot");
            Assert.True(spot.IsEffectivelyVisible);
            var heading = Find<TextBlock>(view, "RadialSpotHeading");
            Assert.Equal("Radial Menu Settings", heading.Text);
            Assert.Same(heading, spot.Children[0]);                       // the label is the first (top) thing
            var pie = Find<RadialMenuPie>(view, "RadialSpotPie");
            Assert.Equal("Zoomy", pie.Labels![0]);                        // this application's menu, not a fixed picture
            var line = Find<TextBlock>(view, "RadialSpotLine"); var sliders = Find<StackPanel>(view, "RadialSliders");
            var btn = Find<Button>(view, "RadialSpotButton");
            Assert.True(In(heading, view).Bottom <= In(btn, view).Top + 0.5);          // only the label above the picture
            Assert.True(In(btn, view).Bottom <= In(line, view).Top + 0.5);
            Assert.True(In(line, view).Bottom <= In(sliders, view).Top + 0.5);          // the line, then the sliders, below the picture
            Assert.Null(view.FindControl<RadioButton>("RadialTab"));                    // no new tab between Eraser and Mapping
            Assert.Equal("Opens with: Pen button 2", line.Text);
            Assert.False(Find<Button>(view, "RadialUseButton").IsEffectivelyVisible);
            Assert.Equal(1.0, Find<Button>(view, "RadialSpotButton").Opacity);
            Assert.True(sliders.IsEnabled);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task NothingOnTheRadialMenu_PictureDimmed_LineBelow_AndUsePenButton2Sets()
    {
        var profile = NewProfile(Name(), new PluginSettingStore?[] { null, AuxKeyBinding.MakeKeyBinding("E") });
        var (vm, view, window, _) = Open(profile);
        try
        {
            Assert.True(Find<StackPanel>(view, "RadialSpot").IsEffectivelyVisible);   // still shown
            Assert.True(Find<Button>(view, "RadialSpotButton").Opacity < 0.6);        // dimmed
            Assert.False(Find<Button>(view, "RadialSpotButton").IsEnabled);
            Assert.Equal("No pen button opens the radial menu yet", Find<TextBlock>(view, "RadialSpotLine").Text);
            Assert.False(Find<StackPanel>(view, "RadialSliders").IsEnabled);
            var use = Find<Button>(view, "RadialUseButton");
            Assert.True(use.IsEffectivelyVisible);
            Assert.Equal("Use pen button 2", use.Content);
            Assert.True(In(Find<Button>(view, "RadialSpotButton"), view).Bottom <= In(Find<TextBlock>(view, "RadialSpotLine"), view).Top + 0.5);

            Assert.True(use.Command!.CanExecute(null));
            use.Command.Execute(null);                  // what a click on the button runs
            await Pump(100); Settle(window);

            Assert.True(RadialMenuBinding.Is(profile.BindingSettings.PenButtons[1]));
            Assert.Equal("Opens with: Pen button 2", Find<TextBlock>(view, "RadialSpotLine").Text);
            Assert.False(use.IsEffectivelyVisible);
            Assert.Equal(1.0, Find<Button>(view, "RadialSpotButton").Opacity);
            Assert.True(Find<Button>(view, "RadialSpotButton").IsEnabled);
            Assert.True(Find<StackPanel>(view, "RadialSliders").IsEnabled);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void APenWithOneButton_OffersButton1()
    {
        var (_, view, window, _) = Open(NewProfile(Name(), new PluginSettingStore?[] { null }));
        try { Assert.Equal("Use pen button 1", Find<Button>(view, "RadialUseButton").Content); }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ChangingTheButtonElsewhere_UpdatesThePicture()
    {
        var profile = NewProfile(Name(), new PluginSettingStore?[] { null, Radial("A") });
        var (vm, view, window, _) = Open(profile);
        try
        {
            profile.BindingSettings.PenButtons[1] = AuxKeyBinding.MakeKeyBinding("E")!;
            vm.RefreshRadialSpot(); Settle(window);
            Assert.Equal("No pen button opens the radial menu yet", Find<TextBlock>(view, "RadialSpotLine").Text);
            await Pump();
        }
        finally { window.Close(); }
    }

    // ── clicking the picture opens the editor ───────────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task ClickingThePicture_OpensTheRadialMenuEditor_OnTheMenuOfThePenButtonThatHasIt()
    {
        var (vm, view, window, _) = Open(NewProfile(Name(), new PluginSettingStore?[] { null, Radial("Zoomy") }));
        RadialMenuEditorDialog? dialog = null;
        try
        {
            Find<Button>(view, "RadialSpotButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));   // the real click handler
            await PumpUntil(() => window.OwnedWindows.OfType<RadialMenuEditorDialog>().Any());
            dialog = window.OwnedWindows.OfType<RadialMenuEditorDialog>().First();   // the same editor as Change… › Radial menu › Edit menu…
            var editor = dialog.DataContext as RadialMenuEditorViewModel;
            Assert.NotNull(editor);
            Assert.Equal("Zoomy", editor!.Labels[0]);              // the menu of pen button 2
        }
        finally
        {
            dialog?.Close();
            await Pump(60);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task AnEditorChange_IsAppliedToTheSelectedTable_AndThePictureShowsIt_CancelChangesNothing()
    {
        var tablet = Name();
        var daemon = new FakeDaemonTransport { Settings = Document(tablet, 40, 50) };
        daemon.GetSettingsHandler = async () => { await Task.Yield(); return daemon.Settings; };
        var store = new MemorySettingsFileStore { Saved = Document(tablet, 40, 50) };
        using var app = new AppSession(FakeSession.Over(daemon, store), new FakeLifecycle());
        daemon.Reconnect();
        await PumpUntil(() => app.CurrentSettings is not null);
        var settings = app.CurrentSettings!;
        var (vm, view, window, _) = Open(settings.Profiles[0], apply: app.ApplySettingsAsync, settings: settings);
        try
        {
            string? seen = null;
            Assert.False(await vm.EditRadialMenuAsync(json => { seen = json; return Task.FromResult<string?>(null); }));   // Cancel
            Assert.False(app.HasUnsavedChanges);
            Assert.NotNull(seen);

            Assert.True(await vm.EditRadialMenuAsync(json =>
            {
                var m = RadialMenuJson.Parse(json); m.Slices[0].Label = "After";
                return Task.FromResult<string?>(RadialMenuJson.Serialize(m));
            }));
            await PumpUntil(() => !app.SettingsBusy && app.SaveState != SettingsSaveState.Applying);
            Settle(window);
            Assert.Equal("After", RadialMenuBinding.ReadMenu(app.CurrentSettings!.Profiles[0].BindingSettings.PenButtons[1]).Slices[0].Label);
            Assert.Equal("After", Find<RadialMenuPie>(view, "RadialSpotPie").Labels![0]);
            Assert.True(app.HasUnsavedChanges);                       // kept by the footer's Save
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task EditingWhereNothingOpensTheMenu_OrOnAReadOnlyPage_IsLogged_NotSwallowed()
    {
        var none = NewProfile(Name(), new PluginSettingStore?[] { null, AuxKeyBinding.MakeKeyBinding("E") });
        var (vm, _, window, _) = Open(none);
        var ro = NewProfile(Name(), new PluginSettingStore?[] { null, Radial("A") });
        var (vm2, _, window2, _) = Open(ro, readOnly: true);
        try
        {
            var lines = await Logged(async () =>
            {
                Assert.False(await vm.EditRadialMenuAsync(_ => Task.FromResult<string?>(null)));
                Assert.False(await vm2.EditRadialMenuAsync(_ => Task.FromResult<string?>(null)));
                Assert.False(await vm2.EditRadialMenuAsync(_ => throw new InvalidOperationException("boom")));
            });
            Assert.Contains(lines, l => l.Contains("no pen button opens the radial menu"));
            Assert.Contains(lines, l => l.Contains("read-only"));
        }
        finally { window.Close(); window2.Close(); }
    }

    [AvaloniaFact]
    public async Task AnEditorThatThrows_IsLoggedWithTheException()
    {
        var (vm, _, window, _) = Open(NewProfile(Name(), new PluginSettingStore?[] { null, Radial("A") }));
        try
        {
            var lines = await Logged(async () => Assert.False(await vm.EditRadialMenuAsync(_ => throw new InvalidOperationException("boom-editor"))));
            Assert.Contains(lines, l => l.Contains("editing the radial menu failed") && l.Contains("boom-editor"));
        }
        finally { window.Close(); }
    }

    // ── the sliders ──────────────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task TransparencySlider_ChangesThePictureLive_AndTheNumberBoxBehavesLikeTheEditors()
    {
        var (vm, view, window, _) = Open(NewProfile(Name(), new PluginSettingStore?[] { null, Radial("A") }));
        try
        {
            var slider = Find<Slider>(view, "RadialTransparencySlider"); var box = Find<TextBox>(view, "RadialTransparencyBox");
            var pie = Find<RadialMenuPie>(view, "RadialSpotPie");
            Assert.Equal(0, slider.Minimum); Assert.Equal(100, slider.Maximum);
            Assert.Equal(40, (int)slider.Value); Assert.Equal("40", box.Text);

            slider.Value = 10; Settle(window);
            Assert.Equal(10, pie.Transparency);                        // live in the picture
            Assert.Equal(RadialMenuDef.OpacityFor(10), pie.Opacity);    // the whole picture fades (v27)
            Assert.Equal("10", box.Text);                              // synced
            Assert.NotEqual(RadialMenuDef.OpacityFor(0), pie.Opacity);

            vm.RadialPreview!.TransparencyInput = "75%"; Settle(window);   // typing, as the editor accepts it
            Assert.Equal(75, (int)slider.Value); Assert.Equal(75, pie.Transparency);
            vm.RadialPreview.TransparencyInput = "abc"; Settle(window);
            Assert.Equal("75", box.Text);                              // not a number: the old value comes back
            vm.RadialPreview.TransparencyInput = "250"; Settle(window);
            Assert.Equal(100, (int)slider.Value);                      // clamped

            box.Focus(); Settle(window);
            box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Down });
            Settle(window);
            Assert.Equal(99, (int)slider.Value); Assert.Equal("99", box.Text);   // Down changes it by 1
            box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Up });
            Assert.Equal(100, (int)slider.Value);
            box.Text = "33";
            box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });   // Enter applies
            Settle(window);
            Assert.Equal(33, (int)slider.Value); Assert.Equal(33, pie.Transparency);
            await Pump(TabletDetailViewModel.RadialApplyDelay.Milliseconds + 80);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task SizeSlider_ChangesThePictureLive_AndNeverGoesAboveTheLargestMenu()
    {
        var (vm, view, window, _) = Open(NewProfile(Name(), new PluginSettingStore?[] { null, Radial("A") }));
        try
        {
            var slider = Find<Slider>(view, "RadialSizeSlider"); var number = Find<TextBox>(view, "RadialSizeBox");
            var box = Find<Viewbox>(view, "RadialSpotViewbox");
            Assert.Equal(RadialMenuDef.MaxSize, (int)slider.Maximum);
            Assert.Equal(RadialMenuDef.MaxSize, 50);
            Assert.Equal(320, 2 * RadialMenuGeometry.OuterRadius(RadialMenuDef.MaxSize), 3);   // the picture in Zak's screenshot: 320 pt across
            Assert.Equal(320, 2 * RadialMenuGeometry.OuterRadius(100), 3);                     // nothing bigger, whatever number is stored
            Assert.Equal("50", number.Text);
            double large = box.Width;
            Assert.Equal(TabletDetailViewModel.RadialPictureMax, large, 1);

            slider.Value = 10; Settle(window);
            Assert.Equal("10", number.Text);                          // the number box follows the slider
            Assert.True(box.Width < large - 30, $"the picture did not get smaller: {box.Width} vs {large}");
            vm.RadialPreview!.SizeInput = "25"; Settle(window);       // typing, as transparency does
            Assert.Equal(25, (int)slider.Value);
            Assert.Equal("25", number.Text);
            vm.RadialPreview.SizeInput = "abc"; Settle(window);
            Assert.Equal("25", number.Text);                          // not a number: the old value comes back
            vm.RadialPreview.SizeInput = "87"; Settle(window);
            Assert.Equal(RadialMenuDef.MaxSize, (int)slider.Value);    // clamped to 1-50
            Assert.Equal("50", number.Text);
            number.Focus(); Settle(window);
            number.Text = "8";
            number.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Settle(window);
            Assert.Equal(8, (int)slider.Value); Assert.Equal("8", number.Text);   // Enter applies
            slider.Value = 1000; Settle(window);                          // above the range: held at the largest
            Assert.Equal(RadialMenuDef.MaxSize, (int)slider.Value);
            Assert.Equal("50", number.Text);
            Assert.Equal(large, box.Width, 1);
            await Pump(TabletDetailViewModel.RadialApplyDelay.Milliseconds + 80);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ASliderChange_IsSentOncePerDrag_AppliedToTheTable_AndARefreshKeepsWhatWasSet()
    {
        int applies = 0;
        var profile = NewProfile(Name(), new PluginSettingStore?[] { null, Radial("A") });
        var (vm, view, window, _) = Open(profile, apply: _ => { applies++; return Task.FromResult(SettingsApplyOutcome.Live); });
        try
        {
            var slider = Find<Slider>(view, "RadialTransparencySlider");
            foreach (var v in new[] { 30, 31, 32, 33 }) slider.Value = v;
            Find<Slider>(view, "RadialSizeSlider").Value = 20;
            Assert.Equal(0, applies);
            vm.RefreshRadialSpot();                                        // a refresh while the change is on its way keeps the sliders
            Assert.Equal(33, (int)slider.Value);
            await PumpUntil(() => applies >= 1);
            await Pump(TabletDetailViewModel.RadialApplyDelay.Milliseconds + 100);
            Assert.Equal(1, applies);
            var stored = RadialMenuBinding.ReadMenu(profile.BindingSettings.PenButtons[1]);
            Assert.Equal(33, stored.Transparency); Assert.Equal(20, stored.Size);
        }
        finally { window.Close(); }
    }

    private static Settings Document(string tablet, int transparency, int size)
    {
        var menu = RadialMenuDefaults.Create(Localizer.Te); menu.Transparency = transparency; menu.Size = size;
        var profile = NewProfile(tablet, new PluginSettingStore?[] { null, RadialMenuBinding.Make(menu) });
        profile.AbsoluteModeSettings = new AbsoluteModeSettings
        {
            Display = new AreaSettings { Width = 1920, Height = 1080, X = 960, Y = 540 },
            Tablet = new AreaSettings { Width = 100, Height = 60, X = 50, Y = 30 },
        };
        return new Settings { Profiles = new ProfileCollection { profile } };
    }

    [AvaloniaFact]
    public async Task SliderChanges_MarkUnsaved_AndSaveKeepsThem()
    {
        var tablet = Name();
        var daemon = new FakeDaemonTransport { Settings = Document(tablet, 40, 50) };
        daemon.GetSettingsHandler = async () => { await Task.Yield(); return daemon.Settings; };
        var store = new MemorySettingsFileStore { Saved = Document(tablet, 40, 50) };
        using var app = new AppSession(FakeSession.Over(daemon, store), new FakeLifecycle());
        daemon.Reconnect();
        await PumpUntil(() => app.CurrentSettings is not null);

        var settings = app.CurrentSettings!;
        var (vm, view, window, _) = Open(settings.Profiles[0], apply: app.ApplySettingsAsync, settings: settings);
        try
        {
            Assert.False(app.HasUnsavedChanges);
            Find<Slider>(view, "RadialTransparencySlider").Value = 12;
            Find<Slider>(view, "RadialSizeSlider").Value = 25;
            await vm.FlushRadialSpotAsync();
            await PumpUntil(() => !app.SettingsBusy && app.SaveState != SettingsSaveState.Applying);

            var stored = RadialMenuBinding.ReadMenu(app.CurrentSettings!.Profiles[0].BindingSettings.PenButtons[1]);
            Assert.Equal(12, stored.Transparency); Assert.Equal(25, stored.Size);
            Assert.True(app.HasUnsavedChanges);                       // like the rest of the page: kept by the footer's Save
            Assert.Equal(40, RadialMenuBinding.ReadMenu(store.Saved!.Profiles[0].BindingSettings.PenButtons[1]).Transparency);

            Assert.True(await app.SaveSettingsAsync());
            Assert.False(app.HasUnsavedChanges);
            var saved = RadialMenuBinding.ReadMenu(store.Saved!.Profiles[0].BindingSettings.PenButtons[1]);
            Assert.Equal(12, saved.Transparency); Assert.Equal(25, saved.Size);
            Assert.Equal(12, (int)Find<Slider>(view, "RadialTransparencySlider").Value);   // the page still shows it after the save refreshed it
            Assert.Equal(25, (int)Find<Slider>(view, "RadialSizeSlider").Value);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task AChangeTheDriverDoesNotConfirm_IsLogged_AndTheSlidersShowTheWorkingCopy()
    {
        var profile = NewProfile(Name(), new PluginSettingStore?[] { null, Radial("A") });
        var (vm, view, window, _) = Open(profile, apply: _ => Task.FromResult(SettingsApplyOutcome.Live));   // Live but nothing prepared: not adopted
        try
        {
            var lines = await Logged(async () =>
            {
                Find<Slider>(view, "RadialTransparencySlider").Value = 5;
                await vm.FlushRadialSpotAsync();
                await Pump(60);
            });
            Assert.Contains(lines, l => l.Contains("size / transparency change was NOT applied"));
            Assert.Contains(lines, l => l.Contains("Settings change was not adopted"));
            // like every other control of the page, the edit stays in the page's working copy (unsaved); the sliders show exactly that
            Assert.Equal(5, RadialMenuBinding.ReadMenu(profile.BindingSettings.PenButtons[1]).Transparency);
            Assert.Equal(5, (int)Find<Slider>(view, "RadialTransparencySlider").Value);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ASavedSizeAboveTheLargest_IsUsedAsTheLargest_WithOneLogLine()
    {
        var menu = RadialMenuDefaults.Create(Localizer.Te); menu.Size = 50;
        var json = RadialMenuJson.Serialize(menu).Replace("\"size\":50", "\"size\":87");
        Assert.Contains("\"size\":87", json);
        var lines = await Logged(() => { RadialMenuJson.Parse(json); RadialMenuJson.Parse(json); return Task.CompletedTask; });
        Assert.Equal(RadialMenuDef.MaxSize, RadialMenuJson.Parse(json).Size);
        Assert.True(lines.Count(l => l.Contains("saved menu size of 87")) <= 1);   // at most one line per value and run
        Assert.Equal(RadialMenuDef.MaxSize, new RadialMenuEditorViewModel(json, "x").Size);
    }

    // ── fit, no overlap, pen test still works ───────────────────────────────────────────────────────────

    [AvaloniaTheory]
    [InlineData(1100, 720, true)]
    [InlineData(900, 600, true)]
    [InlineData(1100, 720, false)]
    [InlineData(900, 600, false)]
    [InlineData(1500, 1000, true)]
    public async Task Fits_NothingOverlaps_AndThePenButtonTestStillWorks(double w, double h, bool hasMenu)
    {
        var profile = NewProfile(Name(), new PluginSettingStore?[] { null, hasMenu ? Radial("A") : AuxKeyBinding.MakeKeyBinding("E") });
        var (vm, view, window, _) = Open(profile, w, h);
        try
        {
            var page = view.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.IsEffectivelyVisible && s.Classes.Contains("tabScroll"));
            var spot = Find<StackPanel>(view, "RadialSpot");
            var test = Find<TextBlock>(view, "PenButtonTestLine");
            var diagram = view.GetVisualDescendants().OfType<PenDiagram>().First(d => d.IsEffectivelyVisible);
            var cards = view.GetVisualDescendants().OfType<PenSwitchCard>().Where(c => c.IsEffectivelyVisible).ToList();
            Assert.NotEmpty(cards);
            var spotRect = In(spot, page);
            // the spot (picture, line, button, sliders) stays inside the page's width and does not overlap the pen buttons or the pen drawing
            Assert.True(spotRect.Left >= -0.5 && spotRect.Right <= page.Viewport.Width + 0.5, $"spot {spotRect} sticks out of the page ({page.Viewport.Width}) at {w}x{h}");
            foreach (var c in cards) Assert.False(spotRect.Intersects(In(c, page)), $"spot {spotRect} overlaps a pen button card {In(c, page)}");
            Assert.False(spotRect.Intersects(In(diagram, page)));
            Assert.False(spotRect.Intersects(In(test, page)));
            // the pen buttons are in the middle: between the pen drawing and the spot
            foreach (var c in cards) { Assert.True(In(c, page).Left >= In(diagram, page).Right - 0.5); Assert.True(In(c, page).Right <= spotRect.Left + 0.5); }
            // inside the spot the parts do not overlap each other either
            var rects = new[] { "RadialSpotHeading", "RadialSpotButton", "RadialSpotLine", "RadialSliders" }.Select(n => In(Find<Control>(view, n), page)).ToList();
            for (int i = 0; i < rects.Count - 1; i++) Assert.True(rects[i].Bottom <= rects[i + 1].Top + 0.5, $"spot part {i} overlaps part {i + 1}: {rects[i]} / {rects[i + 1]}");
            if (!hasMenu) { var useRect = In(Find<Button>(view, "RadialUseButton"), page); Assert.True(useRect.Bottom <= In(Find<StackPanel>(view, "RadialSliders"), page).Top + 0.5); }
            // the picture is never bigger than the largest menu (190 pt for 320 pt)
            Assert.True(Find<RadialMenuPie>(view, "RadialSpotPie").Bounds.Width <= 220 + 0.5);
            Assert.True(Find<Viewbox>(view, "RadialSpotViewbox").Bounds.Width <= TabletDetailViewModel.RadialPictureMax + 0.5);
            // a short window scrolls instead of squeezing; scrolled down, the sliders are reachable
            page.ScrollToEnd(); Settle(window);
            Assert.True(In(Find<StackPanel>(view, "RadialSliders"), page).Bottom <= page.Viewport.Height + 0.5 + page.Extent.Height, "sliders unreachable");

            // the pen button test works as before: holding button 2 changes the line and lights the badge
            Assert.Equal("Press a pen button to test it.", test.Text);
            vm.PenButton2Pressed = true; Settle(window);
            Assert.Equal("Button 2 pressed.", test.Text);
            Assert.Equal("Button 2 pressed.", vm.PenButtonTestText);
            vm.PenButton2Pressed = false; Settle(window);
            Assert.Equal("Press a pen button to test it.", test.Text);
            await Pump();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheStrings_ExistInAll8Languages()
    {
        var keys = new[] { "radial_spot.heading", "radial_spot.opens_with", "radial_spot.none", "radial_spot.use_button", "radial_spot.pen_button",
            "radial.centre.close", "radial.centre.edit", "radial.centre.restore", "radial.centre.hand" };
        var dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "OpenTabletArtist", "Assets", "i18n"));
        foreach (var code in new[] { "en", "bg", "de", "es", "fr", "ja", "ru", "zh-CN" })
        {
            var json = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(dir, code + ".json")));
            foreach (var k in keys) Assert.False(string.IsNullOrWhiteSpace((string?)json[k]), $"{code} misses {k}");
            Assert.Contains("{buttons}", (string)json["radial_spot.opens_with"]!);
            foreach (var k in new[] { "radial_spot.use_button", "radial_spot.pen_button" }) Assert.Contains("{number}", (string)json[k]!);
            if (code != "en")
                foreach (var k in new[] { "radial.centre.close", "radial.centre.edit", "radial.centre.restore", "radial.centre.hand" })
                    Assert.NotEqual((string)json[k]!, (string)Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(dir, "en.json")))[k]!);
        }
    }

    [AvaloniaFact]
    public async Task ClickingACentreIcon_ShowsWhatItDoesUnderThePicture_Logs_AndDoesNotOpenTheEditor()
    {
        var (vm, view, window, _) = Open(NewProfile(Name(), new PluginSettingStore?[] { null, Radial("Zoomy") }));
        try
        {
            var pie = Find<RadialMenuPie>(view, "RadialSpotPie");
            var hint = Find<TextBlock>(view, "RadialCentreHint");
            var btn = Find<Button>(view, "RadialSpotButton");
            Assert.False(hint.IsVisible);

            Point At(int code)
            {
                var c = new Point(pie.Bounds.Width / 2, pie.Bounds.Height / 2);
                double outer = Math.Max(10, Math.Min(pie.Bounds.Width, pie.Bounds.Height) / 2 - 2);
                double d = outer * RadialMenuGeometry.InnerRatio * 0.45;
                return code switch
                {
                    RadialMenuGeometry.Centre => new Point(c.X - d, c.Y - d),
                    RadialMenuGeometry.Edit => new Point(c.X + d, c.Y - d),
                    RadialMenuGeometry.Restore => new Point(c.X - d, c.Y + d),
                    _ => new Point(c.X + d, c.Y + d),
                };
            }

            Assert.Equal(RadialMenuGeometry.Centre, pie.CentreAt(At(RadialMenuGeometry.Centre)));
            Assert.Equal(RadialMenuGeometry.Edit, pie.CentreAt(At(RadialMenuGeometry.Edit)));
            Assert.Equal(RadialMenuGeometry.Restore, pie.CentreAt(At(RadialMenuGeometry.Restore)));
            Assert.Equal(RadialMenuGeometry.Hand, pie.CentreAt(At(RadialMenuGeometry.Hand)));

            var lines = await Logged(async () =>
            {
                Assert.True(pie.HandleClick(At(RadialMenuGeometry.Centre)));
                Settle(window);
                await Pump(30);
            });
            Assert.Equal("Closes the menu.", vm.RadialPreview!.CentreHint);
            Assert.True(hint.IsVisible);
            Assert.Equal("Closes the menu.", hint.Text);
            Assert.Contains(lines, l => l.Contains("the X in the picture was clicked") && l.Contains("Closes the menu."));
            Assert.Empty(window.OwnedWindows.OfType<RadialMenuEditorDialog>());
            Assert.True(In(btn, view).Bottom <= In(hint, view).Top + 0.5);
            Assert.True(In(hint, view).Bottom <= In(Find<TextBlock>(view, "RadialSpotLine"), view).Top + 0.5);

            Assert.True(pie.HandleClick(At(RadialMenuGeometry.Hand)));
            Settle(window);
            Assert.Equal("Drag to move the menu.", hint.Text);
            Assert.Empty(window.OwnedWindows.OfType<RadialMenuEditorDialog>());
        }
        finally { window.Close(); }
    }
}
