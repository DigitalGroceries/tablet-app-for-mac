using System.Linq;
using Newtonsoft.Json.Linq;
using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>TASKS #212 part 4: the steps after the keys - "Touch the ring", "Slide one finger up the strip" (which also
/// tells which way the strip counts) and, on touch models, "Touch the tablet with one finger".</summary>
public class ControlCheckTests
{
    private static ControlCheck Strip(bool turned = false) => new(ControlCheckKind.Strip, 0, "Touch Strip", turned);

    [Fact]
    public void A_ring_works_at_its_first_touch_or_turn_on_its_own_wheel()
    {
        var ring = new ControlCheck(ControlCheckKind.Ring, 1, "Touch Ring 2");
        Assert.False(ring.OnPosition(1, null));
        Assert.False(ring.OnPosition(0, 30));                 // another ring
        Assert.True(ring.OnPosition(1, 30));
        Assert.Equal(ControlCheckState.Works, ring.State);

        var relative = new ControlCheck(ControlCheckKind.Ring, 0, "Touch Ring");
        Assert.False(relative.OnDelta(0, 0));
        Assert.True(relative.OnDelta(0, -1));
    }

    [Fact]
    public void A_strip_slid_up_with_a_falling_position_is_as_the_app_expects()
    {
        var s = Strip();
        Assert.False(s.OnPosition(0, 10));
        Assert.False(s.OnPosition(0, 9));                     // a wobble of one or two is not a slide
        Assert.True(s.OnPosition(0, 7));
        Assert.Equal(StripDirection.UpIsFalling, s.StripUp);
    }

    [Fact]
    public void A_strip_that_rises_as_the_finger_goes_up_counts_the_other_way()
    {
        var s = Strip();
        s.OnPosition(0, 2); s.OnPosition(0, 4);
        Assert.True(s.OnPosition(0, 6));
        Assert.Equal(StripDirection.UpIsRising, s.StripUp);
    }

    [Fact]
    public void Lifting_the_finger_starts_a_new_slide()
    {
        var s = Strip();
        s.OnPosition(0, 10); s.OnPosition(0, 9);
        s.OnPosition(0, null);
        Assert.False(s.OnPosition(0, 7));                     // 10 -> 7 would count, but it is a new slide from 7
        Assert.False(s.IsDone);
        Assert.True(s.OnPosition(0, 4));
    }

    [Fact]
    public void With_the_tablet_turned_up_as_held_is_down_on_the_tablet()
    {
        var s = Strip(turned: true);
        s.OnPosition(0, 2);
        Assert.True(s.OnPosition(0, 6));                      // rising as held = falling on the tablet
        Assert.Equal(StripDirection.UpIsFalling, s.StripUp);
    }

    [Fact]
    public void Touch_works_with_one_finger_or_more()
    {
        var t = new ControlCheck(ControlCheckKind.Touch, 5, "Touch");
        Assert.Equal(-1, t.WheelIndex);
        Assert.False(t.OnPosition(0, 3));
        Assert.False(t.OnTouch(0));
        Assert.True(t.OnTouch(1));
    }

    [Fact]
    public void Skip_and_Reset()
    {
        var s = Strip();
        s.Skip(nothingCameThrough: true);
        Assert.Equal(ControlCheckState.NotSeen, s.State);
        Assert.False(s.OnPosition(0, 1));                     // done: nothing changes it
        s.Reset();
        Assert.Equal(ControlCheckState.Waiting, s.State);
        s.Skip(false);
        Assert.Equal(ControlCheckState.Skipped, s.State);
    }

    [Fact]
    public void The_checks_are_each_wheel_then_touch()
    {
        var list = ControlCheck.For(new[] { (true, "Left Touch Strip"), (true, "Right Touch Strip") }, true, "Touch");
        Assert.Equal(new[] { ControlCheckKind.Strip, ControlCheckKind.Strip, ControlCheckKind.Touch }, list.Select(c => c.Kind));
        Assert.Equal(new[] { 0, 1, -1 }, list.Select(c => c.WheelIndex));
        Assert.Empty(ControlCheck.For(System.Array.Empty<(bool, string)>(), false, "Touch"));
    }

    [Fact]
    public void The_results_are_saved_with_the_key_order_and_read_back()
    {
        var map = new KeyOrderMap(new[] { 1, 0 }, controls: new[]
        {
            new ControlCheckResult(ControlCheckKind.Strip, 0, ControlCheckState.Works, StripDirection.UpIsRising),
            new ControlCheckResult(ControlCheckKind.Touch, -1, ControlCheckState.NotSeen),
        });
        var back = KeyOrderMap.TryParse(map.ToJson())!;
        Assert.Equal(map.Controls, back.Controls);
        Assert.Equal(new[] { 1, 0 }, back.Keys);
    }

