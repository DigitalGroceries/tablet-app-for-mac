using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>The owner's rule (26 Nov 2026): the website's Language menu always lists the languages the app has. Version 29
/// gave the app Italian, Swedish and Brazilian Portuguese and Version 30 Hindi and Swahili, and each time the website's menu
/// was left with its first eight: nothing made anyone look. Now this suite fails while the website's menu (every page of
/// site/src, and the list its script builds the menu from) is not exactly <see cref="Localizer.Languages"/>: the same
/// languages, in the same order, under the same names. To put it right: <c>python3 site/sync_languages.py</c>.</summary>
public class SiteLanguagesTests
{
    const string Fix = " Run: python3 site/sync_languages.py";

    static string Root()
    {
        var d = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "site", "sync_languages.py"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    /// <summary>The app's languages as a person chooses them: without "Match system", which is not a language.</summary>
    static (string Code, string Name)[] App =>
        Localizer.Languages.Where(l => l.Code != Localizer.SystemCode).Select(l => (l.Code, l.NativeName)).ToArray();

    /// <summary>The code Google's page translator knows a language by (site/sync_languages.py, GOOGLE): Brazilian Portuguese
    /// is "pt" there; every other language of the app has the app's code.</summary>
    static string Google(string code) => code == "pt-BR" ? "pt" : code;

    static string[] Pages(string root) => Directory
        .GetFiles(Path.Combine(root, "site", "src"), "*.html", SearchOption.AllDirectories)
        .Where(f => File.ReadAllText(f).Contains("id=\"langmenu\"")).OrderBy(f => f).ToArray();

    [Fact]
    public void The_app_has_a_translation_for_every_language_it_offers()
    {
        var root = Root();
        foreach (var (code, name) in App)
            Assert.True(File.Exists(Path.Combine(root, "OpenTabletArtist", "Assets", "i18n", code + ".json")),
                $"The app offers {name} ({code}) but has no Assets/i18n/{code}.json");
    }

    [Fact]
    public void Every_page_of_the_website_lists_the_apps_languages_in_the_apps_order()
    {
        var root = Root();
        var pages = Pages(root);
        Assert.True(pages.Length >= 7, $"only {pages.Length} website pages with a Language menu were found under site/src");
        foreach (var page in pages)
        {
            var rel = Path.GetRelativePath(root, page);
            var menu = Regex.Match(File.ReadAllText(page), "id=\"langmenu\"><ul>(.*?)</ul>", RegexOptions.Singleline);
            Assert.True(menu.Success, $"{rel}: no Language menu found.");
            var items = Regex.Matches(menu.Groups[1].Value, "<a href=\"([^\"]+)\" lang=\"([^\"]+)\"[^>]*>([^<]+)</a>")
                .Select(m => (Href: m.Groups[1].Value, Code: m.Groups[2].Value, Name: m.Groups[3].Value)).ToArray();
            Assert.True(App.SequenceEqual(items.Select(i => (i.Code, i.Name))),
                $"{rel}: the website's Language menu is not the app's languages.\n  app:     {string.Join(", ", App.Select(a => a.Name))}\n  website: {string.Join(", ", items.Select(i => i.Name))}\n{Fix}");
            foreach (var i in items.Where(i => i.Code != "en"))
            {
                Assert.True(i.Href.StartsWith("https://digitalgroceries-github-io.translate.goog/tablet-app-for-mac/"),
                    $"{rel}: {i.Name} does not open Google's translation of the website: {i.Href}");
                Assert.True(i.Href.Contains($"_x_tr_tl={Google(i.Code)}&amp;") && i.Href.EndsWith($"_x_tr_hl={i.Code}"),
                    $"{rel}: {i.Name} asks Google for another language than {Google(i.Code)}: {i.Href}.{Fix}");
            }
            Assert.StartsWith("https://digitalgroceries.github.io/tablet-app-for-mac/", items[0].Href);
        }
    }

    [Fact]
    public void The_menu_the_websites_script_builds_lists_the_apps_languages_too()
    {
        var root = Root();
        var js = File.ReadAllText(Path.Combine(root, "site", "src", "assets", "lang.js"));
        var list = Regex.Match(js, @"var L = \[(.*?)\];", RegexOptions.Singleline);
        Assert.True(list.Success, "site/src/assets/lang.js: no 'var L = [...]' list.");
        var items = Regex.Matches(list.Groups[1].Value, @"\['([^']+)', '([^']+)'(?:, '([^']+)')?\]")
            .Select(m => (Code: m.Groups[1].Value, Name: m.Groups[2].Value, Tl: m.Groups[3].Success ? m.Groups[3].Value : m.Groups[1].Value)).ToArray();
        Assert.True(App.SequenceEqual(items.Select(i => (i.Code, i.Name))),
            $"site/src/assets/lang.js: its list is not the app's languages.\n  app:     {string.Join(", ", App.Select(a => a.Name))}\n  website: {string.Join(", ", items.Select(i => i.Name))}\n{Fix}");
        foreach (var i in items)
            Assert.True(i.Tl == Google(i.Code), $"site/src/assets/lang.js: {i.Name} asks Google for '{i.Tl}', not '{Google(i.Code)}'.{Fix}");
        // and the script uses that code when it builds the link
        Assert.Contains("(l[2] || l[0])", js);
    }
}
