using System.Collections.Generic;
using System.Linq;
using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>TASKS #212 part 5: "Give the keys Wacom's own functions?" - which models get the offer, which keys get which
/// function, and what the app sets for each (T7 Team/v30/wacom-factory-keys.md, VERIFIED rows only).</summary>
public class WacomFactoryKeysTests
{
    private static readonly FactoryFunction N = FactoryFunction.NotSetUp, Sh = FactoryFunction.Shift,
        Cm = FactoryFunction.Command, Op = FactoryFunction.Option, Sc = FactoryFunction.PenScroll,
        Rm = FactoryFunction.RadialMenu, To = FactoryFunction.TouchOnOff;

    private static FactoryFunction[] Functions(string model, int keys, bool touch = true) =>
        WacomFactoryKeys.For("Wacom " + model, keys, touch)!.Keys.Select(k => k.Function).ToArray();

    [Fact]
    public void Every_verified_model_has_a_proven_layout_with_as_many_keys_and_a_source()
    {
        var driver = KeyLayoutTests.WacomWithKeys().ToDictionary(o => KeyLayouts.ModelOf((string)o[0])!, o => (int)o[1]);
        foreach (var m in WacomFactoryKeys.VerifiedModels)
        {
            Assert.Contains(m, KeyLayouts.ProvenModels);
            var list = WacomFactoryKeys.For("Wacom " + m, driver[m], true);
            Assert.NotNull(list);
            Assert.False(string.IsNullOrWhiteSpace(list!.Source), m);
        }
    }

    [Fact]
    public void Models_without_verified_defaults_get_no_list()
    {
        Assert.Null(WacomFactoryKeys.For("Wacom CTL-4100", 4, false));      // Intuos 2018: NOT FOUND
        Assert.Null(WacomFactoryKeys.For("Wacom CTL-6100WL", 5, false));
        Assert.Null(WacomFactoryKeys.For("Wacom PTH-660", 8, true));        // Intuos Pro 2017: NOT FOUND
        Assert.Null(WacomFactoryKeys.For("Wacom PTH-451", 6, true));        // verified, but no proven layout
        Assert.Null(WacomFactoryKeys.For("Some Tablet", 4, false));
        Assert.Null(WacomFactoryKeys.For(null, 4, false));
    }

    [Fact]
    public void Another_number_of_keys_gets_no_list()
    {
        Assert.Null(WacomFactoryKeys.For("Wacom PTK-640", 6, false));
        Assert.Null(WacomFactoryKeys.For("Wacom PTK-440", 8, false));
    }

    [Fact]
    public void The_Intuos4_lists_follow_the_manual()
    {
        Assert.Equal(new[] { N, Rm, N, N, Sh, Cm, Op, Sc }, Functions("PTK-640", 8));
        Assert.Equal(new[] { N, Rm, N, N, Sh, Cm, Op, Sc }, Functions("PTK-540WL", 8));
        Assert.Equal(new[] { N, N, Sh, Cm, Op, Sc }, Functions("PTK-440", 6));
        var names = WacomFactoryKeys.For("Wacom PTK-440", 6, false)!.Keys.Select(k => k.WacomName);
        Assert.Equal(new[] { "Show Settings", "Precision Mode", "Shift", "Ctrl/Cmd", "Alt/Opt", "Pan/Scroll" }, names);
    }

    [Fact]
    public void Intuos5_and_Pro_2013_touch_on_off_only_where_the_app_drives_touch()
    {
        Assert.Equal(new[] { To, N, N, N, Sh, Cm, Op, Sc }, Functions("PTH-651", 8, touch: true));
        Assert.Equal(new[] { N, N, N, N, Sh, Cm, Op, Sc }, Functions("PTH-650", 8, touch: false));
        Assert.Equal(new[] { N, Rm, N, N, Sh, Cm, Op, Sc }, Functions("PTK-650", 8));
    }

