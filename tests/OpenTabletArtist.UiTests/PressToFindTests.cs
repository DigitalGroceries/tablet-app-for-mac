using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OtdInterop;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>V30 #200 B3 (team vote 23 Nov 2026, "press to find it"): pressing a key that has nothing on it, while the Tablet App
/// is the front window, opens that key's card and its line says it does nothing yet. Grok: only while the app is in front.
/// Devin: once per control, inside the window, never while drawing.</summary>
public class PressToFindTests
{
    private static Settings SettingsFor(string tablet, params string?[] keys)
    {
        var profile = new Profile
        {
            OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)),
            Tablet = tablet,
        };
        profile.BindingSettings.AuxButtons = new PluginSettingStoreCollection();
        foreach (var k in keys) profile.BindingSettings.AuxButtons.Add(k == null ? null! : AuxKeyBinding.MakeKeyBinding(k)!);
        return new Settings { Profiles = new ProfileCollection { profile } };
    }

    private static TabletDetailViewModel Vm(params string?[] keys) => VmFor(SettingsFor("TestTablet-PressToFind-" + Guid.NewGuid().ToString("N")[..8], keys));

    private static TabletDetailViewModel VmFor(Settings s) =>
        new(s.Profiles[0], s, applyAction: _ => Task.FromResult(SettingsApplyOutcome.Live));

    // The daemon's aux report reaches the page through this handler (DaemonPenInputSource.AuxButtons).
    private static void Press(TabletDetailViewModel vm, params bool[] states) =>
        typeof(TabletDetailViewModel).GetMethod("OnAuxButtons", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(vm, new object[] { states });

    [AvaloniaFact]
    public void Pressing_an_empty_key_with_the_app_in_front_jumps_to_it_once()
    {
        var settings = SettingsFor("TestTablet-PressToFind-" + Guid.NewGuid().ToString("N")[..8], "A", null);
        var vm = VmFor(settings);
        vm.IsAppInFront = () => true;
        var jumps = 0; ButtonBinding? to = null; TabletDetailTab? tab = null;
        vm.UnsetControlUsed += (t, b) => { jumps++; to = b; tab = t; };

        Press(vm, false, true);
        Assert.Equal(1, jumps);
        Assert.Same(vm.AuxButtons[1], to);
        Assert.Equal(TabletDetailTab.ExpressKeys, tab);
        Assert.True(vm.AuxButtons[1].JustUsed);
        Assert.Equal("You just used this. It does nothing yet.", vm.AuxButtons[1].NotSetUpText);

        // held, released, pressed again: still only once
        Press(vm, false, true); Press(vm, false, false); Press(vm, false, true);
        Assert.Equal(1, jumps);

        // the same key on a fresh visit of the page: once for the whole run of the app
        var again = VmFor(settings);
        again.IsAppInFront = () => true;
        var jumps2 = 0; again.UnsetControlUsed += (_, _) => jumps2++;
        Press(again, false, true);
        Assert.Equal(0, jumps2);
    }

    [AvaloniaFact]
    public void Only_the_key_pressed_last_shows_the_bubble()
    {
        // V30 build 3 (Zak 24 Nov, L6.3): two empty keys pressed one after the other - only the second still says it
        var vm = Vm(null, null, null);
        vm.IsAppInFront = () => true;
        Press(vm, true, false, false); Press(vm, false, false, false);
        Assert.True(vm.AuxButtons[0].JustUsed);
        Press(vm, false, false, true);
        Assert.False(vm.AuxButtons[0].JustUsed);
        Assert.True(vm.AuxButtons[2].JustUsed);
        Assert.Equal("Not set up", vm.AuxButtons[0].NotSetUpText);
    }

    [AvaloniaFact]
    public void A_key_that_does_something_never_jumps()
    {
        var vm = Vm("A", null);
        vm.IsAppInFront = () => true;
        var jumps = 0; vm.UnsetControlUsed += (_, _) => jumps++;
        Press(vm, true, false);
        Assert.Equal(0, jumps);
        Assert.False(vm.AuxButtons[0].JustUsed);
    }

    [AvaloniaFact]
    public void Nothing_jumps_while_another_app_is_in_front()
    {
        var vm = Vm((string?)null);
        vm.IsAppInFront = () => false;
        var jumps = 0; vm.UnsetControlUsed += (_, _) => jumps++;
        Press(vm, true);
        Assert.Equal(0, jumps);
        Assert.Equal("Not set up", vm.AuxButtons[0].NotSetUpText);
        // it was not used up: with the app in front the next press still finds it
        vm.IsAppInFront = () => true;
        Press(vm, false); Press(vm, true);
        Assert.Equal(1, jumps);
    }

    [AvaloniaFact]
    public void Nothing_jumps_while_drawing_or_just_after_a_stroke()
    {
        var vm = Vm((string?)null);
        vm.IsAppInFront = () => true;
        var jumps = 0; vm.UnsetControlUsed += (_, _) => jumps++;
        vm.TipPressed = true;
        Press(vm, true);
        Assert.Equal(0, jumps);
        vm.TipPressed = false;   // the pen just left the surface: a pause between strokes is still drawing
        Press(vm, false); Press(vm, true);
        Assert.Equal(0, jumps);
        Assert.False(vm.AuxButtons[0].JustUsed);
    }

    [AvaloniaFact]
    public void The_page_opens_the_ExpressKeys_tab_and_the_card_says_it_does_nothing_yet()
    {
        var vm = Vm("A", null);
        var view = new TabletDetailView { DataContext = vm };
        var w = new Window { Content = view, Width = 1300, Height = 1100 }; w.Show(); w.Activate();
        for (int i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); }
        vm.IsAppInFront = () => true;   // the headless window is never reported as active
        Assert.NotEqual(true, view.FindControl<RadioButton>("ButtonsTab")!.IsChecked);

        Press(vm, false, true);
        for (int i = 0; i < 6; i++) { Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); }
        Assert.True(view.FindControl<RadioButton>("ButtonsTab")!.IsChecked);
        Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(),
            t => t.Text == "You just used this. It does nothing yet." && t.IsEffectivelyVisible);
        Assert.Single(view.GetVisualDescendants().OfType<Button>(), b => b.Name == "NotSetUpChoose" && b.IsEffectivelyVisible);
        w.Close();
    }

    [AvaloniaFact]
    public void Turning_a_ring_or_moving_on_a_strip_reports_the_direction_row_that_was_used()
    {
        ButtonBinding Row(int i) => new(i, AuxBinding.Unbound, false, "", canEdit: true, applyBinding: null, label: "row");
        var cw = Row(0); var ccw = Row(1);
        var wheel = new WheelEditor(0, "Ring", false, cw, ccw, Array.Empty<ButtonBinding>(), 0, 0, null, null);
        ButtonBinding? used = null;
        wheel.RowUsed += r => used = r;
        wheel.OnRelativeDelta(1);
        Assert.Same(cw, used);
        wheel.OnRelativeDelta(-1);
        Assert.Same(ccw, used);
    }
}
