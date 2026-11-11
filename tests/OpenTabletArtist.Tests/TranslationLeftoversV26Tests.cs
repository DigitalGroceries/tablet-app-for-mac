using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Version 26: texts that were still English in Bulgarian, German, French, Japanese, Russian and Chinese
/// (settings backup, diagnostic file, update thanks, status light, Touch Strip names…) are translated, and stay so.
/// macOS privacy names (Input Monitoring, Accessibility, Device Control) are left as they were.</summary>
public class TranslationLeftoversV26Tests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private static JObject Lang(string code) =>
        JObject.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "OpenTabletArtist", "Assets", "i18n", code + ".json")));

    private static readonly string[] Keys =
    {
        "update.donor_thanks",
        "update.thanks_title",
        "update.installed_title",
        "update.thanks_ok",
        "tablet.strip.n",
        "home.version",
        "backup.section",
        "backup.description",
        "backup.export",
        "backup.import",
        "backup.apply",
        "backup.auto.title",
        "backup.auto.description",
        "backup.auto.folder",
        "backup.auto.restore",
        "backup.export.dialog",
        "backup.import.dialog",
        "backup.export.done",
        "backup.export.failed",
        "backup.preview.title",
        "backup.preview.check",
        "backup.preview.nothing",
        "backup.import.failed",
        "backup.area.config",
        "backup.import.done",
        "backup.kind.new",
        "backup.kind.changed",
        "backup.kind.same",
        "backup.area.tablet",
        "backup.area.pen",
        "backup.area.app",
        "backup.area.ring",
        "backup.area.preset",
        "backup.area.pref",
        "backup.area.pen.settings",
        "backup.area.mainpen",
        "backup.area.tablet.driver",
        "backup.area.app.settings",
        "backup.area.appbuttons",
        "backup.area.ring.modes",
        "backup.area.ringbackup",
        "diag.zip.title",
        "diag.zip.button",
        "diag.zip.working",
        "diag.zip.done",
        "diag.zip.failed",
        "diag.report.title",
        "diag.report.text",
        "diag.report.added",
        "conflict.process.title",
        "conflict.process.detail",
        "statusled.section",
        "statusled.toggle",
        "statusled.description",
        "appmenu.about",
        "appmenu.export",
        "appmenu.import",
        "tablet.about.touch_trackpad_beta",
        "tablet.about.touch_trackpad_switch"
    };

    /// <summary>Words that really are the same as in English in that language.</summary>
    private static readonly HashSet<(string Lang, string Key)> SameAsEnglish = new()
    {
        ("de", "update.thanks_ok"), ("fr", "update.thanks_ok"), ("ja", "update.thanks_ok"), ("ru", "update.thanks_ok"),
        ("de", "home.version"), ("fr", "home.version"), ("de", "backup.area.tablet"), ("fr", "backup.area.app"),
        ("de", "tablet.strip.n"),   // German keeps "Touch Strip" (the file's own word)
    };

    [Theory]
    [InlineData("bg")] [InlineData("de")] [InlineData("fr")] [InlineData("ja")] [InlineData("ru")] [InlineData("zh-CN")]
    public void TheseTexts_AreTranslated(string code)
    {
        var en = Lang("en");
        var lang = Lang(code);
        var english = Keys.Where(k => en[k] != null)
            .Where(k => lang[k] == null || ((string?)lang[k] == (string?)en[k] && !SameAsEnglish.Contains((code, k))))
            .ToList();
        Assert.True(english.Count == 0, code + " still English: " + string.Join(", ", english));
    }

    [Theory]
    [InlineData("bg")] [InlineData("de")] [InlineData("fr")] [InlineData("ja")] [InlineData("ru")] [InlineData("zh-CN")]
    public void Placeholders_AreKept(string code)
    {
        var en = Lang("en");
        var lang = Lang(code);
        foreach (var k in Keys.Where(k => en[k] != null && lang[k] != null))
        {
            var want = System.Text.RegularExpressions.Regex.Matches((string)en[k]!, @"\{\w+\}").Select(m => m.Value).OrderBy(x => x);
            var got = System.Text.RegularExpressions.Regex.Matches((string)lang[k]!, @"\{\w+\}").Select(m => m.Value).OrderBy(x => x);
            Assert.True(want.SequenceEqual(got), $"{code} {k}: placeholders differ");
        }
    }
}
