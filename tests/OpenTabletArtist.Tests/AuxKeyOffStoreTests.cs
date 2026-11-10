using Newtonsoft.Json.Linq;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletDriver.Desktop.Reflection;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>TASKS #28: each ExpressKey's own On/Off, and how it combines with the "All buttons" switch.</summary>
public class AuxKeyOffStoreTests
{
    private static string Tablet => "TestTablet-KeyOff-" + Guid.NewGuid().ToString("N")[..8];

    private static PluginSettingStoreCollection Keys(params string[] keys)
    {
        var aux = new PluginSettingStoreCollection();
        foreach (var k in keys) aux.Add(AuxKeyBinding.MakeKeyBinding(k)!);
        return aux;
    }

    private static string KeyOf(PluginSettingStore? s) => AuxKeyBinding.ReadKey(s);

    // ── the four combinations: All buttons On/Off x key On/Off ──────────────────────────────────

    [Theory]
    [InlineData(true, true, true)]     // All On,  key On  -> works
    [InlineData(true, false, false)]   // All On,  key Off -> does nothing
    [InlineData(false, true, false)]   // All Off, key On  -> does nothing
    [InlineData(false, false, false)]  // All Off, key Off -> does nothing
    public void AKeyWorksOnlyWhenAllButtonsAndTheKeyAreBothOn(bool all, bool key, bool works) =>
        Assert.Equal(works, AuxKeyOffStore.Works(all, key));

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void WhatTheDriverGets_FollowsTheRule_AndTheShortcutIsKept(bool all, bool keyOn)
    {
        var aux = Keys("A", "B", "C");
        var off = new Dictionary<int, PluginSettingStore?>();
        if (!keyOn) Assert.True(AuxKeyOffStore.TurnOff(aux, off, 2));

        // The table the driver gets: nothing for an Off key; nothing at all while All buttons is Off.
        var driver = Keys("A", "B", "C");
        if (!keyOn) { var o2 = new Dictionary<int, PluginSettingStore?>(); AuxKeyOffStore.TurnOff(driver, o2, 2); }
        AuxKeyOffStore.ApplyAllButtons(driver, all);

        Assert.Equal(AuxKeyOffStore.Works(all, keyOn), driver[1] != null);
        Assert.Equal(all, driver[0] != null);       // key 1 is On: it follows All buttons alone
        Assert.Equal(all, driver[2] != null);
        // Whatever the switches say, the Off key's shortcut is still there to come back.
        Assert.Equal("B", KeyOf(AuxKeyOffStore.Shown(Keys("A", "B", "C"), keyOn ? new() : off, 2)));
    }

    [Fact]
    public void TurnOff_KeepsTheBinding_TurnOn_PutsItBack()
    {
        var aux = Keys("A", "B", "C");
        var off = new Dictionary<int, PluginSettingStore?>();
        Assert.True(AuxKeyOffStore.TurnOff(aux, off, 2));
        Assert.Null(aux[1]);
        Assert.False(AuxKeyOffStore.IsOn(off, 2));
        Assert.True(AuxKeyOffStore.IsOn(off, 1));
        Assert.Equal("B", KeyOf(AuxKeyOffStore.Shown(aux, off, 2)));
        Assert.False(AuxKeyOffStore.TurnOff(aux, off, 2));   // already Off

        Assert.True(AuxKeyOffStore.TurnOn(aux, off, 2));
        Assert.Equal("B", KeyOf(aux[1]));
        Assert.Empty(off);
        Assert.False(AuxKeyOffStore.TurnOn(aux, off, 2));
        Assert.False(AuxKeyOffStore.TurnOff(aux, off, 9));   // no such key
    }

    [Fact]
    public void EditingAnOffKey_ChangesWhatIsKept_NotWhatTheDriverGets()
    {
        var aux = Keys("A", "B");
        var off = new Dictionary<int, PluginSettingStore?>();
        AuxKeyOffStore.TurnOff(aux, off, 1);
        AuxKeyOffStore.SetBinding(aux, off, 1, AuxKeyBinding.MakeKeyBinding("Z"));
        Assert.Null(aux[0]);
        Assert.Equal("Z", KeyOf(AuxKeyOffStore.Shown(aux, off, 1)));
        AuxKeyOffStore.TurnOn(aux, off, 1);
        Assert.Equal("Z", KeyOf(aux[0]));

        AuxKeyOffStore.SetBinding(aux, off, 2, null);   // an On key: the table itself changes
        Assert.Null(aux[1]);
    }

