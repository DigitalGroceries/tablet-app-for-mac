using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>Version 30 (Digital Groceries 24 Nov 2026: "do we have the app translated to japanese and indian? add them if we dont",
/// then "also the most popular african language too"): Japanese was already there; Hindi and Swahili are new. Every key
/// of en.json, the same placeholders, offered in Settings › System › Language, and a Mac set to Hindi (India) or
/// Swahili (Kenya, Tanzania) gets them.</summary>
public class NewLanguagesV30Tests
{
    public static readonly TheoryData<string> Codes = new() { "hi", "sw" };

    static Dictionary<string, string> Table(string code) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(SettingSearchIndexWalk.IndexFolder(), "..", "i18n", code + ".json")))!;

    [Theory]
    [MemberData(nameof(Codes))]
    public void Every_english_key_is_there_with_its_placeholders(string code)
    {
        var en = Table("en");
        var t = Table(code);
        Assert.Equal(en.Keys.OrderBy(k => k), t.Keys.OrderBy(k => k));
        var holes = new Regex(@"\{[^}]*\}");
        foreach (var (key, english) in en)
        {
            Assert.False(string.IsNullOrWhiteSpace(t[key]), $"{code}: {key} is empty");
            Assert.Equal(holes.Matches(english).Select(m => m.Value).OrderBy(x => x),
                         holes.Matches(t[key]).Select(m => m.Value).OrderBy(x => x));
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(Codes))]
    public void The_app_offers_and_uses_it(string code)
    {
        var preference = Localizer.Preference;
        try
        {
            Assert.Contains(Localizer.Languages, l => l.Code == code);
            Localizer.SetPreference(code);
            Assert.Equal(code, Localizer.ResolvedCode);
            Assert.Equal(Table(code)["nav.home"], Localizer.T("nav.home"));
        }
        finally { Localizer.SetPreference(preference); }
    }

    [Fact]
    public void A_Mac_in_India_or_East_Africa_gets_its_language()
    {
        Assert.Equal("hi", Localizer.Normalize("hi"));
        Assert.Equal("hi", Localizer.Normalize("HI"));
        Assert.Equal("sw", Localizer.Normalize("sw"));
        Assert.Contains(Localizer.Languages, l => l.Code == "ja");     // Japanese was already in
    }

    [Fact]
    public void Daemon_is_never_written_as_a_demon()
    {
        foreach (var bad in new[] { "दानव", "दैत्य", "राक्षस" }) Assert.DoesNotContain(Table("hi").Values, v => v.Contains(bad));
        foreach (var bad in new[] { "pepo", "shetani", "jini" })
            Assert.DoesNotContain(Table("sw").Values, v => Regex.IsMatch(v, @"\b" + bad + @"\b", RegexOptions.IgnoreCase));
    }
}
