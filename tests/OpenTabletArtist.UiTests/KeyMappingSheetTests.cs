using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OtdInterop;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>TASKS #212, "Set up my buttons": the sheet itself, and what the ExpressKeys page does around it - the keys
/// do nothing while it is open (team vote 23 Nov 2026), their settings come back after, and the result is saved for the
/// tablet model.</summary>
public class KeyMappingSheetTests
{
    private static string Tablet() => "TestTablet-KeyMap-" + Guid.NewGuid().ToString("N")[..8];

    private static Settings SettingsFor(string tablet, params string[] keys)
    {
        var profile = new Profile
        {
            OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)),
            Tablet = tablet,
        };
        profile.BindingSettings.AuxButtons = new PluginSettingStoreCollection();
        foreach (var k in keys) profile.BindingSettings.AuxButtons.Add(AuxKeyBinding.MakeKeyBinding(k)!);
        return new Settings { Profiles = new ProfileCollection { profile } };
    }

    private static TabletDetailViewModel VmFor(Settings s) =>
        new(s.Profiles[0], s, applyAction: _ => Task.FromResult(SettingsApplyOutcome.Live));

    private static void Report(TabletDetailViewModel vm, params bool[] states) =>
        typeof(TabletDetailViewModel).GetMethod("OnAuxButtons", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(vm, new object[] { states });

    private static void Tap(TabletDetailViewModel vm, int count, int key)
    {
        var s = new bool[count]; s[key] = true;
        Report(vm, s);
        Report(vm, new bool[count]);
    }

    private static string[] LiveKeys(Settings s) =>
        s.Profiles[0].BindingSettings.AuxButtons.Select(b => AuxKeyBinding.ReadKey(b)).ToArray();

    private static void Cleanup(string tablet)
    {
        KeyOrderStore.Clear(tablet);
        foreach (var k in AppSettings.Keys("").Where(k => k.Contains(tablet, StringComparison.Ordinal)).ToList()) AppSettings.Remove(k);
    }

    // ── the page around the sheet ───────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task The_keys_do_nothing_while_the_sheet_is_open_and_the_order_is_saved()
    {
        var tablet = Tablet();
        try
        {
            var settings = SettingsFor(tablet, "A", "B", "C");
            var vm = VmFor(settings);
            vm.IsAppInFront = () => true;
            var jumps = 0; vm.UnsetControlUsed += (_, _) => jumps++;
            string[]? liveWhileOpen = null; bool openWhileOpen = false;
            vm.ShowKeyMapping = sheet =>
            {
                liveWhileOpen = LiveKeys(settings);
                openWhileOpen = vm.IsKeyMappingOpen;
                Tap(vm, 3, 2); Tap(vm, 3, 0); Tap(vm, 3, 1);          // top key = the driver's third, then first, then second
                Assert.True(sheet.IsFinished);
                return Task.FromResult<KeyOrderMap?>(sheet.Result);
            };

            Assert.True(await vm.RunKeyMappingAsync());

            Assert.True(openWhileOpen);
            Assert.NotNull(liveWhileOpen);
            Assert.All(liveWhileOpen!, k => Assert.True(string.IsNullOrEmpty(k) || k == AuxKeyBinding.None));   // nothing for the driver to do
            Assert.Equal(new[] { "A", "B", "C" }, LiveKeys(settings));                                            // and back afterwards
            Assert.True(vm.AuxButtonsEnabled);
            Assert.False(vm.IsKeyMappingOpen);
            Assert.Equal(0, jumps);                                                                               // no "press to find it" during the sheet
            Assert.Equal(new[] { 2, 0, 1 }, KeyOrderStore.Load(tablet)!.Keys);
            Assert.Equal(new[] { "C", "A", "B" }, vm.AuxButtonsInPlace.Select(b => b.Summary));
            Assert.Equal(new[] { 1, 2, 3 }, vm.AuxButtonsInPlace.Select(b => b.Place));
        }
        finally { Cleanup(tablet); }
    }

    [AvaloniaFact]
    public async Task Dont_save_keeps_the_mapping_that_was_there()
    {
        var tablet = Tablet();
        try
        {
            KeyOrderStore.Save(tablet, new KeyOrderMap(new[] { 1, 0 }));
            var settings = SettingsFor(tablet, "A", "B");
            var vm = VmFor(settings);
            vm.ShowKeyMapping = sheet => { Tap(vm, 2, 0); return Task.FromResult<KeyOrderMap?>(null); };
            Assert.False(await vm.RunKeyMappingAsync());
            Assert.Equal(new[] { 1, 0 }, KeyOrderStore.Load(tablet)!.Keys);
            Assert.Equal(new[] { "A", "B" }, LiveKeys(settings));
        }
        finally { Cleanup(tablet); }
    }

    [AvaloniaFact]
    public async Task Keys_the_user_had_turned_off_stay_off()
    {
        var tablet = Tablet();
        try
        {
            var settings = SettingsFor(tablet, "A", "B");
            var vm = VmFor(settings);
            vm.AuxButtonsEnabled = false;
            for (int i = 0; i < 5; i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10, TestContext.Current.CancellationToken); }
            vm.ShowKeyMapping = sheet => { Tap(vm, 2, 1); Tap(vm, 2, 0); return Task.FromResult<KeyOrderMap?>(sheet.Result); };
            Assert.True(await vm.RunKeyMappingAsync());
            Assert.False(vm.AuxButtonsEnabled);
            Assert.Equal(new[] { 1, 0 }, KeyOrderStore.Load(tablet)!.Keys);
        }
        finally { Cleanup(tablet); }
    }

    [AvaloniaFact]
    public async Task Keys_left_off_by_a_run_that_was_cut_short_come_back_on()
    {
        var tablet = Tablet();
        try
        {
            var settings = SettingsFor(tablet, "A", "B");
            var vm = VmFor(settings);
            // The app is stopped while the sheet is open: the sheet never returns.
            var never = new TaskCompletionSource<KeyOrderMap?>();
            vm.ShowKeyMapping = _ => never.Task;
            _ = vm.RunKeyMappingAsync();
            for (int i = 0; i < 5; i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10, TestContext.Current.CancellationToken); }
            Assert.All(LiveKeys(settings), k => Assert.True(string.IsNullOrEmpty(k) || k == AuxKeyBinding.None));

            // Next start: a new page for the same settings.
            var next = VmFor(settings);
            Assert.False(next.AuxButtonsEnabled);
            Assert.True(await next.RestoreKeysAfterBrokenMappingAsync());
            Assert.True(next.AuxButtonsEnabled);
            Assert.Equal(new[] { "A", "B" }, LiveKeys(settings));
            Assert.False(await next.RestoreKeysAfterBrokenMappingAsync());     // only once
        }
        finally { Cleanup(tablet); }
    }

    [AvaloniaFact]
    public async Task Off_by_the_users_own_choice_is_not_touched_at_start()
    {
        var tablet = Tablet();
        try
        {
            var settings = SettingsFor(tablet, "A", "B");
            var vm = VmFor(settings);
            vm.AuxButtonsEnabled = false;
            for (int i = 0; i < 5; i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10, TestContext.Current.CancellationToken); }
            var next = VmFor(settings);
            Assert.False(await next.RestoreKeysAfterBrokenMappingAsync());
            Assert.False(next.AuxButtonsEnabled);
        }
        finally { Cleanup(tablet); }
    }

    // ── part 4: the ring / strip step and the touch step ─────────────────────────────────────────

    private static void Invoke(TabletDetailViewModel vm, string method, object arg) =>
        typeof(TabletDetailViewModel).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(vm, new[] { arg });

    [AvaloniaFact]
    public async Task After_the_keys_the_ring_and_touch_reach_the_sheet_and_their_results_are_saved()
    {
        var tablet = Tablet();
        try
        {
            var settings = SettingsFor(tablet, "A", "B");
            settings.Profiles[0].BindingSettings.WheelBindings = new System.Collections.Generic.List<WheelBindingSettings> { new() };
            var vm = VmFor(settings);
            vm.HasTouchForMapping = true;
            vm.IsAppInFront = () => true;
            var jumps = 0; vm.UnsetControlUsed += (_, _) => jumps++;
            Assert.Single(vm.Wheels);
            vm.ShowKeyMapping = sheet =>
            {
                Assert.Equal(new[] { ControlCheckKind.Ring, ControlCheckKind.Touch }, sheet.Controls.Select(c => c.Kind));
                Tap(vm, 2, 0); Tap(vm, 2, 1);
                Assert.Equal(ControlCheckKind.Ring, sheet.CurrentControl!.Kind);
                Invoke(vm, "OnWheelPositions", new uint?[] { 12 });
                Invoke(vm, "OnWheelPositions", new uint?[] { 20 });   // turning: no jump to a ring row meanwhile
                Assert.Equal(ControlCheckKind.Touch, sheet.CurrentControl!.Kind);
                Invoke(vm, "OnTouches", 1);
                Assert.True(sheet.IsFinished);
                return Task.FromResult<KeyOrderMap?>(sheet.Result);
            };
            Assert.True(await vm.RunKeyMappingAsync());
            Assert.Equal(0, jumps);
            var saved = KeyOrderStore.Load(tablet)!;
            Assert.Equal(new[] { ControlCheckState.Works, ControlCheckState.Works }, saved.Controls.Select(c => c.State));
        }
        finally { Cleanup(tablet); }
    }

    [AvaloniaFact]
    public void The_checks_follow_the_pages_rings_and_the_tablets_touch()
    {
        var tablet = Tablet();
        var settings = SettingsFor(tablet, "A");
        settings.Profiles[0].BindingSettings.WheelBindings = new System.Collections.Generic.List<WheelBindingSettings> { new(), new() };
        var vm = VmFor(settings);
        vm.HasTouchForMapping = false;
        var checks = vm.MappingChecks(turned: false);
        Assert.Equal(new[] { "Touch Ring 1", "Touch Ring 2" }, checks.Select(c => c.Title));
        Assert.Equal(new[] { 0, 1 }, checks.Select(c => c.WheelIndex));
        vm.HasTouchForMapping = true;
        Assert.Equal(ControlCheckKind.Touch, vm.MappingChecks(turned: true).Last().Kind);

        var none = VmFor(SettingsFor(Tablet(), "A"));
        none.HasTouchForMapping = false;
        Assert.Empty(none.MappingChecks(turned: false));
    }

    [AvaloniaFact]
    public void The_sheet_lights_the_drawn_ring_and_shows_a_strip_beside_the_words()
    {
        var ring = new KeyMappingViewModel(6, null, false, KeyLayouts.For("Wacom PTK-440", 6),
            ControlCheck.For(new[] { (false, "Touch Ring") }, true, "Touch"));
        var sheet = new KeyMappingDialog(ring);
        sheet.Show();
        Assert.False(sheet.RingIsLit);
        Assert.False(sheet.ControlPicture.IsVisible);
        for (int k = 0; k < 6; k++) { ring.OnKeys(Down(6, k)); ring.OnKeys(new bool[6]); }
        Dispatcher.UIThread.RunJobs();
        Assert.True(sheet.RingIsLit);
        Assert.False(sheet.ControlPicture.IsVisible);
        Assert.Equal("Touch Ring", sheet.StepLabel.Text);
        ring.OnWheelPosition(0, 9);
        Dispatcher.UIThread.RunJobs();
        Assert.False(sheet.RingIsLit);
        Assert.True(sheet.ControlPicture.IsVisible);           // the finger picture for touch
        sheet.Close();

        var strip = new KeyMappingViewModel(4, null, false, KeyLayouts.For("Wacom PTZ-430", 4),
            ControlCheck.For(new[] { (true, "Touch Strip") }, false, "Touch"));
        var sheet2 = new KeyMappingDialog(strip);
        sheet2.Show();
        for (int k = 0; k < 4; k++) { strip.OnKeys(Down(4, k)); strip.OnKeys(new bool[4]); }
        Dispatcher.UIThread.RunJobs();
        Assert.True(sheet2.ControlPicture.IsVisible);
        Assert.True(sheet2.BackButton.IsEnabled && sheet2.SkipButton.IsVisible);
        strip.OnWheelPosition(0, 10); strip.OnWheelPosition(0, 6);
        Dispatcher.UIThread.RunJobs();
        Assert.False(sheet2.ControlPicture.IsVisible);
        Assert.Contains("Touch Strip works.", sheet2.InstructionLabel.Text);
        Assert.True(sheet2.SaveButton.IsVisible);
        sheet2.Close();
    }

    // ── the sheet ───────────────────────────────────────────────────────────────────────────────

    private static bool[] Down(int count, int key) { var s = new bool[count]; s[key] = true; return s; }

    [AvaloniaFact]
    public void The_sheet_shows_the_lit_key_and_the_right_buttons()
    {
        var vm = new KeyMappingViewModel(3);
        var sheet = new KeyMappingDialog(vm);
        sheet.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Key 1 of 3", sheet.StepLabel.Text);
        Assert.Equal("Press the lit key on your tablet.", sheet.InstructionLabel.Text);
        Assert.True(sheet.SkipButton.IsVisible && sheet.StopButton.IsVisible && sheet.BackButton.IsVisible);
        Assert.False(sheet.BackButton.IsEnabled);
        Assert.False(sheet.SaveButton.IsVisible || sheet.DontSaveButton.IsVisible || sheet.StartAgainButton.IsVisible);
        Assert.Equal(3, sheet.KeySpots.Count);

        vm.OnKeys(Down(3, 1)); vm.OnKeys(new bool[3]);
        Assert.Equal("Key 2 of 3", sheet.StepLabel.Text);
        Assert.True(sheet.BackButton.IsEnabled);
        vm.OnKeys(Down(3, 1)); vm.OnKeys(new bool[3]);
        Assert.Equal("You pressed a key you already did. Press the lit one.", sheet.MessageLabel.Text);
        sheet.Close();
    }

    [AvaloniaFact]
    public async Task Save_hands_back_the_mapping_and_Dont_save_hands_back_nothing()
    {
        var vm = new KeyMappingViewModel(2);
        var sheet = new KeyMappingDialog(vm);
        sheet.Show();
        vm.OnKeys(Down(2, 1)); vm.OnKeys(new bool[2]);
        vm.OnKeys(Down(2, 0)); vm.OnKeys(new bool[2]);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("All 2 keys are set up.", sheet.InstructionLabel.Text);
        Assert.True(sheet.SaveButton.IsVisible && sheet.DontSaveButton.IsVisible && sheet.StartAgainButton.IsVisible);
        Assert.False(sheet.SkipButton.IsVisible || sheet.StopButton.IsVisible || sheet.BackButton.IsVisible);
        sheet.SaveButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(new[] { 1, 0 }, (await sheet.Result)!.Keys);

        var vm2 = new KeyMappingViewModel(2);
        var sheet2 = new KeyMappingDialog(vm2);
        sheet2.Show();
        vm2.OnKeys(Down(2, 1)); vm2.OnKeys(new bool[2]);
        vm2.StopCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Don't save", sheet2.DontSaveButton.Content);
        sheet2.DontSaveButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Null(await sheet2.Result);
    }

    [AvaloniaFact]
    public async Task Stopped_with_nothing_recorded_there_is_nothing_to_save()
    {
        var vm = new KeyMappingViewModel(2);
        var sheet = new KeyMappingDialog(vm);
        sheet.Show();
        vm.StopCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(sheet.SaveButton.IsVisible);
        Assert.Equal("Close", sheet.DontSaveButton.Content);
        sheet.Close();
        Assert.Null(await sheet.Result);
    }

    [AvaloniaFact]
    public void The_page_has_the_Set_up_my_buttons_button()
    {
        var tablet = Tablet();
        try
        {
            var settings = SettingsFor(tablet, "A", "B");
            var vm = VmFor(settings);
            var view = new TabletDetailView { DataContext = vm };
            var w = new Window { Content = view, Width = 1300, Height = 1100 }; w.Show();
            typeof(TabletDetailView).GetMethod("SelectTab", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(view, new object[] { TabletDetailTab.ExpressKeys });
            for (int i = 0; i < 6; i++) { Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); }
            var button = view.FindControl<Button>("SetUpButtonsButton")!;
            Assert.True(button.IsEffectivelyVisible);
            Assert.Equal("Set up my buttons", button.Content);
            Assert.NotNull(vm.ShowKeyMapping);                 // the page gave the view model its sheet
            w.Close();
        }
        finally { Cleanup(tablet); }
    }
}