    [Fact]
    public void Default_ClearsEveryAction_AndOffKeysStayOff()
    {
        var aux = Keys("A", "B", "C");
        var off = new Dictionary<int, PluginSettingStore?>();
        AuxKeyOffStore.TurnOff(aux, off, 3);
        AuxKeyOffStore.ClearAll(aux, off);
        Assert.All(aux, s => Assert.Null(s));
        Assert.False(AuxKeyOffStore.IsOn(off, 3));
        Assert.Null(AuxKeyOffStore.Shown(aux, off, 3));
    }

    // ── stored per application, like the binding ───────────────────────────────────────────────

    [Fact]
    public void EachTable_All_App_Pen_HasItsOwnOffKeys()
    {
        var t = Tablet;
        var all = AuxKeyOffStore.ScopeKey(t);
        var app = AuxKeyOffStore.ScopeKey(t, appBundleId: "com.adobe.Photoshop");
        var pen = AuxKeyOffStore.ScopeKey(t, penKey: "123");
        Assert.Equal(3, new[] { all, app, pen }.Distinct().Count());
        Assert.Equal(app, AuxKeyOffStore.ScopeKey(t, appBundleId: "COM.ADOBE.PHOTOSHOP"));   // app ids compare without case, as the app store does

        var a = Keys("A", "B");
        var offAll = new Dictionary<int, PluginSettingStore?>();
        AuxKeyOffStore.TurnOff(a, offAll, 1);
        AuxKeyOffStore.Save(all, offAll);

        Assert.Single(AuxKeyOffStore.Load(all));
        Assert.Empty(AuxKeyOffStore.Load(app));      // the application is not affected
        Assert.Empty(AuxKeyOffStore.Load(pen));

        AuxKeyOffStore.CopyScope(all, app);          // a new application starts as a copy of All
        Assert.Single(AuxKeyOffStore.Load(app));
        AuxKeyOffStore.Save(app, new());             // turning the key back On there leaves All alone
        Assert.Empty(AuxKeyOffStore.Load(app));
        Assert.Single(AuxKeyOffStore.Load(all));
    }

    // ── settings save / load round trip ────────────────────────────────────────────────────────

    [Fact]
    public void OffKeys_SurviveSaveAndLoad_IncludingTheKeptShortcut_AndAKeyThatHadNone()
    {
        var scope = AuxKeyOffStore.ScopeKey(Tablet, appBundleId: "com.example.Paint");
        var aux = Keys("A", "B", "C");
        aux[2] = null!;                                           // key 3 has no action
        var off = new Dictionary<int, PluginSettingStore?>();
        AuxKeyOffStore.TurnOff(aux, off, 2);
        AuxKeyOffStore.TurnOff(aux, off, 3);
        AuxKeyOffStore.Save(scope, off);

        // In memory …
        var back = AuxKeyOffStore.Load(scope);
        Assert.Equal(new[] { 2, 3 }, back.Keys.OrderBy(k => k));
        Assert.Equal("B", KeyOf(back[2]));
        Assert.Null(back[3]);

        // … and from the settings file on disk, as the next start of the app reads it.
        var file = Path.Combine(AppPaths.LocalAppData, "settings.json");
        var onDisk = JObject.Parse(File.ReadAllText(file));
        var raw = (string)onDisk[scope]!;
        var fromDisk = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<int, PluginSettingStore?>>(raw)!;
        Assert.Equal("B", KeyOf(fromDisk[2]));
        Assert.Null(fromDisk[3]);

        // Turning every key On again leaves nothing stored.
        AuxKeyOffStore.TurnOn(aux, back, 2);
        AuxKeyOffStore.TurnOn(aux, back, 3);
        AuxKeyOffStore.Save(scope, back);
        Assert.Empty(AuxKeyOffStore.Load(scope));
        Assert.True(string.IsNullOrEmpty(AppSettings.Get(scope)));
        Assert.Equal("B", KeyOf(aux[1]));
    }

    [Fact]
    public void ACorruptValue_ReadsAsNoOffKeys()
    {
        var scope = AuxKeyOffStore.ScopeKey(Tablet);
        AppSettings.Set(scope, "{not json");
        Assert.Empty(AuxKeyOffStore.Load(scope));
    }
}