    [Fact]
    public void Intuos3_has_the_same_four_in_each_group()
    {
        Assert.Equal(new[] { Cm, Sh, Op, Sc }, Functions("PTZ-430", 4));
        Assert.Equal(new[] { Cm, Sh, Op, Sc, Cm, Sh, Op, Sc }, Functions("PTZ-930", 8));
    }

    [Fact]
    public void Intuos_2013_Control_and_Bamboo_clicks_are_not_in_the_plans_list()
    {
        Assert.Equal(new[] { Sh, Op, Cm, N }, Functions("CTH-480", 4));
        Assert.Equal(new[] { To, N, N, N }, Functions("CTH-460", 4, touch: true));
        Assert.Equal(new[] { N, N, N, N }, Functions("CTH-470", 4, touch: false));
        Assert.All(Functions("CTE-450", 4), f => Assert.Equal(N, f));   // Back, Forward, Show Desktop, Exposé
        Assert.All(Functions("CTE-440", 2), f => Assert.Equal(N, f));   // Back, Forward
    }

    [Fact]
    public void The_plan_covers_only_recorded_keys_with_a_function_the_app_can_do()
    {
        var list = WacomFactoryKeys.For("Wacom PTK-440", 6, false);
        // Places 1-6 (Wacom's numbering) were the driver's keys 5,4,3,2,1,0; place 4 was skipped.
        var map = new KeyOrderMap(new[] { 5, 4, 3, KeyOrderMap.NotSeen, 1, 0 });
        var plan = WacomFactoryKeys.Plan(list, map);
        Assert.Equal(new Dictionary<int, FactoryFunction> { [3] = Sh, [1] = Op, [0] = Sc }, plan);
    }

    [Fact]
    public void No_plan_without_a_list_or_for_a_map_saved_turned()
    {
        var map = new KeyOrderMap(new[] { 0, 1, 2, 3 });
        Assert.Empty(WacomFactoryKeys.Plan(null, map));
        Assert.Empty(WacomFactoryKeys.Plan(WacomFactoryKeys.For("Wacom PTZ-430", 4, false), new KeyOrderMap(new[] { 0, 1, 2, 3 }, flipped: true)));
        Assert.Empty(WacomFactoryKeys.Plan(WacomFactoryKeys.For("Wacom CTE-450", 4, false), map));   // nothing the app can do
    }

    [Fact]
    public void Each_function_has_the_apps_binding()
    {
        Assert.Null(WacomFactoryKeys.Binding(N));
        var shift = WacomFactoryKeys.Binding(Sh)!;
        Assert.Equal(AuxKind.Keyboard, shift.Kind);
        Assert.Equal(new AuxCombo(false, false, false, "Shift"), shift.Combo);
        Assert.Equal("Application", WacomFactoryKeys.Binding(Cm)!.Combo.Key);
        Assert.Equal("Alt", WacomFactoryKeys.Binding(Op)!.Combo.Key);
        var scroll = WacomFactoryKeys.Binding(Sc)!;
        Assert.Equal(AuxKind.Scroll, scroll.Kind);
        Assert.Equal(PenScrollBinding.ScrollValue, scroll.Scroll);
        Assert.Equal(AuxKind.RadialMenu, WacomFactoryKeys.Binding(Rm)!.Kind);
        Assert.Equal(AuxBinding.TouchToggle, WacomFactoryKeys.Binding(To));
        // Each one is written and read back as the same binding (a bare modifier is a plain Key Binding).
        foreach (var f in new[] { Sh, Cm, Op, Sc, To })
        {
            var b = WacomFactoryKeys.Binding(f)!;
            Assert.Equal(b, AuxKeyBinding.ReadBinding(AuxKeyBinding.MakeBinding(b)));
        }
    }