    [Fact]
    public void A_mapping_saved_before_part_4_or_a_damaged_check_still_reads()
    {
        Assert.Empty(KeyOrderMap.TryParse("{\"v\":1,\"keys\":[0,1],\"flipped\":false,\"groups\":[]}")!.Controls);
        var odd = KeyOrderMap.TryParse("{\"keys\":[0],\"controls\":[{\"kind\":\"pedal\",\"wheel\":0,\"state\":\"works\"},1,"
            + "{\"kind\":\"ring\",\"wheel\":0,\"state\":\"works\"}]}")!;
        Assert.Equal(new[] { 0 }, odd.Keys);
        Assert.Single(odd.Controls);
        Assert.Equal(ControlCheckKind.Ring, odd.Controls[0].Kind);
    }

    [Fact]
    public void A_touch_report_says_how_many_fingers_are_down()
    {
        var r = JObject.Parse("{\"Data\":{\"Touches\":[{\"TouchID\":0,\"Position\":{\"X\":1,\"Y\":2}},null,{\"TouchID\":2,\"Position\":{\"X\":3,\"Y\":4}}]}}");
        Assert.True(DeviceReportSample.TryParseTouches(r, out var n));
        Assert.Equal(2, n);
        Assert.True(DeviceReportSample.TryParseTouches(JObject.Parse("{\"Data\":{\"Touches\":[null,null]}}"), out n));
        Assert.Equal(0, n);
        Assert.False(DeviceReportSample.TryParseTouches(JObject.Parse("{\"Data\":{\"AuxButtons\":[true]}}"), out _));
    }

    // ── the sheet's model: the checks follow the keys ──

    private static void Tap(KeyMappingViewModel vm, int count, int key)
    {
        var s = new bool[count]; s[key] = true;
        vm.OnKeys(s);
        vm.OnKeys(new bool[count]);
    }

    private static KeyMappingViewModel Intuos4S(bool touch = false, bool turned = false) =>
        new(6, null, turned, KeyLayouts.For("Wacom PTK-440", 6),
            ControlCheck.For(new[] { (false, "Touch Ring") }, touch, "Touch", turned));

    [Fact]
    public void After_the_last_key_the_ring_step_comes_and_lights_the_drawn_ring()
    {
        var vm = Intuos4S();
        for (int k = 0; k < 6; k++) Tap(vm, 6, k);
        Assert.False(vm.IsFinished);
        Assert.Equal(ControlCheckKind.Ring, vm.CurrentControl!.Kind);
        Assert.True(vm.IsRingLit);
        Assert.Equal("Touch Ring", vm.StepText);
        Assert.Equal("Touch the ring with one finger and move it round a little.", vm.Instruction);
        Assert.All(vm.Places, p => Assert.False(p.IsLit));
        Tap(vm, 6, 0);                                        // a key now is not for the ring
        Assert.False(vm.IsFinished);
        vm.OnWheelPosition(0, 40);
        Assert.True(vm.IsFinished);
        Assert.Equal("All 6 keys are set up.\nTouch Ring works.", vm.Instruction);
        Assert.Equal(ControlCheckState.Works, vm.Result.Controls.Single().State);
    }

    [Fact]
    public void Touch_comes_last_and_nothing_coming_through_is_said_plainly()
    {
        var vm = Intuos4S(touch: true);
        for (int k = 0; k < 6; k++) Tap(vm, 6, k);
        vm.OnWheelPosition(0, 3);
        Assert.Equal(ControlCheckKind.Touch, vm.CurrentControl!.Kind);
        Assert.False(vm.IsRingLit);
        Assert.Equal("Touch the tablet with one finger.", vm.Instruction);
        vm.NothingCame();
        vm.SkipCommand.Execute(null);
        Assert.True(vm.IsFinished);
        Assert.EndsWith("Nothing came through from touch.", vm.Instruction);
        Assert.Equal(ControlCheckState.NotSeen, vm.Result.Controls.Last().State);
    }

