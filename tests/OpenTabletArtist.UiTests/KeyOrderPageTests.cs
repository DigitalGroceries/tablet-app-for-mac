using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
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

/// <summary>TASKS #212, step-by-step button mapping, the ExpressKeys page: once the order the keys sit in is saved for
/// a tablet model, the page lists the keys in that order and numbers them by place. Everything else (settings, the
/// live press, the Intuos4's key displays) still goes by the driver's number.</summary>
public class KeyOrderPageTests
{
    private static string Tablet() => "TestTablet-KeyOrder-" + Guid.NewGuid().ToString("N")[..8];

    private static Settings SettingsFor(string tablet, float rotation, params string[] keys)
    {
        var profile = new Profile
        {
            OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)),
            Tablet = tablet,
            AbsoluteModeSettings = new AbsoluteModeSettings
            {
                Display = new AreaSettings { Width = 2560, Height = 1440, X = 1280, Y = 720 },
                Tablet = new AreaSettings { Width = 224, Height = 148, X = 112, Y = 74, Rotation = rotation },
            },
        };
        profile.BindingSettings.AuxButtons = new PluginSettingStoreCollection();
        foreach (var k in keys) profile.BindingSettings.AuxButtons.Add(AuxKeyBinding.MakeKeyBinding(k)!);
        return new Settings { Profiles = new ProfileCollection { profile } };
    }

    private static TabletDetailViewModel VmFor(string tablet, float rotation = 0, params string[] keys)
    {
        var s = SettingsFor(tablet, rotation, keys);
        return new TabletDetailViewModel(s.Profiles[0], s, applyAction: _ => Task.FromResult(SettingsApplyOutcome.Live));
    }

    private static void Press(TabletDetailViewModel vm, params bool[] states) =>
        typeof(TabletDetailViewModel).GetMethod("OnAuxButtons", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(vm, new object[] { states });

    private static string KeyOf(ButtonBinding b) => b.Summary;

    [AvaloniaFact]
    public void Without_a_mapping_the_page_is_as_it_was()
    {
        var vm = VmFor(Tablet(), 0, "A", "B", "C", "D");
        Assert.Equal(vm.AuxButtons, vm.AuxButtonsInPlace);
        Assert.Equal(new[] { 1, 2, 3, 4 }, vm.AuxButtonsInPlace.Select(b => b.Place));
        Assert.Equal(new[] { "Button 1", "Button 2", "Button 3", "Button 4" }, vm.AuxButtonsInPlace.Select(b => b.Label));
        Assert.DoesNotContain(vm.AuxButtonsInPlace, b => b.StartsNewGroup);
    }

    [AvaloniaFact]
    public void With_a_mapping_the_keys_are_listed_in_the_order_they_sit_and_numbered_by_place()
    {
        var tablet = Tablet();
        try
        {
            KeyOrderStore.Save(tablet, new KeyOrderMap(new[] { 2, 0, 3, 1 }));
            var vm = VmFor(tablet, 0, "A", "B", "C", "D");

            // Top to bottom: the driver's third key, first, fourth, second - with their own shortcuts.
            Assert.Equal(new[] { "C", "A", "D", "B" }, vm.AuxButtonsInPlace.Select(KeyOf));
            Assert.Equal(new[] { 1, 2, 3, 4 }, vm.AuxButtonsInPlace.Select(b => b.Place));
            Assert.Equal(new[] { "Button 1", "Button 2", "Button 3", "Button 4" }, vm.AuxButtonsInPlace.Select(b => b.Label));

            // The driver's order is untouched: settings are stored under the driver's number.
            Assert.Equal(new[] { "A", "B", "C", "D" }, vm.AuxButtons.Select(KeyOf));
            Assert.Equal(new[] { 1, 2, 3, 4 }, vm.AuxButtons.Select(b => b.Index));
        }
        finally { KeyOrderStore.Clear(tablet); }
    }

    [AvaloniaFact]
    public void A_press_lights_the_key_where_it_sits()
    {
        var tablet = Tablet();
        try
        {
            KeyOrderStore.Save(tablet, new KeyOrderMap(new[] { 2, 0, 3, 1 }));
            var vm = VmFor(tablet, 0, "A", "B", "C", "D");
            Press(vm, false, false, true, false);            // the driver's third key
            Assert.True(vm.AuxButtonsInPlace[0].IsPressed);  // is the top key on the page
            Assert.Equal(1, vm.AuxButtonsInPlace.Count(b => b.IsPressed));
        }
        finally { KeyOrderStore.Clear(tablet); }
    }

    [AvaloniaFact]
    public void A_second_group_of_keys_starts_with_a_gap()
    {
        var tablet = Tablet();
        try
        {
            KeyOrderStore.Save(tablet, new KeyOrderMap(new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, groups: new[] { 4, 4 }));
            var vm = VmFor(tablet, 0, "A", "B", "C", "D", "E", "F", "G", "H");
            Assert.Equal(new[] { false, false, false, false, true, false, false, false },
                vm.AuxButtonsInPlace.Select(b => b.StartsNewGroup));
        }
        finally { KeyOrderStore.Clear(tablet); }
    }

    [AvaloniaFact]
    public void A_tablet_turned_round_shows_the_keys_the_other_way_up()
    {
        var tablet = Tablet();
        try
        {
            // Mapped the usual way up, three keys above and two below; then the tablet is turned by 180 degrees.
            KeyOrderStore.Save(tablet, new KeyOrderMap(new[] { 2, 0, 1, 4, 3 }, flipped: false, groups: new[] { 3, 2 }));
            var vm = VmFor(tablet, 180, "A", "B", "C", "D", "E");
            Assert.Equal(new[] { "D", "E", "B", "A", "C" }, vm.AuxButtonsInPlace.Select(KeyOf));
            Assert.Equal(new[] { 1, 2, 3, 4, 5 }, vm.AuxButtonsInPlace.Select(b => b.Place));
            // The group of two is on top now, so the gap is above the third key.
            Assert.Equal(new[] { false, false, true, false, false }, vm.AuxButtonsInPlace.Select(b => b.StartsNewGroup));
        }
        finally { KeyOrderStore.Clear(tablet); }
    }

    [AvaloniaFact]
    public void A_mapping_for_fewer_keys_than_the_tablet_has_still_shows_every_key()
    {
        var tablet = Tablet();
        try
        {
            KeyOrderStore.Save(tablet, new KeyOrderMap(new[] { 3, KeyOrderMap.NotSeen, 1 }));
            var vm = VmFor(tablet, 0, "A", "B", "C", "D");
            Assert.Equal(new[] { "D", "B", "A", "C" }, vm.AuxButtonsInPlace.Select(KeyOf));
            Assert.Equal(4, vm.AuxButtonsInPlace.Distinct().Count());
        }
        finally { KeyOrderStore.Clear(tablet); }
    }

    [AvaloniaFact]
    public void The_page_draws_the_keys_in_that_order_with_the_place_on_each_key()
    {
        var tablet = Tablet();
        try
        {
            KeyOrderStore.Save(tablet, new KeyOrderMap(new[] { 1, 2, 0 }));
            var vm = VmFor(tablet, 0, "A", "B", "C");
            var view = new TabletDetailView { DataContext = vm };
            var w = new Window { Content = view, Width = 1300, Height = 1100 }; w.Show();
            typeof(TabletDetailView).GetMethod("SelectTab", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(view, new object[] { TabletDetailTab.ExpressKeys });
            for (int i = 0; i < 6; i++) { Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); }

            var list = view.FindControl<ItemsControl>("AuxKeysList")!;
            Assert.Equal(new[] { "B", "C", "A" }, list.Items.Cast<ButtonBinding>().Select(KeyOf));
            var nubs = list.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("expressKeyNub"))
                .Select(b => ((TextBlock)b.Child!).Text).ToArray();
            Assert.Equal(new[] { "1", "2", "3" }, nubs);
            w.Close();
        }
        finally { KeyOrderStore.Clear(tablet); }
    }
}
