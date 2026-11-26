using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Team rule 7.12 (wording), checked here so nobody has to remember it (team rule 0.7, Digital Groceries 19 Nov 2026):
/// the English texts never say "daemon" (say "driver"), and always write "Touch Ring(s)" and "Touch Strip(s)" with two
/// capitals, never "touch ring" or "Touch ring".</summary>
public class WordingRulesTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private static JObject English() =>
        JObject.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "OpenTabletArtist", "Assets", "i18n", "en.json")));

    [Fact]
    public void EnglishTexts_NeverSayDaemon()
    {
        var bad = English().Properties()
            .Where(p => p.Value.Type == JTokenType.String && Regex.IsMatch((string)p.Value!, @"\bdaemons?\b", RegexOptions.IgnoreCase))
            .Select(p => p.Name).ToList();
        Assert.True(bad.Count == 0, "Say \"driver\", not \"daemon\" (team rule 7.12): " + string.Join(", ", bad));
    }

    [Fact]
    public void EnglishTexts_WriteTouchRingAndTouchStripWithTwoCapitals()
    {
        var wrong = new Regex(@"\b(touch|Touch) (ring|strip)s?\b");
        var bad = English().Properties()
            .Where(p => p.Value.Type == JTokenType.String && wrong.Matches((string)p.Value!).Any(m => !m.Value.StartsWith("Touch R") && !m.Value.StartsWith("Touch S")))
            .Select(p => p.Name).ToList();
        Assert.True(bad.Count == 0, "Write \"Touch Ring\" / \"Touch Strip\" with two capitals (team rule 7.12): " + string.Join(", ", bad));
    }
}
