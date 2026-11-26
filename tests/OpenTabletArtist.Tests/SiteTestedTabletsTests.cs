using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>The owner's rule (25 Nov 2026): wherever we say what the app was tested with, we name every tablet it was
/// tested with, ours and the ones people using the app have told us work. The manual and the README have named them since
/// Version 30, but the first lines of the website's Home still said "Tested with a Wacom Intuos4", and at the first
/// correction only the list lower on that page was changed, not that sentence: nothing made anyone look at every place.
///
/// The list is kept in ONE place: the table under "Tested system and tablet" in the app's manual. This suite fails while
/// any other place that says what the app was tested with does not name every tablet in that table: the README's table,
/// the manual's first lines, the website's Home (the sentence at the top and the one under the list lower down), its
/// llms.txt and its Help page. To add a tablet: add its row to the manual's table, then follow the failing tests.</summary>
public class SiteTestedTabletsTests
{
    const string Where = " (the list is the table under 'Tested system and tablet' in OpenTabletArtist/Assets/manual/manual.md)";

    static string Root()
    {
        var d = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "site", "build_site.py"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));
    static string Manual => Read("OpenTabletArtist", "Assets", "manual", "manual.md");
    static string Home => Read("site", "src", "index.html");

    /// <summary>The rows of the first table under a "## heading" of a Markdown file: tablet, model, what it was tested with.</summary>
    static List<(string Tablet, string Model, string With)> Table(string markdown, string heading, string file)
    {
        var lines = markdown.Replace("\r", "").Split('\n');
        int at = System.Array.IndexOf(lines, heading);
        Assert.True(at >= 0, $"{file} has no '{heading}' heading.");
        var rows = new List<(string, string, string)>();
        bool inTable = false;
        foreach (var line in lines.Skip(at + 1))
        {
            if (line.StartsWith("## ")) break;
            if (!line.StartsWith("|")) { if (inTable) break; continue; }
            inTable = true;
            var cells = line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray();
            if (cells.Length < 3 || cells[0] == "Tablet" || cells[0].All(ch => ch is '-' or ':' or ' ')) continue;
            rows.Add((cells[0], cells[1], cells[2]));
        }
        return rows;
    }

    /// <summary>The tablets people using the app have told us work: the one list everything else is checked against.</summary>
    static List<(string Tablet, string Model, string With)> ByUsers
    {
        get
        {
            var rows = Table(Manual, "## Tested system and tablet", "The manual");
            Assert.True(rows.Count > 0, "The manual's 'Tested system and tablet' has no table of tablets.");
            return rows;
        }
    }

    /// <summary>The tablet the app is made and tested with by us, from the manual's "Our own test system" line.</summary>
    static (string Tablet, string Model) Ours
    {
        get
        {
            var m = Regex.Match(Manual, @"^Our own test system: .*?Wacom (.+?) \(([A-Z]+-\d+[A-Z]*)\)", RegexOptions.Multiline);
            Assert.True(m.Success, "The manual's 'Tested system and tablet' no longer starts with 'Our own test system: ... Wacom <tablet> (<model>)'.");
            return (m.Groups[1].Value, m.Groups[2].Value);
        }
    }

    static void NamesEveryModel(string text, string place)
    {
        Assert.True(text.Contains(Ours.Model), $"{place} does not name our own test tablet, the {Ours.Tablet} ({Ours.Model}).{Where}");
        foreach (var t in ByUsers)
            Assert.True(text.Contains(t.Model), $"{place} says what the app was tested with but does not name the {t.Tablet} ({t.Model}).{Where}");
    }

    [Fact]
    public void The_manual_has_the_list_with_a_model_number_for_every_tablet()
    {
        Assert.Matches(@"^[A-Z]{2,4}-\d{2,4}[A-Z]*$", Ours.Model);
        foreach (var t in ByUsers)
        {
            Assert.True(Regex.IsMatch(t.Model, @"^[A-Z]{2,4}-\d{2,4}[A-Z]*$"), $"The manual's table: '{t.Model}' ({t.Tablet}) is not a model number such as PTK-640.");
            Assert.False(string.IsNullOrWhiteSpace(t.With), $"The manual's table does not say which version the {t.Tablet} was tested with.");
        }
        Assert.DoesNotContain(ByUsers, t => t.Model == Ours.Model);   // ours has its own line above the table
        Assert.Equal(ByUsers.Count, ByUsers.Select(t => t.Model).Distinct().Count());
    }

    [Fact]
    public void The_READMEs_table_is_the_manuals_list_and_our_own_tablet()
    {
        var readme = Table(Read("README.md"), "## Tablets users have tested", "README.md");
        var ours = readme.Where(r => r.With.Contains("our own tablet")).ToList();
        Assert.True(ours.Count == 1 && ours[0].Tablet == Ours.Tablet && ours[0].Model == Ours.Model,
            $"README.md's table should have one row for our own tablet, the {Ours.Tablet} ({Ours.Model}), marked 'our own tablet'.");
        var users = readme.Where(r => !r.With.Contains("our own tablet")).ToList();
        Assert.True(users.SequenceEqual(ByUsers),
            "README.md's table 'Tablets users have tested' is not the manual's list" + Where + ".\n  manual: "
            + string.Join("; ", ByUsers.Select(t => $"{t.Tablet} | {t.Model} | {t.With}")) + "\n  README: "
            + string.Join("; ", users.Select(t => $"{t.Tablet} | {t.Model} | {t.With}")));
    }

    [Fact]
    public void The_manuals_first_lines_name_every_tested_tablet()
    {
        var intro = Manual.Replace("\r", "").Split('\n').FirstOrDefault(l => l.StartsWith("Made and tested on"));
        Assert.True(intro != null, "The manual no longer has its 'Made and tested on ...' lines near the top.");
        NamesEveryModel(intro!, "The manual's 'Made and tested on ...' lines");
    }

    /// <summary>The sentence a visitor reads first: the paragraph at the top of Home that says what the app was tested with. It
    /// names every tablet in full (no model numbers there, so "Intuos5 touch Small, Medium and Large" would not do: a
    /// tablet added to the list later could hide behind a size that is already written).</summary>
    [Fact]
    public void Homes_first_lines_name_every_tested_tablet()
    {
        var leads = Regex.Matches(Home, "<p class=\"lead\">(.*?)</p>", RegexOptions.Singleline)
            .Select(m => SiteHomePageTests.Visible(m.Groups[1].Value)).Where(t => t.Contains("Tested")).ToList();
        Assert.True(leads.Count == 1, $"Home should say what the app was tested with in exactly one of its first paragraphs (found {leads.Count}).");
        var lead = leads[0];
        Assert.True(lead.Contains("Wacom " + Ours.Tablet), $"Home's first lines do not name our own test tablet, the Wacom {Ours.Tablet}.");
        foreach (var t in ByUsers)
            Assert.True(lead.Contains(t.Tablet), $"Home's first lines say what the app was tested with but do not name the {t.Tablet}.{Where}\n  They say: {lead.Trim()}");
    }

    /// <summary>Lower on Home, after the install steps: our own test system on one of the short "chips", and under them the
    /// sentence with every tablet's model number (the long list is not a chip: on a phone it became a seven-line blob).</summary>
    [Fact]
    public void Home_gives_the_model_number_of_every_tested_tablet()
    {
        var facts = Regex.Match(Home, "<ul class=\"facts\">(.*?)</ul>\\s*<p class=\"muted\">(.*?)</p>", RegexOptions.Singleline);
        Assert.True(facts.Success, "Home no longer has its <ul class=\"facts\"> list with a <p class=\"muted\"> sentence under it.");
        NamesEveryModel(SiteHomePageTests.Visible(facts.Value), "Home's list (Free and open source ... All supported Wacom tablets) with the sentence under it");
        foreach (var chip in Regex.Matches(facts.Groups[1].Value, "<li>(.*?)</li>", RegexOptions.Singleline).Select(m => SiteHomePageTests.Visible(m.Groups[1].Value).Trim()))
            Assert.True(chip.Length <= 70, $"Home's list is drawn as short rounded chips; this one is too long for a phone ({chip.Length} letters): {chip}");
        Assert.DoesNotContain("Tested only on that system and tablet", Home);   // the line that was true before users told us more
    }

    [Fact]
    public void The_websites_llms_txt_names_every_tested_model()
        => NamesEveryModel(Read("site", "src", "llms.txt"), "site/src/llms.txt");

    [Fact]
    public void The_websites_Help_page_names_every_tested_model()
        => NamesEveryModel(SiteHomePageTests.Visible(Read("site", "src", "help", "index.html")), "The website's Help page (made from the manual)");
}
