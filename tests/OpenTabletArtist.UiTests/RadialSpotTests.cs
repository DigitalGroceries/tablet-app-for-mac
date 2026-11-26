using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
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

/// <summary>TASKS #63 (Digital Groceries 20:53 / 21:00 / 21:02): Tablet › Pen › "Radial Menu Settings": the radial menu's picture on the right (label above it,
/// nothing else above), the line (V30 build 4: an Off / On switch above the corner instead of "Use pen button 2") and the Menu transparency / Menu size sliders below it; the pen buttons in the middle next to
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

    // both corners, so a control inside a scaling Viewbox (the picture in a narrow window) is measured as drawn
    private static Rect In(Visual v, Visual root) =>
        new Rect(v.TranslatePoint(new Point(0, 0), root) ?? default, v.TranslatePoint(new Point(v.Bounds.Width, v.Bounds.Height), root) ?? default);

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
            // V29 #180 (Digital Groceries' Pen page mockup, 21 Nov): no title above the picture; the picture is the first (top) thing.
            Assert.Null(view.FindControl<TextBlock>("RadialSpotHeading"));
            // V30 build 5 (Digital Groceries 24 Nov, build 4 test: "On" was cut off): the switch row sits above the scaling picture, not inside it.
            Assert.Same(Find<StackPanel>(view, "RadialSwitchRow"), spot.Children[0]);
            Assert.Same(Find<Viewbox>(view, "RadialSpotFit"), spot.Children[1]);
            var pie = Find<RadialMenuPie>(view, "RadialSpotPie");
            Assert.Equal("Zoomy", pie.Labels![0]);                        // this application's menu, not a fixed picture
            var line = Find<TextBlock>(view, "RadialSpotLine"); var sliders = Find<StackPanel>(view, "RadialSliders");
            var btn = Find<Button>(view, "RadialSpotButton");
            Assert.True(In(btn, view).Bottom <= In(line, view).Top + 0.5);
            Assert.True(In(line, view).Bottom <= In(sliders, view).Top + 0.5);          // the line, then the sliders, below the picture
            Assert.Null(view.FindControl<RadioButton>("RadialTab"));                    // no tab of its own
            Assert.Equal("By default Radial Menu opens with Pen Button 2", line.Text);   // V29 #180 (Digital Groceries 21 Nov)
            Assert.Null(view.FindControl<Button>("RadialUseButton"));   // V30 build 4: the switch does its job
            Assert.Equal(1.0, Find<Button>(view, "RadialSpotButton").Opacity);
            Assert.True(sliders.IsEnabled);
            // V30 build 4 (Digital Groceries 24 Nov ~15:44-15:50): "Radial menu  Off (switch) On", outside, next to the square's top right corner.
            var sw = Find<ToggleSwitch>(view, "RadialSwitch");
            Assert.True(sw.IsChecked);
            Assert.True(sw.IsEnabled);
            var row = In(Find<StackPanel>(view, "RadialSwitchRow"), view); var square = In(Find<Grid>(view, "RadialSpotArea"), view);
            Assert.True(row.Bottom <= square.Top + 0.5, $"switch row {row} is above the square {square}");
            Assert.True(Math.Abs(row.Right - square.Right) < 2, $"switch row {row} ends at the square's right edge {square}");
            Assert.Equal("Radial menu", Find<TextBlock>(view, "RadialSwitchLabel").Text);
            Assert.True(Find<ToggleSwitch>(view, "PaletteSwitch").IsChecked);   // the factory menu opens the palette on a double press
        }
        finally { window.Close(); }
    }

    /// <summary>V30 build 4 (Digital Groceries 24 Nov ~15:44: "They should never be grayed out. Use different way to show that they are not setup. Add a
    /// label next to then, a swithch with On on one side and Off on the other. When the user turns the switch on the app should ask them
    /// what buttons should open this menu."): nothing opens the radial menu - full strength, switch Off; On asks, Off gives the button back.</summary>
    [AvaloniaFact]
    public async Task NothingOnTheRadialMenu_FullStrength_SwitchOff_OnAsksWhichButton_OffGivesItBack()
    {
        var profile = NewProfile(Name(), new PluginSettingStore?[] { null, AuxKeyBinding.MakeKeyBinding("E") });
        var (vm, view, window, _) = Open(profile);
        try
        {
            Assert.True(Find<StackPanel>(view, "RadialSpot").IsEffectivelyVisible);   // still shown
            Assert.Equal(1.0, Find<Button>(view, "RadialSpotButton").Opacity);        // never dimmed
            Assert.Equal(1.0, Find<StackPanel>(view, "RadialSliders").Opacity);
            Assert.True(Find<StackPanel>(view, "RadialSliders").IsEnabled);
            Assert.Equal(1.0, Find<StackPanel>(view, "PaletteSliders").Opacity);
            Assert.Equal("No pen button opens the radial menu", Find<TextBlock>(view, "RadialSpotLine").Text);
            Assert.Equal("No pen button opens the colour palette", Find<TextBlock>(view, "PaletteSpotLine").Text);
            Assert.Null(view.FindControl<Button>("RadialUseButton"));
            var sw = Find<ToggleSwitch>(view, "RadialSwitch");
            Assert.False(sw.IsChecked);
            Assert.False(Find<ToggleSwitch>(view, "PaletteSwitch").IsChecked);

            // On: the page asks which pen button (Cancel leaves it off)
            IReadOnlyList<string>? asked = null; string? title = null; int? answer = null;
            vm.ChooseMenuButton = (t, _, c) => { title = t; asked = c; return Task.FromResult(answer); };
            sw.IsChecked = true; await Pump(100); Settle(window);
            Assert.Equal("Which pen button opens the radial menu?", title);
            Assert.Equal(new[] { "Pen button 1", "Pen button 2" }, asked);
            Assert.False(sw.IsChecked);                                         // cancelled: off again
            Assert.True(AuxKeyBinding.ReadBinding(profile.BindingSettings.PenButtons[1]) is { IsBound: true });

            answer = 1;                                                         // Pen button 2
            sw.IsChecked = true; await Pump(100); Settle(window);
            Assert.True(RadialMenuBinding.Is(profile.BindingSettings.PenButtons[1]));
            Assert.True(sw.IsChecked);
            Assert.Equal("By default Radial Menu opens with Pen Button 2", Find<TextBlock>(view, "RadialSpotLine").Text);
            Assert.True(Find<Button>(view, "RadialSpotButton").IsEnabled);

            // Off: pen button 2 does what it did before (E), not the radial menu
            sw.IsChecked = false; await Pump(100); Settle(window);
            Assert.False(RadialMenuBinding.Is(profile.BindingSettings.PenButtons[1]));
            Assert.Equal(AuxKeyBinding.ReadBinding(AuxKeyBinding.MakeKeyBinding("E")), AuxKeyBinding.ReadBinding(profile.BindingSettings.PenButtons[1]));
            Assert.False(sw.IsChecked);
            Assert.Equal("No pen button opens the radial menu", Find<TextBlock>(view, "RadialSpotLine").Text);
        }
        finally { window.Close(); }
    }

    /// <summary>V30 build 4: the colour palette's own switch. Off keeps the radial menu and stops the double press opening the palette; On asks
    /// which pen button (pressed twice) and gives that button the palette back.</summary>
    [AvaloniaFact]
    public async Task ThePaletteSwitch_OffKeepsTheMenu_OnAsksAndPutsItBack()
    {
        var profile = NewProfile(Name(), new PluginSettingStore?[] { null, Radial("Undo") });
        var (vm, view, window, _) = Open(profile);
        try
        {
            var pal = Find<ToggleSwitch>(view, "PaletteSwitch");
            Assert.True(pal.IsChecked);
            Assert.Equal("By default Colour Palette opens with Pen button 2 double click", Find<TextBlock>(view, "PaletteSpotLine").Text);
            pal.IsChecked = false; await Pump(100); Settle(window);
            Assert.True(RadialMenuBinding.Is(profile.BindingSettings.PenButtons[1]));        // the radial menu stays
            Assert.False(RadialMenuBinding.OpensPalette(profile.BindingSettings.PenButtons[1]));
            Assert.Equal("Undo", RadialMenuBinding.ReadMenu(profile.BindingSettings.PenButtons[1]).Slices[0].Label);
            Assert.True(Find<ToggleSwitch>(view, "RadialSwitch").IsChecked);
            Assert.Equal("No pen button opens the colour palette", Find<TextBlock>(view, "PaletteSpotLine").Text);

            IReadOnlyList<string>? asked = null;
            vm.ChooseMenuButton = (_, _, c) => { asked = c; return Task.FromResult<int?>(1); };
            pal.IsChecked = true; await Pump(100); Settle(window);
            Assert.Equal(new[] { "Pen button 1, pressed twice", "Pen button 2, pressed twice" }, asked);
            Assert.True(RadialMenuBinding.OpensPalette(profile.BindingSettings.PenButtons[1]));
            Assert.Equal("Undo", RadialMenuBinding.ReadMenu(profile.BindingSettings.PenButtons[1]).Slices[0].Label);
            Assert.True(pal.IsChecked);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task APenWithOneButton_IsOfferedButton1()
    {
        var (vm, view, window, _) = Open(NewProfile(Name(), new PluginSettingStore?[] { null }));
        try
        {
            IReadOnlyList<string>? asked = null;
            vm.ChooseMenuButton = (_, _, c) => { asked = c; return Task.FromResult<int?>(null); };
            Find<ToggleSwitch>(view, "RadialSwitch").IsChecked = true; await Pump(100);
            Assert.Equal(new[] { "Pen button 1" }, asked);
        }
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
            Assert.Equal("No pen button opens the radial menu", Find<TextBlock>(view, "RadialSpotLine").Text);
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

    /// <summary>v27 (Digital Groceries 16:05): the Menu transparency and Menu size boxes line up (same left edge, same width) and the sliders are
    /// the same length; only transparency has the % sign (the size is a step from 1 to 50, not a percentage).</summary>
    [AvaloniaFact]
    public void TheTwoNumberBoxes_LineUp_AndTheSliderLinesEndTogether()
    {
        var (_, view, window, _) = Open(NewProfile(Name(), new PluginSettingStore?[] { null, Radial("A") }));
        try
        {
            Settle(window);
            var tBox = Find<TextBox>(view, "RadialTransparencyBox"); var sBox = Find<TextBox>(view, "RadialSizeBox");
            var tSlider = Find<Slider>(view, "RadialTransparencySlider"); var sSlider = Find<Slider>(view, "RadialSizeSlider");
            var t = tBox.TranslatePoint(new Point(0, 0), view)!.Value; var z = sBox.TranslatePoint(new Point(0, 0), view)!.Value;
            Assert.Equal(t.X, z.X, 1);
            Assert.Equal(tBox.Bounds.Width, sBox.Bounds.Width, 1);
            // build 17 (Digital Groceries' list 1, item 7): the transparency slider is half a knob shorter, so its line ends where the size
            // slider's line ends at 10 (under the knob's centre); V29List1Tests.Item7 checks that against the knob itself
            Assert.Equal(sSlider.Bounds.Width - 10, tSlider.Bounds.Width, 1);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task TransparencySlider_ChangesThePictureLive_AndTheNumberBoxBehavesLikeTheEditors()
    {
        var (vm, view, window, _) = Open(NewProfile(Name(), new PluginSettingStore?[] { null, Radial("A") }));
        try
        {
            var slider = Find<Slider>(view, "RadialTransparencySlider"); var box = Find<TextBox>(view, "RadialTransparencyBox");
            var pie = Find<RadialMenuPie>(view, "RadialSpotPie");
            Assert.Equal(0, slider.Minimum); Assert.Equal(60, slider.Maximum);   // v27 (Digital Groceries 16:01): 60% is the maximum
            Assert.Equal(40, (int)slider.Value); Assert.Equal("40", box.Text);

            slider.Value = 10; Settle(window);
            Assert.Equal(10, pie.Transparency);                        // live in the picture
            Assert.Equal(RadialMenuDef.OpacityFor(10), pie.Opacity);    // the whole picture fades (v27)
            Assert.Equal("10", box.Text);                              // synced
            Assert.NotEqual(RadialMenuDef.OpacityFor(0), pie.Opacity);

            vm.RadialPreview!.TransparencyInput = "55%"; Settle(window);   // typing, as the editor accepts it
            Assert.Equal(55, (int)slider.Value); Assert.Equal(55, pie.Transparency);
            vm.RadialPreview.TransparencyInput = "abc"; Settle(window);
            Assert.Equal("55", box.Text);                              // not a number: the old value comes back
            vm.RadialPreview.TransparencyInput = "250"; Settle(window);
            Assert.Equal(60, (int)slider.Value);                       // clamped to the 60% limit

            box.Focus(); Settle(window);
            box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Down });
            Settle(window);
            Assert.Equal(59, (int)slider.Value); Assert.Equal("59", box.Text);   // Down changes it by 1
            box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Up });
            Assert.Equal(60, (int)slider.Value);
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
            Assert.Equal(RadialMenuDef.MaxSizeStep, (int)slider.Maximum);   // v27 (Digital Groceries 16:07): the size is set from 1 to 10
            Assert.Equal(RadialMenuDef.MaxSize, 50);
            Assert.Equal(320, 2 * RadialMenuGeometry.OuterRadius(RadialMenuDef.MaxSize), 3);   // the picture in Digital Groceries' screenshot: 320 pt across
            Assert.Equal(320, 2 * RadialMenuGeometry.OuterRadius(100), 3);                     // nothing bigger, whatever number is stored
            Assert.Equal("10", number.Text);
            double large = box.Width;
            Assert.Equal(TabletDetailViewModel.RadialPictureMax, large, 1);
            var sliders = Find<StackPanel>(view, "RadialSliders"); var spot = Find<StackPanel>(view, "RadialSpot");
            AssertRealSize(view, vm.RadialPreview!.Size);                   // v27 (Digital Groceries 16:35): the picture is the menu's real size
            // measured inside the radial column, so a page scroll (focusing the number box scrolls it into view) does not count as a move
            double slidersTop = In(sliders, spot).Top, slidersLeft = In(sliders, spot).Left;

            slider.Value = 2; Settle(window);
            Assert.Equal("2", number.Text);                           // the number box follows the slider
            Assert.True(box.Width < large - 30, $"the picture did not get smaller: {box.Width} vs {large}");
            AssertRealSize(view, vm.RadialPreview.Size);
            // the square kept for the largest menu does not shrink, so the sliders stay where they are while the size is dragged
            Assert.Equal(slidersTop, In(sliders, spot).Top, 1);
            Assert.Equal(slidersLeft, In(sliders, spot).Left, 1);
            vm.RadialPreview!.SizeInput = "5"; Settle(window);        // typing, as transparency does
            Assert.Equal(5, (int)slider.Value);
            Assert.Equal("5", number.Text);
            Assert.Equal(RadialMenuDef.SizeForStep(5), vm.RadialPreview.Size);
            vm.RadialPreview.SizeInput = "abc"; Settle(window);
            Assert.Equal("5", number.Text);                           // not a number: the old value comes back
            vm.RadialPreview.SizeInput = "87"; Settle(window);
            Assert.Equal(RadialMenuDef.MaxSizeStep, (int)slider.Value);   // clamped to 1-10
            Assert.Equal("10", number.Text);
            number.Focus(); Settle(window);
            number.Text = "8";
            number.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Settle(window);
            Assert.Equal(8, (int)slider.Value); Assert.Equal("8", number.Text);   // Enter applies
            slider.Value = 1000; Settle(window);                          // above the range: held at the largest
            Assert.Equal(RadialMenuDef.MaxSizeStep, (int)slider.Value);
            Assert.Equal("10", number.Text);
            Assert.Equal(large, box.Width, 1);
            AssertRealSize(view, vm.RadialPreview.Size);
            Assert.Equal(slidersTop, In(sliders, spot).Top, 1);
            await Pump(TabletDetailViewModel.RadialApplyDelay.Milliseconds + 80);
        }
        finally { window.Close(); }
    }

    /// <summary>The picture is drawn exactly as big as the menu the pen opens: the same edge as the menu window (2 x outer radius + 4,
    /// RadialMenuOverlay.Show) and nothing scales it, so it is the real size on the screen.</summary>
    private static void AssertRealSize(TabletDetailView view, int size)
    {
        double real = 2 * RadialMenuGeometry.OuterRadius(size) + 4;
        var pie = Find<RadialMenuPie>(view, "RadialSpotPie");
        // within half a point: layout rounds to whole pixels, as the menu window's own size is
        Assert.Equal(real, pie.Bounds.Width, tolerance: 0.5);
        Assert.Equal(real, pie.Bounds.Height, tolerance: 0.5);
        var onScreen = In(pie, view);   // after every scale on the way up (the inner Viewbox and the fit Viewbox)
        Assert.Equal(real, onScreen.Width, tolerance: 0.5);
        Assert.Equal(real, onScreen.Height, tolerance: 0.5);
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
            Find<Slider>(view, "RadialSizeSlider").Value = 4;    // step 4 (v27: 1-10) = size 17
            Assert.Equal(0, applies);
            vm.RefreshRadialSpot();                                        // a refresh while the change is on its way keeps the sliders
            Assert.Equal(33, (int)slider.Value);
            await PumpUntil(() => applies >= 1);
            await Pump(TabletDetailViewModel.RadialApplyDelay.Milliseconds + 100);
            Assert.Equal(1, applies);
            var stored = RadialMenuBinding.ReadMenu(profile.BindingSettings.PenButtons[1]);
            Assert.Equal(33, stored.Transparency); Assert.Equal(RadialMenuDef.SizeForStep(4), stored.Size);
        }
        finally { window.Close(); }
    }

    /// <summary>V29 #166 (Digital Groceries 20 Nov ~09:45: "The transparency and the size is out of synk and jump on different values than what I
    /// put them"): a page refresh after an apply used to build new sliders even when nothing differed, so a slider being dragged
    /// jumped. The picture and its sliders stay the same objects while they already show the stored menu.</summary>
    [AvaloniaFact]
    public async Task ARefreshAfterAnApply_KeepsTheSameSliders_WhenNothingDiffers()
    {
        int applies = 0;
        var profile = NewProfile(Name(), new PluginSettingStore?[] { null, Radial("A") });
        var (vm, view, window, _) = Open(profile, apply: _ => { applies++; return Task.FromResult(SettingsApplyOutcome.Live); });
        try
        {
            Find<Slider>(view, "RadialTransparencySlider").Value = 44;
            await PumpUntil(() => applies >= 1);
            await Pump(TabletDetailViewModel.RadialApplyDelay.Milliseconds + 100);
            var before = vm.RadialPreview;
            vm.RefreshRadialSpot();
            Assert.Same(before, vm.RadialPreview);
            Assert.Equal(44, (int)Find<Slider>(view, "RadialTransparencySlider").Value);
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
            Find<Slider>(view, "RadialSizeSlider").Value = 5;    // step 5 (v27: 1-10) = size 23
            await vm.FlushRadialSpotAsync();
            await PumpUntil(() => !app.SettingsBusy && app.SaveState != SettingsSaveState.Applying);

            var stored = RadialMenuBinding.ReadMenu(app.CurrentSettings!.Profiles[0].BindingSettings.PenButtons[1]);
            Assert.Equal(12, stored.Transparency); Assert.Equal(RadialMenuDef.SizeForStep(5), stored.Size);
            Assert.True(app.HasUnsavedChanges);                       // like the rest of the page: kept by the footer's Save
            Assert.Equal(40, RadialMenuBinding.ReadMenu(store.Saved!.Profiles[0].BindingSettings.PenButtons[1]).Transparency);

            Assert.True(await app.SaveSettingsAsync());
            Assert.False(app.HasUnsavedChanges);
            var saved = RadialMenuBinding.ReadMenu(store.Saved!.Profiles[0].BindingSettings.PenButtons[1]);
            Assert.Equal(12, saved.Transparency); Assert.Equal(RadialMenuDef.SizeForStep(5), saved.Size);
            Assert.Equal(12, (int)Find<Slider>(view, "RadialTransparencySlider").Value);   // the page still shows it after the save refreshed it
            Assert.Equal(5, (int)Find<Slider>(view, "RadialSizeSlider").Value);
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
    [InlineData(1046, 1365, true)]    // Digital Groceries' window in his 16:39 screenshot
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
            var rects = new[] { "RadialSpotButton", "RadialSpotLine", "RadialSliders" }.Select(n => In(Find<Control>(view, n), page)).ToList();
            for (int i = 0; i < rects.Count - 1; i++) Assert.True(rects[i].Bottom <= rects[i + 1].Top + 0.5, $"spot part {i} overlaps part {i + 1}: {rects[i]} / {rects[i + 1]}");
            Assert.True(In(Find<StackPanel>(view, "RadialSwitchRow"), page).Bottom <= In(Find<Grid>(view, "RadialSpotArea"), page).Top + 0.5);   // V30 build 4
            // the picture is never bigger than the largest menu (320 pt + the window's 4)
            Assert.True(Find<RadialMenuPie>(view, "RadialSpotPie").Bounds.Width <= TabletDetailViewModel.RadialPictureMax + 0.5);
            Assert.True(Find<Viewbox>(view, "RadialSpotViewbox").Bounds.Width <= TabletDetailViewModel.RadialPictureMax + 0.5);
            Assert.Equal(TabletDetailViewModel.RadialSpotAreaSize, Find<Grid>(view, "RadialSpotArea").Width);
            // v27 (Digital Groceries 16:39): in Digital Groceries' window size and larger the picture is the real size; only a narrower window shrinks the square to fit
            if (w >= 1046 && hasMenu) AssertRealSize(view, vm.RadialPreview!.Size);
            // a short window scrolls instead of squeezing; scrolled down, the sliders are reachable
            page.ScrollToEnd(); Settle(window);
            Assert.True(In(Find<StackPanel>(view, "RadialSliders"), page).Bottom <= page.Viewport.Height + 0.5 + page.Extent.Height, "sliders unreachable");

            // the pen button test works as before: holding button 2 changes the line and lights the badge
            Assert.Equal("PRESS A BUTTON TO TEST IT", test.Text);   // V29 #180: Digital Groceries' capitals, and the tip and eraser count too
            vm.PenButton2Pressed = true; Settle(window);
            Assert.Equal("BUTTON 2 PRESSED.", test.Text);
            Assert.Equal("Button 2 pressed.", vm.PenButtonTestText);
            vm.PenButton2Pressed = false; Settle(window);
            Assert.Equal("PRESS A BUTTON TO TEST IT", test.Text);
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
    // ── V30 build 5: the colour palette window (Digital Groceries 24 Nov ~17:43) ─────────────────────────────────────────────

    /// <summary>Digital Groceries 24 Nov ~17:43: "In Tablet / Pen when you click on the radial menu, it opens a popup window to set it up. I want the same
    /// for the colour pallet under the radial menu. The users should be able to click it and then go to fine tune it with the exaxt colour
    /// palletes they want": clicking the palette picture opens the palette window on the colours of this menu.</summary>
    [AvaloniaFact]
    public async Task ClickingThePalette_OpensThePaletteWindow_OnThisMenusColours()
    {
        var menu = RadialMenuDefaults.Create(Localizer.Te);
        menu.PaletteColours = new Dictionary<string, string> { ["0"] = "#123456" };
        var (vm, view, window, _) = Open(NewProfile(Name(), new PluginSettingStore?[] { null, RadialMenuBinding.Make(menu) }));
        PaletteEditorDialog? dialog = null;
        try
        {
            var button = Find<Button>(view, "PaletteSpotButton");
            Assert.True(button.IsEffectivelyEnabled);
            Assert.Equal("Click to choose your own colours", ToolTip.GetTip(button));
            Assert.Same(Find<ColourPaletteWheel>(view, "PaletteSpotWheel"), button.Content);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));   // the real click handler
            await PumpUntil(() => window.OwnedWindows.OfType<PaletteEditorDialog>().Any());
            dialog = window.OwnedWindows.OfType<PaletteEditorDialog>().First();
            Assert.Equal("Colour Palette Settings", dialog.Title);
            Assert.Equal("#123456", ColourPalette.HexFor(dialog.Wheel.Menu!, 0));
            Assert.Equal("Default colours", dialog.DefaultButton.Content);
            Assert.Equal("Save", dialog.SaveButton.Content);
        }
        finally
        {
            dialog?.Close();
            await Pump(60);
            window.Close();
        }
    }

    /// <summary>The window itself: a real click on a colour asks for a colour for THAT swatch; Save returns the menu with it; Default colours
    /// puts every built-in colour back; Cancel returns nothing.</summary>
    [AvaloniaFact]
    public async Task PaletteWindow_ClickAColour_PickOne_Save_Default_Cancel()
    {
        _ = new ThemeViewModel();
        var json = RadialMenuJson.Serialize(RadialMenuDefaults.Create(Localizer.Te));
        var dialog = new PaletteEditorDialog(json);
        (string hex, string name)? asked = null;
        dialog.PickColour = (hex, name) => { asked = (hex, name); return Task.FromResult<string?>("#abcdef"); };
        dialog.Show();
        try
        {
            for (int i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); dialog.UpdateLayout(); }
            var wheel = dialog.Wheel;
            int size = wheel.MenuSize;
            // find a point on swatch 5 the way the window does (from the centre of the wheel)
            Point? onFive = null;
            for (double y = 0; y < wheel.Bounds.Height && onFive == null; y += 2)
                for (double x = 0; x < wheel.Bounds.Width; x += 2)
                    if (ColourPalette.SwatchAt(x - wheel.Bounds.Width / 2, y - wheel.Bounds.Height / 2, size) == 5) { onFive = new Point(x, y); break; }
            Assert.NotNull(onFive);
            var at = wheel.TranslatePoint(onFive!.Value, dialog)!.Value;
            dialog.MouseDown(at, MouseButton.Left);
            dialog.MouseUp(at, MouseButton.Left);
            await PumpUntil(() => asked != null);
            Assert.Equal(ColourPalette.HexFor(RadialMenuJson.Parse(json), 5), asked!.Value.hex);
            Assert.Equal(ColourPalette.NameOf(5), asked.Value.name);
            await PumpUntil(() => ColourPalette.HexFor(dialog.Wheel.Menu!, 5) == "#ABCDEF");   // the picture shows it at once

            dialog.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var saved = await dialog.Result;
            Assert.NotNull(saved);
            Assert.Equal("#ABCDEF", ColourPalette.HexFor(RadialMenuJson.Parse(saved!), 5));
        }
        finally { dialog.Close(); }

        var d2 = new PaletteEditorDialog(json) { PickColour = (_, _) => Task.FromResult<string?>("#010203") };
        d2.Show();
        try
        {
            await d2.ChangeAsync(7);
            Assert.Equal("#010203", ColourPalette.HexFor(d2.Wheel.Menu!, 7));
            d2.DefaultButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(ColourPalette.HexFor(RadialMenuJson.Parse(json), 7), ColourPalette.HexFor(d2.Wheel.Menu!, 7));
            d2.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Null(RadialMenuJson.Parse((await d2.Result)!).PaletteColours);
        }
        finally { d2.Close(); }

        var d3 = new PaletteEditorDialog(json) { PickColour = (_, _) => Task.FromResult<string?>(null) };   // the picker's Cancel
        d3.Show();
        await d3.ChangeAsync(3);
        Assert.Null(d3.Wheel.Menu!.PaletteColours);
        d3.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Null(await d3.Result);
    }

    [AvaloniaFact]
    public async Task APaletteChange_IsAppliedToTheSelectedTable_AndThePaletteShowsIt_CancelChangesNothing()
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
            Assert.False(await vm.EditPaletteAsync(j => { seen = j; return Task.FromResult<string?>(null); }));   // Cancel
            Assert.False(app.HasUnsavedChanges);
            Assert.NotNull(seen);
            Assert.False(await vm.EditPaletteAsync(j => Task.FromResult<string?>(j)));                         // Save with no change
            Assert.False(app.HasUnsavedChanges);

            Assert.True(await vm.EditPaletteAsync(j =>
            {
                var m = RadialMenuJson.Parse(j); m.PaletteColours = new Dictionary<string, string> { ["0"] = "#FF00AA" };
                return Task.FromResult<string?>(RadialMenuJson.Serialize(m));
            }));
            await PumpUntil(() => !app.SettingsBusy && app.SaveState != SettingsSaveState.Applying);
            Settle(window);
            Assert.Equal("#FF00AA", ColourPalette.HexFor(RadialMenuBinding.ReadMenu(app.CurrentSettings!.Profiles[0].BindingSettings.PenButtons[1]), 0));
            Assert.Equal("#FF00AA", ColourPalette.HexFor(Find<ColourPaletteWheel>(view, "PaletteSpotWheel").Menu!, 0));
            Assert.True(app.HasUnsavedChanges);                       // kept by the footer's Save
        }
        finally { window.Close(); }
    }

    /// <summary>Nothing opens the radial menu yet: the colours stay in the pictures (a refresh keeps them) and go in with the menu when the
    /// radial menu's switch is turned on.</summary>
    [AvaloniaFact]
    public async Task PaletteColours_WithNothingOnTheRadialMenu_GoInWhenTheSwitchIsTurnedOn()
    {
        var applied = new List<Settings>();
        var (vm, view, window, profile) = Open(NewProfile(Name(), new PluginSettingStore?[] { null, AuxKeyBinding.MakeKeyBinding("E") }),
            apply: s => { applied.Add(s); return Task.FromResult(SettingsApplyOutcome.Live); });
        try
        {
            Assert.False(Find<ToggleSwitch>(view, "RadialSwitch").IsChecked);
            Assert.True(await vm.EditPaletteAsync(j =>
            {
                var m = RadialMenuJson.Parse(j); m.PaletteColours = new Dictionary<string, string> { ["2"] = "#0A0B0C" };
                return Task.FromResult<string?>(RadialMenuJson.Serialize(m));
            }));
            Assert.Empty(applied);                                    // nothing to apply it to yet
            vm.RefreshRadialSpot();
            Assert.Equal("#0A0B0C", ColourPalette.HexFor(vm.RadialPreview!.PaletteMenu!, 2));   // a refresh keeps it
            vm.ChooseMenuButton = (_, _, choices) => Task.FromResult<int?>(1);                  // "Pen button 2"
            vm.RadialSwitchOn = true;
            await PumpUntil(() => RadialMenuBinding.Is(profile.BindingSettings.PenButtons[1]));
            Assert.Equal("#0A0B0C", ColourPalette.HexFor(RadialMenuBinding.ReadMenu(profile.BindingSettings.PenButtons[1]), 2));
        }
        finally { window.Close(); }
    }
}
