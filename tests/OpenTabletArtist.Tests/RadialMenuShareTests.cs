using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using Xunit;
using RadialMenuBinding = OpenTabletArtist.Domain.RadialMenuBinding;

namespace OpenTabletArtist.Tests;

/// <summary>V29 #170 (Zak 20 Nov: "I have a menu in front of me = I should be able to control it the same no matter what
/// app is open"): one radial menu and palette per tablet, in All, every application's table and every pen's table.</summary>
public class RadialMenuShareTests
{
    private static string Tablet => "TestTablet-OneMenu-" + Guid.NewGuid().ToString("N")[..8];

    private static string Menu(string firstLabel, int size = RadialMenuDef.DefaultSize, int transparency = RadialMenuDef.DefaultTransparency)
    {
        var m = RadialMenuDefaults.Create(Localizer.Te);
        m.Slices[0].Label = firstLabel; m.Size = size; m.Transparency = transparency;
        return RadialMenuJson.Serialize(m);
    }

    private static string Default => RadialMenuJson.Serialize(RadialMenuDefaults.Create(Localizer.Te));

    private static BindingSettings Table(params PluginSettingStore?[] penButtons)
    {
        var b = new BindingSettings { PenButtons = new PluginSettingStoreCollection(), AuxButtons = new PluginSettingStoreCollection() };
        foreach (var s in penButtons) b.PenButtons.Add(s!);
        return b;
    }

    private static string First(BindingSettings b) => RadialMenuJson.Parse(RadialMenuShare.FirstMenuJson(b)).Slices[0].Label;

    [Fact]
    public void Unify_GivesEveryRadialButtonTheMenu_AndLeavesOtherButtonsAlone()
    {
        var b = Table(AuxKeyBinding.MakeKeyBinding("E"), RadialMenuBinding.Make(Menu("A")));
        b.AuxButtons.Add(RadialMenuBinding.Make(Menu("B")));
        Assert.True(RadialMenuShare.Differs(b, Menu("A")));
        Assert.Equal(1, RadialMenuShare.Unify(b, Menu("A")));
        Assert.False(RadialMenuShare.Differs(b, Menu("A")));
        Assert.Equal("A", RadialMenuBinding.ReadMenu(b.AuxButtons[0]).Slices[0].Label);
        Assert.False(RadialMenuBinding.Is(b.PenButtons[0]));
        Assert.Equal(0, RadialMenuShare.Unify(b, Menu("A")));   // nothing left to change
    }

    [Fact]
    public void AChangedMenu_IsTheEdit()
    {
        var before = RadialMenuShare.MenusByPosition(Table(null, RadialMenuBinding.Make(Menu("A"))));
        var after = Table(null, RadialMenuBinding.Make(Menu("A", transparency: 12)));
        Assert.Equal(RadialMenuShare.Canonical(Menu("A", transparency: 12)), RadialMenuShare.EditedMenu(before, after, Menu("A"), Default));
    }

    [Fact]
    public void ANewButtonWithTheDefaultMenu_IsNotAnEdit_SoItGetsTheTabletsMenu()
    {
        var before = RadialMenuShare.MenusByPosition(Table(null, null));
        var after = Table(RadialMenuBinding.Make(), null);
        Assert.Null(RadialMenuShare.EditedMenu(before, after, Menu("Mine"), Default));
        // with no menu anywhere yet, the new button's menu starts the tablet's one menu
        Assert.Equal(RadialMenuShare.Canonical(Default), RadialMenuShare.EditedMenu(before, after, null, Default));
    }

    [Fact]
    public void ANewButtonWithItsOwnMenu_IsTheEdit()
    {
        var before = RadialMenuShare.MenusByPosition(Table(null, null));
        var after = Table(RadialMenuBinding.Make(Menu("Edited in Change...")), null);
        Assert.Equal(RadialMenuShare.Canonical(Menu("Edited in Change...")), RadialMenuShare.EditedMenu(before, after, Menu("Mine"), Default));
    }

    [Fact]
    public void AnUnrelatedEdit_ChangesNoMenu()
    {
        var table = Table(AuxKeyBinding.MakeKeyBinding("E"), RadialMenuBinding.Make(Menu("A")));
        var before = RadialMenuShare.MenusByPosition(table);
        table.PenButtons[0] = AuxKeyBinding.MakeKeyBinding("B");
        Assert.Null(RadialMenuShare.EditedMenu(before, table, Menu("A"), Default));
    }

    [Fact]
    public void Current_IsAllsMenu_ElseTheRemembered_ElseAnApplications()
    {
        var tablet = Tablet;
        Assert.Null(RadialMenuShare.Current(tablet, Table(null, null)));
        PerAppBindingStore.Add(tablet, "com.adobe.Photoshop", "Adobe Photoshop 2025", "", Table(null, RadialMenuBinding.Make(Menu("Ps"))));
        Assert.Equal(RadialMenuShare.Canonical(Menu("Ps")), RadialMenuShare.Current(tablet, Table(null, null)));
        AppSettings.Set(RadialMenuShare.Key(tablet), Menu("Remembered"));
        Assert.Equal(RadialMenuShare.Canonical(Menu("Remembered")), RadialMenuShare.Current(tablet, Table(null, null)));
        Assert.Equal(RadialMenuShare.Canonical(Menu("All")), RadialMenuShare.Current(tablet, Table(null, RadialMenuBinding.Make(Menu("All")))));
    }

