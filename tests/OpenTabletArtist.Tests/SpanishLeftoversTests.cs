using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>TASKS #20: es.json has a Spanish value for every English key. A value may equal the English one only
/// for names that stay the same in Spanish (brands, macOS setting names the file keeps in English, technical
/// abbreviations, and words spelt the same).</summary>
public class SpanishLeftoversTests
{
    /// <summary>Keys whose Spanish value is meant to equal the English one, and why.</summary>
    public static readonly IReadOnlyDictionary<string, string> SameAsEnglish = new Dictionary<string, string>
    {
        ["app.name"] = "product name",
        ["affinity.section"] = "product name (Affinity, by Canva)",
        ["nav.scribble"] = "the app's Scribble page name",
        ["settings.tab.dev"] = "technical abbreviation",
        ["perm.input_monitoring"] = "macOS setting name, kept in English throughout es.json",
        ["perm.accessibility"] = "macOS setting name, kept in English throughout es.json",
        ["perm.device_control"] = "macOS setting name, kept in English throughout es.json",
        ["advanced.tab.plugins"] = "'plugins' is the usual Spanish word too",
        ["tablet.tab.expresskeys"] = "Wacom feature name",
        ["tablet.tab.json"] = "file format",
        ["tablet.mapping.diagonal"] = "same word in Spanish",
        ["scribble.azal"] = "azimuth / altitude abbreviation",
    };

    static string I18nDir()
    {
        var d = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "OpenTabletArtist", "Assets", "i18n"))) d = d.Parent;
        Assert.NotNull(d);
        return Path.Combine(d!.FullName, "OpenTabletArtist", "Assets", "i18n");
    }

    static Dictionary<string, string> Load(string code) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(I18nDir(), code + ".json")))!;

    [Fact]
    public void Spanish_HasEveryKey_AndNoEnglishLeftovers()
    {
        var en = Load("en");
        var es = Load("es");
        var missing = en.Keys.Where(k => !es.ContainsKey(k)).ToList();
        Assert.True(missing.Count == 0, "es.json lacks: " + string.Join(", ", missing));
        var leftovers = en.Where(p => es.TryGetValue(p.Key, out var v) && v == p.Value && !SameAsEnglish.ContainsKey(p.Key))
            .Select(p => $"{p.Key} = \"{p.Value}\"").ToList();
        Assert.True(leftovers.Count == 0, "es.json still English: " + string.Join("; ", leftovers));
    }

    [Fact]
    public void Spanish_SaysTableta_NotTablet()
    {
        // Product names keep their English word.
        string[] names = { "Tablet App for Mac", "Wacom Tablet Utility", "Tablet Software" };
        foreach (var (k, v) in Load("es"))
        {
            var text = names.Aggregate(v, (t, n) => t.Replace(n, ""));
            Assert.False(System.Text.RegularExpressions.Regex.IsMatch(text, @"\b[Tt]ablets?\b"), $"{k}: {v}");
        }
    }

    [Fact]
    public void Spanish_KeepsThePlaceholders()
    {
        var en = Load("en");
        var rx = new System.Text.RegularExpressions.Regex(@"\{\w+\}");
        foreach (var (k, v) in Load("es"))
        {
            if (!en.TryGetValue(k, out var e)) continue;
            Assert.Equal(rx.Matches(e).Select(m => m.Value).OrderBy(x => x), rx.Matches(v).Select(m => m.Value).OrderBy(x => x));
        }
    }
}
