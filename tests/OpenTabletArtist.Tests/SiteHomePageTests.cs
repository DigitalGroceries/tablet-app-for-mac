using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Digital Groceries' rule (26 Nov 2026): the website's Home says what is new in the current version only; what older
/// versions had is on a page of its own. Home carried the notes of the version before under its "What is new" (a whole
/// "Version 29" list under Version 30's), because each release moved the old notes down instead of out, and a picture
/// line still said "New in Version 29". Home now links to Releases, the page that has every version's notes. These tests
/// fail if the old notes come back, or if Releases stops having them.</summary>
public class SiteHomePageTests
{
    static string Root()
    {
        var d = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "site", "build_site.py"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    static string Home => File.ReadAllText(Path.Combine(Root(), "site", "src", "index.html"));
    static string Releases => File.ReadAllText(Path.Combine(Root(), "site", "src", "releases", "index.html"));

    /// <summary>What a visitor reads: the page without its tags. (The pictures' descriptions for screen readers are inside
    /// tags; they say which version a picture was taken in, which is not news about that version.)</summary>
    internal static string Visible(string html)
    {
        html = Regex.Replace(html, @"<(script|style)\b.*?</\1>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        return Regex.Replace(System.Net.WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " ")), @"\s+", " ");
    }

    /// <summary>Home from its "What is new" heading to the next main heading.</summary>
    static string WhatIsNew()
    {
        var home = Home;
        var start = Regex.Match(home, @"<h2[^>]*>What is new in Version \{\{V\}\}</h2>");
        Assert.True(start.Success, "Home has no 'What is new in Version {{V}}' heading.");
        int end = home.IndexOf("<h2", start.Index + start.Length, System.StringComparison.Ordinal);
        Assert.True(end > start.Index, "Home's 'What is new' section has no end.");
        return home[start.Index..end];
    }

    [Fact]
    public void Home_says_what_is_new_in_the_current_version_only()
    {
        var section = WhatIsNew();
        var headings = Regex.Matches(section, @"<h[2-6][^>]*>(.*?)</h[2-6]>", RegexOptions.Singleline).Select(m => m.Groups[1].Value).ToList();
        Assert.True(headings.Count == 1, "Home's 'What is new' has headings for more than the current version: " + string.Join(" | ", headings.Skip(1)));
        Assert.DoesNotContain("{{P1}}", section);
        Assert.DoesNotContain("{{P2}}", section);
        var named = Regex.Match(Visible(section), @".{0,70}Version \d+.{0,50}");
        Assert.False(named.Success, "Home's 'What is new' names a version by number (\"…" + named.Value.Trim() + "…\"); it is about the current one ({{V}}) only.");
    }

    /// <summary>The only versions Home names by number are in the install steps (what someone updating from a very old
    /// version does once). Anything else about an older version, a list or a picture line such as "New in Version 29: ...",
    /// belongs on Releases. The two versions people can go back to are written {{P1}} and {{P2}}: links, not news.</summary>
    [Fact]
    public void Home_says_nothing_else_about_older_versions()
    {
        var text = Regex.Replace(Visible(Home), @"From Version \d+ on\b|Updating from Version \d+ or older", "");
        var named = Regex.Match(text, @".{0,70}Version \d+.{0,50}");
        Assert.False(named.Success, "Home names a version by number: \"…" + named.Value.Trim()
            + "…\". Home is about the current version ({{V}}); what older versions had is on the Releases page.");
    }

    [Fact]
    public void Home_sends_people_to_Releases_for_earlier_versions()
        => Assert.Contains("earlier versions is on the <a href=\"/tablet-app-for-mac/releases/\">Releases</a> page", WhatIsNew());

    [Fact]
    public void Releases_has_the_notes_of_the_two_versions_people_can_go_back_to()
    {
        // The versions Home lets people go back to (site.json "previous") each have their own section with notes on Releases.
        var json = File.ReadAllText(Path.Combine(Root(), "site", "site.json"));
        var previous = Regex.Match(json, "\"previous\"\\s*:\\s*\\[\\s*(\\d+)\\s*,\\s*(\\d+)\\s*\\]");
        Assert.True(previous.Success, "site.json has no \"previous\": [N, M].");
        var releases = Releases;
        foreach (var v in new[] { previous.Groups[1].Value, previous.Groups[2].Value })
        {
            var head = Regex.Match(releases, $@"<h2[^>]*>\s*(?:<a[^>]*>)?\s*Version {v}\b");   // <h2 id=...><a href="#v29">Version 29</a></h2>
            Assert.True(head.Success, $"Releases has no section for Version {v}.");
            int end = releases.IndexOf("<h2", head.Index + 4, System.StringComparison.Ordinal);
            var body = releases[head.Index..(end < 0 ? releases.Length : end)];
            Assert.True(Regex.Matches(body, "<li>").Count >= 3, $"Releases says almost nothing about Version {v}.");
        }
    }
}