    [Fact]
    public void ShareToStores_PutsTheMenuInEveryApplication_AndTheAllStash_AndKeepsTheOldOneOnce()
    {
        var tablet = Tablet;
        PerAppBindingRuntime.SyncAllBindings(tablet, Table(null, RadialMenuBinding.Make(Menu("All"))));
        PerAppBindingStore.Add(tablet, "com.adobe.Photoshop", "Adobe Photoshop 2025", "", Table(null, RadialMenuBinding.Make(Menu("Ps", size: 12))));
        PerAppBindingStore.Add(tablet, "com.seriflabs.affinity", "Affinity", "", Table(null, RadialMenuBinding.Make(Menu("Af", size: 69))));
        PerAppBindingStore.Add(tablet, "com.apple.Safari", "Safari", "", Table(AuxKeyBinding.MakeKeyBinding("E")));

        Assert.Equal(2, RadialMenuShare.ShareToStores(tablet, Menu("All")));
        foreach (var app in PerAppBindingStore.List(tablet).Where(a => a.BundleId != "com.apple.Safari"))
            Assert.Equal("All", First(app.Bindings));
        Assert.False(RadialMenuBinding.Is(PerAppBindingStore.Find(tablet, "com.apple.Safari")!.Bindings.PenButtons[0]));   // no radial button: untouched
        Assert.Equal("Ps", RadialMenuJson.Parse(AppSettings.Get(RadialMenuShare.ReplacedKey(tablet, "app:com.adobe.Photoshop"))).Slices[0].Label);

        // a later change: every table follows, the stash too, and the first kept menu stays
        Assert.Equal(3, RadialMenuShare.ShareToStores(tablet, Menu("All", transparency: 12)));
        Assert.Equal(12, RadialMenuJson.Parse(RadialMenuShare.FirstMenuJson(PerAppBindingRuntime.StashedAllBindings(tablet))).Transparency);
        Assert.Equal(12, RadialMenuJson.Parse(RadialMenuShare.FirstMenuJson(PerAppBindingStore.Find(tablet, "com.seriflabs.affinity")!.Bindings)).Transparency);
        Assert.Equal("Ps", RadialMenuJson.Parse(AppSettings.Get(RadialMenuShare.ReplacedKey(tablet, "app:com.adobe.Photoshop"))).Slices[0].Label);
        Assert.Equal(0, RadialMenuShare.ShareToStores(tablet, Menu("All", transparency: 12)));   // idempotent
    }

    [Fact]
    public void AnyTableUses_LooksInEveryTable()
    {
        var tablet = Tablet;
        PerAppBindingStore.Add(tablet, "com.adobe.Photoshop", "Adobe Photoshop 2025", "", Table(null, RadialMenuBinding.Make(Menu("Ps"))));
        Assert.True(RadialMenuShare.AnyTableUses(tablet, Table(null, null), Menu("Ps")));
        Assert.False(RadialMenuShare.AnyTableUses(tablet, Table(null, null), Menu("Other")));
    }

    // ── Grok's review of #170 ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void WhenAButtonIsAddedOrRemoved_AnUntouchedMenuIsNotAnEdit()   // finding 3: positions shift
    {
        var before = RadialMenuShare.MenusByPosition(Table(null, RadialMenuBinding.Make(Menu("A"))));
        var after = Table(AuxKeyBinding.MakeKeyBinding("E"), null, RadialMenuBinding.Make(Menu("A")));   // one more pen button, menu moved
        Assert.Null(RadialMenuShare.EditedMenu(before, after, Menu("A"), Default));
        var edited = Table(AuxKeyBinding.MakeKeyBinding("E"), null, RadialMenuBinding.Make(Menu("A", size: 9)));
        Assert.Equal(RadialMenuShare.Canonical(Menu("A", size: 9)), RadialMenuShare.EditedMenu(before, edited, Menu("A"), Default));
    }

    [Fact]
    public void WhileAllIsBehind_TheRememberedMenuWins()   // finding 2
    {
        var tablet = Tablet;
        AppSettings.Set(RadialMenuShare.Key(tablet), Menu("Newer"));
        var all = Table(null, RadialMenuBinding.Make(Menu("Older")));
        Assert.Equal("Older", RadialMenuJson.Parse(RadialMenuShare.Current(tablet, all)).Slices[0].Label);
        RadialMenuShare.SetAllBehind(tablet, true);
        Assert.Equal("Newer", RadialMenuJson.Parse(RadialMenuShare.Current(tablet, all)).Slices[0].Label);
        RadialMenuShare.SetAllBehind(tablet, false);
        Assert.False(RadialMenuShare.AllIsBehind(tablet));
    }

    [Fact]
    public void ABindingPutBack_IsNotAnEdit()   // finding 1
    {
        Assert.False(RadialMenuShare.IsRestoring);
        using (RadialMenuShare.Restoring()) Assert.True(RadialMenuShare.IsRestoring);
        Assert.False(RadialMenuShare.IsRestoring);
    }
}
