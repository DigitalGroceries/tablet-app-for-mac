using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.Views;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// TASKS #76 (Version 28, agreed with Digital Groceries 14 Nov): the whole app is usable with no tablet plugged in. The user picks
/// a model (or the sample tablet), the app saves a normal profile for it from the built-in catalog, the pages read its
/// specs from the catalog while it isn't connected, and a different model plugged in instead gets an offer to copy
/// what the two share (pen and screen mapping).
/// </summary>
public class SetUpBeforeConnectingTests
{
    private const string Sample = OfflineTablet.SampleTablet; // "Wacom PTK-440"

    [Fact]
    public void TheSampleTabletIsInTheCatalog()
    {
        Assert.NotNull(OfflineTablet.ConfigFor(Sample));
        Assert.Contains(SupportedTabletsCatalog.All, t => t.Name == Sample);
    }

    [Fact]
    public void CatalogJson_LooksLikeTheDriversReportOfAConnectedTablet()
    {
        var json = OfflineTablet.CatalogTabletJson("wacom ptk-440"); // any case
        Assert.NotNull(json);
        var props = json!["Properties"]!;
        Assert.Equal(Sample, props["Name"]!.ToString());
        Assert.True(props["Specifications"]!["Digitizer"]!["Width"]!.Value<float>() > 0);
        // The About page reads the same shape the driver sends.
        var about = TabletAboutInfo.From(new JArray(json), Sample);
        Assert.NotNull(about);
    }

    // GitHub #119 (jbrannigan, V28): the CTL-4100WL has no mouse, so its catalog entry has "MouseButtons": null;
    // opening its page before it was connected crashed the app and the data poll failed every 30 s.
    [Fact]
    public void CatalogJson_ForATabletWithoutAMouse_GivesItsAboutFacts()
    {
        var json = OfflineTablet.CatalogTabletJson("Wacom CTL-4100WL");
        Assert.NotNull(json);
        Assert.Equal(JTokenType.Null, json!["Properties"]!["Specifications"]!["MouseButtons"]!.Type);   // the shape that crashed
        var about = TabletAboutInfo.From(new JArray(json), "Wacom CTL-4100WL");
        Assert.NotNull(about);
        Assert.Null(about!.MouseButtons);
        Assert.Equal(5u, about.ExpressKeys);
        Assert.Equal(2u, about.PenButtons);
        Assert.Equal(152f, about.WidthMm, 3);
    }

    [Fact]
    public void UnknownModel_HasNoCatalogEntryAndNoProfile()
    {
        Assert.Null(OfflineTablet.CatalogTabletJson("Wacom XYZ-0000"));
        Assert.Null(OfflineTablet.BuildProfile("Wacom XYZ-0000"));
    }

    [Fact]
    public void BuildProfile_GivesAFullWellFormedProfile()
    {
        var profile = OfflineTablet.BuildProfile("wacom ptk-440");
        Assert.NotNull(profile);
        Assert.Equal(Sample, profile!.Tablet);                     // the catalog's spelling, as the driver reports it
        Assert.NotNull(profile.OutputMode);
        var abs = profile.AbsoluteModeSettings;
        Assert.NotNull(abs);
        Assert.True(abs!.Tablet.Width > 0 && abs.Tablet.Height > 0);
        Assert.True(abs.Display.Width > 0 && abs.Display.Height > 0);
        Assert.NotNull(profile.BindingSettings);
        Assert.True(profile.BindingSettings.PenButtons.Count >= 2);  // Intuos4 pens have two buttons
        Assert.True(profile.BindingSettings.AuxButtons.Count > 0);   // and the tablet has ExpressKeys
    }

    [Fact]
    public void WithRemembered_AddsCatalogEntriesButTheDriversReportWins()
    {
        var connected = new JArray(new JObject { ["Properties"] = new JObject { ["Name"] = Sample, ["FromDriver"] = true } });

        var all = OfflineTablet.WithRemembered(connected, new[] { Sample, "Wacom PTH-660", "Wacom XYZ-0000", "" });

        var names = all.Select(t => t["Properties"]!["Name"]!.ToString()).ToList();
        Assert.Equal(new[] { Sample, "Wacom PTH-660" }, names);           // unknown and empty names skipped
        Assert.True(all[0]["Properties"]!["FromDriver"]!.Value<bool>());  // the connected one is the driver's own
    }

    [Fact]
    public void Offer_WhenADifferentModelIsConnected()
    {
        var offer = OfflineTablet.PickOffer(new[] { "Wacom PTH-660" }, new[] { Sample }, _ => false);
        Assert.Equal(new OfflineSetupOffer("Wacom PTH-660", Sample), offer);
    }

