using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>TASKS #212: the drawn key layout per tablet family (KeyLayouts). Every Wacom model the driver knows with
/// ExpressKeys gets a drawing with exactly its key count, inside its box, no two keys on top of each other; only the
/// models whose manual shows the key places get a proven layout, every other one the plain column.</summary>
public class KeyLayoutTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "OpenTabletArtist.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    /// <summary>(name, key count) of every Wacom configuration with ExpressKeys.</summary>
    public static IEnumerable<object[]> WacomWithKeys()
    {
        var dir = Path.Combine(Root(), "external/OpenTabletDriver/OpenTabletDriver.Configurations/Configurations/Wacom");
        foreach (var file in Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            var j = JObject.Parse(File.ReadAllText(file));
            int count = j["Specifications"]?["AuxiliaryButtons"]?["ButtonCount"]?.Value<int>() ?? 0;
            if (count > 0) yield return new object[] { j["Name"]!.Value<string>()!, count };
        }
    }

    // The models the leader's hand-over (26 Nov 2026 19:30 UTC) lists as proven from their manuals.
    private static readonly string[] Proven =
    {
        "PTK-640", "PTK-840", "PTK-1240", "PTK-540WL", "PTK-440",
        "PTH-650", "PTH-850", "PTK-650", "PTH-651", "PTH-851",
        "CTL-480", "CTH-480", "CTH-680",
        "CTE-450", "CTE-650", "MTE-450",
        "CTE-440", "CTE-640",
        "PTZ-430", "PTZ-431W", "PTZ-630", "PTZ-631W", "PTZ-930", "PTZ-1230", "PTZ-1231W",
        "CTL-4100", "CTL-6100", "CTL-4100WL", "CTL-6100WL",
        "CTH-460", "CTH-461", "CTH-661", "CTH-470", "CTH-670",
    };

    // Not proven (the hand-over's list): they must keep the plain column.
    private static readonly string[] NotProven =
    {
        "PTH-450", "PTK-450", "PTH-451", "PTH-460", "PTH-660", "PTH-860", "PTK-470", "PTK-670", "PTK-870",
        "DTK-1300", "DTH-1300", "DTK-2100", "DTZ-2100", "DTK-2200", "DTH-2200", "DTZ-1200W", "DTH-227", "DTH-271",
        "CTH-300", "CTH-301", "CTL-680", "CTH-490", "CTH-690", "CTL-490", "CTL-690",
    };

    [Fact]
    public void There_are_59_Wacom_models_with_keys()
    {
        Assert.Equal(59, WacomWithKeys().Count());
    }

    [Theory]
    [MemberData(nameof(WacomWithKeys))]
    public void Every_Wacom_model_with_keys_gets_a_sound_drawing(string name, int count)
    {
        var layout = KeyLayouts.For(name, count);
        Assert.Equal(count, layout.KeyCount);
        Assert.Equal(count, layout.GroupSizes.Sum());
        Assert.All(layout.Spots, s => Assert.True(s.W > 0 && s.H > 0));
        foreach (var spots in new[] { layout.Spots, layout.TurnedSpots })
        {
            Assert.All(spots, s => Assert.True(s.X >= 0 && s.Y >= 0 && s.X + s.W <= layout.Width && s.Y + s.H <= layout.Height,
                $"{name}: a key sits outside the drawing"));
            for (int i = 0; i < spots.Count; i++)
                for (int k = i + 1; k < spots.Count; k++)
                    Assert.False(spots[i].Overlaps(spots[k]), $"{name}: keys {i + 1} and {k + 1} overlap");
        }
        // Groups follow one another: never group 0, 1, 0.
        var groups = layout.Spots.Select(s => s.Group).ToArray();
        Assert.Equal(groups.OrderBy(g => g), groups);
        if (layout.GroupNames.Count > 0) Assert.Equal(layout.GroupSizes.Count, layout.GroupNames.Count);
        if (layout.Ring is { } r)
        {
            Assert.True(r.X >= 0 && r.Y >= 0 && r.X + r.D <= layout.Width && r.Y + r.D <= layout.Height);
            var ringBox = new KeySpot(r.X, r.Y, r.D, r.D, -1);
            Assert.All(layout.Spots, s => Assert.False(s.Overlaps(ringBox), $"{name}: a key sits on the ring"));
        }
        var model = KeyLayouts.ModelOf(name)!;
        Assert.Equal(Proven.Contains(model), layout.Proven);
        if (layout.Proven) Assert.False(string.IsNullOrWhiteSpace(layout.Source));
    }

    [Fact]
    public void Every_proven_model_is_a_driver_model_with_that_many_keys()
    {
        var driver = WacomWithKeys().ToDictionary(o => KeyLayouts.ModelOf((string)o[0])!, o => (int)o[1]);
        Assert.Equal(Proven.OrderBy(x => x), KeyLayouts.ProvenModels.OrderBy(x => x));
        foreach (var m in Proven)
        {
            Assert.True(driver.ContainsKey(m), m + " is not a driver model");
            Assert.True(KeyLayouts.For("Wacom " + m, driver[m]).Proven, m);
        }
        Assert.All(NotProven, m => Assert.DoesNotContain(m, KeyLayouts.ProvenModels));
    }

    [Theory]
    [InlineData("Wacom PTK-440", "PTK-440")]
    [InlineData("Wacom PTK-540WL", "PTK-540WL")]
    [InlineData("Wacom PTZ-431W", "PTZ-431W")]
    [InlineData("Wacom Cintiq 13HD (DTK-1300)", "DTK-1300")]
    [InlineData("Wacom CTL-4100WL", "CTL-4100WL")]
    [InlineData("Wacom ET-0405-U", null)]
    [InlineData("TestTablet-KeyMap-1234", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void The_model_is_read_from_the_driver_name(string? name, string? model)
    {
        Assert.Equal(model, KeyLayouts.ModelOf(name));
    }

    [Fact]
    public void The_Intuos4_S_has_three_keys_above_the_ring_and_three_below()
    {
        var l = KeyLayouts.For("Wacom PTK-440", 6);
        Assert.True(l.Proven);
        Assert.Equal(new[] { 3, 3 }, l.GroupSizes);
        Assert.NotNull(l.Ring);
        var ring = l.Ring!.Value;
        Assert.All(l.Spots.Take(3), s => Assert.True(s.Y + s.H <= ring.Y));
        Assert.All(l.Spots.Skip(3), s => Assert.True(s.Y >= ring.Y + ring.D));
        Assert.Equal("Above the ring", l.GroupNames[0].English);
    }

    [Fact]
    public void The_Intuos4_M_L_XL_and_Intuos5_have_four_and_four()
    {
        foreach (var m in new[] { "PTK-640", "PTK-840", "PTK-1240", "PTK-540WL", "PTH-650", "PTH-850", "PTK-650", "PTH-651", "PTH-851" })
            Assert.Equal(new[] { 4, 4 }, KeyLayouts.For("Wacom " + m, 8).GroupSizes);
    }

    [Fact]
    public void The_Intuos3_group_has_a_tall_outer_key_two_inner_keys_and_a_wide_bottom_key()
    {
        var one = KeyLayouts.For("Wacom PTZ-430", 4);
        Assert.Equal(new[] { 4 }, one.GroupSizes);
        var s = one.Spots;
        Assert.True(s[0].H > s[1].H && s[0].X < s[1].X, "key 1 tall, on the outer (left) edge");
        Assert.True(s[1].Y < s[2].Y && s[1].X == s[2].X, "key 2 above key 3 on the inner side");
        Assert.True(s[3].W > s[0].W && s[3].Y > s[0].Y + s[0].H - 1, "key 4 wide at the bottom");

        var two = KeyLayouts.For("Wacom PTZ-930", 8);
        Assert.Equal(new[] { 4, 4 }, two.GroupSizes);
        Assert.True(two.Spots[4].X > two.Spots[5].X, "the right group is the mirror image: its key 1 is outermost");
        Assert.All(two.Spots.Take(4), l => Assert.True(l.X + l.W < two.Width / 2));
        Assert.All(two.Spots.Skip(4), r => Assert.True(r.X > two.Width / 2));
    }

    [Fact]
    public void The_Intuos_2018_has_four_keys_across_the_top_and_the_Bluetooth_model_a_power_button_in_the_middle()
    {
        var usb = KeyLayouts.For("Wacom CTL-4100", 4);
        Assert.True(usb.Proven);
        Assert.True(usb.Width > usb.Height);
        Assert.Equal(usb.Spots.OrderBy(s => s.X), usb.Spots);
        Assert.All(usb.Spots, s => Assert.Equal(usb.Spots[0].Y, s.Y));

        var wl = KeyLayouts.For("Wacom CTL-6100WL", 5);
        Assert.Equal(new[] { 4, 1 }, wl.GroupSizes);
        var power = wl.Spots[4];
        Assert.Equal(wl.Width / 2, power.X + power.W / 2, 3);
        Assert.Equal("Power button", wl.GroupNames[1].English);
    }

    [Fact]
    public void The_2013_Intuos_has_two_keys_in_each_upper_corner()
    {
        var l = KeyLayouts.For("Wacom CTH-480", 4);
        Assert.Equal(new[] { 2, 2 }, l.GroupSizes);
        Assert.All(l.Spots.Take(2), s => Assert.True(s.X + s.W < l.Width / 2));
        Assert.All(l.Spots.Skip(2), s => Assert.True(s.X > l.Width / 2));
    }

    [Fact]
    public void The_2007_Bamboo_keys_sit_round_the_ring()
    {
        var l = KeyLayouts.For("Wacom CTE-450", 4);
        var r = l.Ring!.Value;
        var s = l.Spots;
        Assert.True(s[0].X < s[1].X && s[0].Y == s[1].Y, "upper left, upper right");
        Assert.True(s[2].X < s[3].X && s[2].Y == s[3].Y && s[2].Y > s[0].Y, "lower left, lower right");
        Assert.True(s[0].Y + s[0].H <= r.Y && s[2].Y >= r.Y + r.D);
    }

    [Theory]
    [InlineData("Wacom PTH-450", 6)]
    [InlineData("Wacom PTK-470", 5)]
    [InlineData("Wacom Cintiq 22HD (DTK-2200)", 20)]
    [InlineData("Wacom CTH-490", 4)]
    [InlineData("TestTablet-KeyMap-1", 3)]
    [InlineData(null, 2)]
    public void Models_without_a_proven_layout_get_the_plain_column(string? name, int count)
    {
        var l = KeyLayouts.For(name, count);
        Assert.False(l.Proven);
        Assert.Equal("plain column", l.Family);
        Assert.Equal(count, l.KeyCount);
        Assert.All(l.Spots, s => Assert.Equal(l.Spots[0].X, s.X));
        Assert.Equal(l.Spots.OrderBy(s => s.Y), l.Spots);
    }

    [Fact]
    public void Another_key_count_than_the_manual_gives_the_plain_column()
    {
        // A driver configuration that reports another number of keys must never hide or add a key.
        Assert.False(KeyLayouts.For("Wacom PTK-440", 8).Proven);
        Assert.Equal(8, KeyLayouts.For("Wacom PTK-440", 8).KeyCount);
        Assert.Equal(0, KeyLayouts.For("Wacom PTK-440", 0).KeyCount);
    }

    [Fact]
    public void The_plain_column_keeps_saved_groups_with_a_gap_and_ignores_a_wrong_split()
    {
        var l = KeyLayouts.Column(8, new[] { 4, 4 });
        Assert.Equal(new[] { 4, 4 }, l.GroupSizes);
        Assert.True(l.Spots[4].Y - (l.Spots[3].Y + l.Spots[3].H) > l.Spots[3].Y - (l.Spots[2].Y + l.Spots[2].H));
        Assert.Equal(new[] { 8 }, KeyLayouts.Column(8, new[] { 4, 3 }).GroupSizes);
    }

    [Fact]
    public void Turned_round_the_drawing_is_turned_half_round()
    {
        var l = KeyLayouts.For("Wacom PTK-440", 6);
        var t = l.TurnedSpots;
        Assert.True(t[0].Y > t[5].Y, "turned, key 1 is at the bottom");
        Assert.Equal(l.Width - l.Spots[0].X - l.Spots[0].W, t[0].X, 6);
        var r = l.TurnedRing!.Value;
        Assert.Equal(l.Height - l.Ring!.Value.Y - l.Ring!.Value.D, r.Y, 6);
    }

    // ── the sheet's model with a layout ──

    private static void Tap(KeyMappingViewModel vm, int count, int key)
    {
        var s = new bool[count]; s[key] = true;
        vm.OnKeys(s);
        vm.OnKeys(new bool[count]);
    }

    [Fact]
    public void A_first_run_on_a_proven_tablet_has_its_groups_and_names()
    {
        var vm = new KeyMappingViewModel(6, null, false, KeyLayouts.For("Wacom PTK-440", 6));
        Assert.True(vm.Layout.Proven);
        Assert.False(vm.DrawTurned);
        Assert.Equal("Above the ring", vm.GroupText);
        Assert.Equal(new[] { false, false, false, true, false, false }, vm.Places.Select(p => p.StartsNewGroup));
        for (int k = 0; k < 3; k++) Tap(vm, 6, k);
        Assert.Equal("Below the ring", vm.GroupText);
        Assert.Equal(new[] { 3, 3 }, vm.Result.Groups);
    }

    [Fact]
    public void Turned_round_a_proven_layout_is_drawn_turned_lit_in_the_tablets_own_order_and_saved_as_not_turned()
    {
        var vm = new KeyMappingViewModel(6, null, flipped: true, KeyLayouts.For("Wacom PTK-440", 6));
        Assert.True(vm.DrawTurned);
        Assert.True(vm.Places[0].IsLit);
        Assert.Equal("Group 1 of 2", vm.GroupText);           // "Above the ring" would be wrong as seen
        Tap(vm, 6, 5);
        Assert.False(vm.Result.Flipped);
        Assert.Equal(5, vm.Result.Keys[0]);
    }

    [Fact]
    public void Turned_round_the_plain_column_is_saved_as_turned_as_before()
    {
        var vm = new KeyMappingViewModel(3, null, flipped: true, KeyLayouts.For("TestTablet", 3));
        Assert.False(vm.DrawTurned);
        Assert.False(vm.Layout.Proven);
        Tap(vm, 3, 0);
        Assert.True(vm.Result.Flipped);
    }

    [Fact]
    public void A_proven_layout_wins_over_groups_from_an_old_saved_mapping()
    {
        var vm = new KeyMappingViewModel(8, new[] { 2, 6 }, false, KeyLayouts.For("Wacom PTK-640", 8));
        Assert.Equal(new[] { 4, 4 }, vm.Result.Groups);
        var plain = new KeyMappingViewModel(8, new[] { 2, 6 }, false, KeyLayouts.For("Wacom PTH-660", 8));
        Assert.Equal(new[] { 2, 6 }, plain.Result.Groups);
        Assert.Equal(new[] { 2, 6 }, plain.Layout.GroupSizes);
    }
}