    [Fact]
    public void A_modifier_held_on_its_own_reads_by_its_keyboard_name()
    {
        Assert.Equal("Shift", AuxKeyBinding.ModifierKeyName("Shift"));
        Assert.Equal(System.OperatingSystem.IsMacOS() ? "Command" : "Menu", AuxKeyBinding.ModifierKeyName("Application"));
        Assert.Equal(System.OperatingSystem.IsMacOS() ? "Option" : "Alt", AuxKeyBinding.ModifierKeyName("Alt"));
        Assert.Null(AuxKeyBinding.ModifierKeyName("A"));
        var shown = AuxKeyBinding.Describe(WacomFactoryKeys.Binding(Cm)!);
        Assert.DoesNotContain("Application", shown);
    }

    // ── the sheet's last screen ─────────────────────────────────────────────────────────────────

    private static void Tap(KeyMappingViewModel vm, int count, int key)
    {
        var s = new bool[count]; s[key] = true;
        vm.OnKeys(s);
        vm.OnKeys(new bool[count]);
    }

    private static KeyMappingViewModel Sheet(string model, int keys) => new(keys,
        layout: KeyLayouts.For("Wacom " + model, keys), factory: WacomFactoryKeys.For("Wacom " + model, keys, false));

    [Fact]
    public void The_last_screen_offers_Wacoms_functions_and_I_choose_myself_is_the_default()
    {
        var vm = Sheet("PTZ-430", 4);
        Assert.False(vm.OffersWacomFunctions);                 // not while it runs
        for (int k = 3; k >= 0; k--) Tap(vm, 4, k);
        Assert.True(vm.IsFinished);
        Assert.True(vm.OffersWacomFunctions);
        Assert.False(vm.UseWacomFunctions);
        Assert.Equal("Key 1: Command\nKey 2: Shift\nKey 3: Option\nKey 4: Move pen to scroll", vm.WacomFunctionsText);
        Assert.Equal(new Dictionary<int, FactoryFunction> { [3] = Cm, [2] = Sh, [1] = Op, [0] = Sc }, vm.WacomPlan);
        vm.UseWacomFunctions = true;
        vm.StartAgainCommand.Execute(null);
        Assert.False(vm.UseWacomFunctions);                    // a new run asks again
    }

    [Fact]
    public void The_list_says_which_keys_keep_what_they_do_and_why()
    {
        var vm = Sheet("PTK-440", 6);
        Tap(vm, 6, 0); vm.SkipCommand.Execute(null);
        for (int k = 2; k < 6; k++) Tap(vm, 6, k);
        Assert.Equal("Key 1: Show Settings - the app cannot do this, so it keeps what it does now\n"
            + "Key 2: skipped - keeps what it does now\nKey 3: Shift\nKey 4: Command\nKey 5: Option\nKey 6: Move pen to scroll",
            vm.WacomFunctionsText);
    }

    [Fact]
    public void No_offer_for_a_model_with_nothing_to_give_or_without_a_list()
    {
        var bamboo = Sheet("CTE-450", 4);
        for (int k = 0; k < 4; k++) Tap(bamboo, 4, k);
        Assert.False(bamboo.OffersWacomFunctions);

        var plain = new KeyMappingViewModel(4);
        for (int k = 0; k < 4; k++) Tap(plain, 4, k);
        Assert.False(plain.OffersWacomFunctions);
        Assert.Equal("", plain.WacomFunctionsText);

        // A list without the proven layout to go with it is not used.
        var noLayout = new KeyMappingViewModel(4, factory: WacomFactoryKeys.For("Wacom PTZ-430", 4, false));
        for (int k = 0; k < 4; k++) Tap(noLayout, 4, k);
        Assert.False(noLayout.OffersWacomFunctions);
    }

    [Fact]
    public void Stopped_with_no_key_recorded_there_is_no_offer()
    {
        var vm = Sheet("PTZ-430", 4);
        vm.StopCommand.Execute(null);
        Assert.False(vm.OffersWacomFunctions);
    }
}