    [Fact]
    public void NoOffer_WhenTheChosenModelIsConnected_OrNothingWasSetUpBeforehand_OrItWasAnswered()
    {
        Assert.Null(OfflineTablet.PickOffer(new[] { Sample }, new[] { Sample }, _ => false));
        Assert.Null(OfflineTablet.PickOffer(new[] { "Wacom PTH-660" }, Array.Empty<string>(), _ => false));
        Assert.Null(OfflineTablet.PickOffer(new[] { "Wacom PTH-660" }, new[] { Sample }, _ => true));
        Assert.Null(OfflineTablet.PickOffer(Array.Empty<string>(), new[] { Sample }, _ => false));
        // V28 #76 review: only a tablet first seen AFTER the model was set up beforehand gets the offer, so a tablet the
        // user already had keeps its own settings.
        Assert.Null(OfflineTablet.PickOffer(new[] { "Wacom PTH-660" }, new[] { Sample }, _ => false, _ => false));
        Assert.NotNull(OfflineTablet.PickOffer(new[] { "Wacom PTH-660" }, new[] { Sample }, _ => false, _ => true));
    }

    [Fact]
    public void OnlyATabletFirstSeenAfterTheSetUp_IsNew()
    {
        var setUp = new DateTime(2026, 11, 15, 22, 0, 0);
        Assert.True(OfflineTablet.IsNewSince(setUp.AddMinutes(5), setUp));      // plugged in for the first time later
        Assert.False(OfflineTablet.IsNewSince(setUp.AddDays(-3), setUp));      // the user's own tablet, seen before
        Assert.False(OfflineTablet.IsNewSince(null, setUp));
        Assert.False(OfflineTablet.IsNewSince(setUp, null));
    }

    [Fact]
    public void CopyShared_CopiesPenAndMapping_LeavesExpressKeysAlone()
    {
        var from = OfflineTablet.BuildProfile(Sample)!;
        var to = OfflineTablet.BuildProfile("Wacom PTH-660")!;
        Assert.NotNull(to);
        var toAuxBefore = Newtonsoft.Json.JsonConvert.SerializeObject(to.BindingSettings.AuxButtons);

        // The user's own choices on the model they set up beforehand.
        from.BindingSettings.TipActivationThreshold = 7.5f;
        from.BindingSettings.PenButtons[1] = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Binding.MouseBinding));
        from.BindingSettings.DisableTilt = true;
        from.AbsoluteModeSettings!.Display = new AreaSettings { Width = 2560, Height = 1440, X = 1280, Y = 720 };
        from.AbsoluteModeSettings.Tablet = new AreaSettings { Width = 100, Height = 50, X = 50, Y = 25 };

        OfflineTablet.CopyShared(from, to, (200, 100), (400, 300));

        Assert.Equal(7.5f, to.BindingSettings.TipActivationThreshold);
        Assert.Equal(from.BindingSettings.PenButtons[1]!.Path, to.BindingSettings.PenButtons[1]!.Path);
        Assert.True(to.BindingSettings.DisableTilt);
        Assert.Equal(2560, to.AbsoluteModeSettings!.Display.Width);
        // Same share of the new surface: half the width, half the height.
        Assert.Equal(200, to.AbsoluteModeSettings.Tablet.Width);
        Assert.Equal(150, to.AbsoluteModeSettings.Tablet.Height);
        Assert.Equal(100, to.AbsoluteModeSettings.Tablet.X);
        Assert.Equal(75, to.AbsoluteModeSettings.Tablet.Y);
        // ExpressKeys differ between models: untouched.
        Assert.Equal(toAuxBefore, Newtonsoft.Json.JsonConvert.SerializeObject(to.BindingSettings.AuxButtons));
        // A copy, not shared objects: changing one later doesn't change the other.
        from.AbsoluteModeSettings.Display.Width = 1;
        Assert.Equal(2560, to.AbsoluteModeSettings.Display.Width);
    }

    [Theory]
    [InlineData("ptk 440", true)]
    [InlineData("PTK440", true)]
    [InlineData("440", true)]
    [InlineData("wacom ptk", true)]
    [InlineData("pth", false)]
    [InlineData("", true)]
    public void Picker_FindsTheModelHoweverItIsTyped(string query, bool found)
    {
        var tablets = new[] { ("Wacom PTK-440", "157 × 98 mm"), ("Huion H420", "") };
        var hits = ChooseTabletDialog.Filter(tablets, query).Select(t => t.Name).ToList();
        Assert.Equal(found, hits.Contains("Wacom PTK-440"));
    }

    [Fact]
    public void EveryLanguageHasTheNewTexts()
    {
        var dir = FindI18nDir();
        var keys = new[]
        {
            "notablet.title", "notablet.body", "notablet.choose", "home.no_tablets", "home.no_tablets_hint",
            "choose.title", "choose.body", "choose.search", "choose.sample", "choose.setup", "choose.no_driver",
            "choose.unknown", "choose.failed", "choose.not_saved", "confirm.tablet_set_up", "confirm.settings_copied",
            "offer.text", "offer.copy", "offer.not_now",
        };
        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            var d = JObject.Parse(File.ReadAllText(file));
            foreach (var k in keys)
                Assert.False(string.IsNullOrWhiteSpace(d[k]?.ToString()), $"{Path.GetFileName(file)} lacks {k}");
            // The offer must keep both placeholders in every language.
            Assert.Contains("{connected}", d["offer.text"]!.ToString());
            Assert.Contains("{chosen}", d["offer.text"]!.ToString());
        }
    }

    private static string FindI18nDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "OpenTabletArtist", "Assets", "i18n")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return Path.Combine(dir!, "OpenTabletArtist", "Assets", "i18n");
    }
}
