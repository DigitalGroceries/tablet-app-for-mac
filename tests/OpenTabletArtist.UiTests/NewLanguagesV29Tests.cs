using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>Version 29 (item 4, Digital Groceries 17-18 Nov): Italian, Swedish and Brazilian Portuguese ("the one spoken by more
/// people"). Every key of en.json, the same placeholders, offered in Settings › System › Language, and a Portuguese
/// Mac from Portugal gets the Brazilian texts rather than English.</summary>
public class NewLanguagesV29Tests
{
    public static readonly TheoryData<string> Codes = new() { "it", "sv", "pt-BR" };

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
    public void Every_Portuguese_Mac_gets_the_Brazilian_texts()
    {
        Assert.Equal("pt-BR", Localizer.Normalize("pt"));
        Assert.Equal("pt-BR", Localizer.Normalize("pt-PT"));
        Assert.Equal("pt-BR", Localizer.Normalize("pt-BR"));
        Assert.Equal("it", Localizer.Normalize("it"));
        Assert.Equal("sv", Localizer.Normalize("SV"));
    }
}