    [Fact]
    public void Back_from_the_first_check_goes_back_to_the_last_key()
    {
        var vm = Intuos4S(touch: true);
        for (int k = 0; k < 6; k++) Tap(vm, 6, k);
        vm.OnWheelPosition(0, 3);                             // ring done, touch is on
        vm.BackCommand.Execute(null);
        Assert.Equal(ControlCheckKind.Ring, vm.CurrentControl!.Kind);
        Assert.Equal(ControlCheckState.Waiting, vm.CurrentControl.State);
        vm.BackCommand.Execute(null);
        Assert.Null(vm.CurrentControl);
        Assert.True(vm.Places[5].IsLit);
        Assert.Equal("Key 6 of 6", vm.StepText);
    }

    [Fact]
    public void Stop_ends_everything_and_keeps_what_was_done()
    {
        var vm = Intuos4S(touch: true);
        for (int k = 0; k < 6; k++) Tap(vm, 6, k);
        vm.OnWheelPosition(0, 3);
        vm.StopCommand.Execute(null);
        Assert.True(vm.IsFinished);
        Assert.Null(vm.CurrentControl);
        Assert.Single(vm.Result.Controls);                    // the ring; touch was never reached
        Assert.Equal(6, vm.Result.Keys.Count);
    }

    [Fact]
    public void Stop_during_the_keys_skips_the_checks()
    {
        var vm = Intuos4S();
        Tap(vm, 6, 0);
        vm.StopCommand.Execute(null);
        Assert.True(vm.IsFinished);
        Assert.Empty(vm.Result.Controls);
    }

    [Fact]
    public void Start_again_forgets_the_checks_too()
    {
        var vm = Intuos4S();
        for (int k = 0; k < 6; k++) Tap(vm, 6, k);
        vm.OnWheelPosition(0, 3);
        vm.StartAgainCommand.Execute(null);
        Assert.False(vm.IsFinished);
        Assert.Null(vm.CurrentControl);
        Assert.Empty(vm.Result.Controls);
        Assert.True(vm.Places[0].IsLit);
    }

    [Fact]
    public void An_Intuos3_strip_that_counts_the_other_way_is_named_at_the_end()
    {
        var vm = new KeyMappingViewModel(4, null, false, KeyLayouts.For("Wacom PTZ-430", 4),
            ControlCheck.For(new[] { (true, "Touch Strip") }, false, "Touch"));
        for (int k = 0; k < 4; k++) Tap(vm, 4, k);
        Assert.Equal("Slide one finger up the strip, from the bottom to the top.", vm.Instruction);
        Assert.False(vm.IsRingLit);                           // no ring: the sheet shows the strip beside the words
        vm.OnWheelPosition(0, 1); vm.OnWheelPosition(0, 5);
        Assert.True(vm.IsFinished);
        Assert.Contains("Touch Strip works, but it counts the other way round.", vm.Instruction);
        Assert.Equal(StripDirection.UpIsRising, vm.Result.Controls.Single().StripUp);
    }

    [Fact]
    public void Two_rings_are_not_lit_on_a_drawing_that_shows_one()
    {
        var vm = new KeyMappingViewModel(8, null, false, KeyLayouts.For("Wacom PTK-640", 8),
            ControlCheck.For(new[] { (false, "Touch Ring 1"), (false, "Touch Ring 2") }, false, "Touch"));
        for (int k = 0; k < 8; k++) Tap(vm, 8, k);
        Assert.False(vm.IsRingLit);
        vm.OnWheelPosition(1, 5);                             // the other ring first: not this step
        Assert.Equal(0, vm.CurrentControl!.WheelIndex);
        vm.OnWheelPosition(0, 5);
        Assert.Equal(1, vm.CurrentControl!.WheelIndex);
    }

    [Fact]
    public void A_tablet_with_a_ring_but_no_keys_goes_straight_to_the_ring()
    {
        var vm = new KeyMappingViewModel(0, controls: ControlCheck.For(new[] { (false, "Touch Ring") }, false, "Touch"));
        Assert.False(vm.IsFinished);
        Assert.Equal(ControlCheckKind.Ring, vm.CurrentControl!.Kind);
        Assert.False(vm.CanGoBack);
        vm.OnWheelDelta(0, 2);
        Assert.Equal("Touch Ring works.", vm.Instruction);
    }

    [Fact]
    public void Without_checks_the_sheet_ends_after_the_keys_as_before()
    {
        var vm = new KeyMappingViewModel(2);
        Tap(vm, 2, 0); Tap(vm, 2, 1);
        Assert.True(vm.IsFinished);
        Assert.Equal("All 2 keys are set up.", vm.Instruction);
        Assert.Empty(vm.Result.Controls);
    }
}
