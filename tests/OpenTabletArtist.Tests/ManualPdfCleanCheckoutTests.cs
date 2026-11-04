using System.IO;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Item 11 (#20): scripts/make-manual-pdf.py must build on a clean checkout without $SITE_TOOLS, using
/// the repo's own site/ folder. Guards the files and the default it relies on.</summary>
public class ManualPdfCleanCheckoutTests
{
    static string Root()
    {
        var d = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "scripts", "make-manual-pdf.py"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    [Fact]
    public void Script_DefaultsToRepoSiteFolder()
    {
        var s = File.ReadAllText(Path.Combine(Root(), "scripts", "make-manual-pdf.py"));
        Assert.Contains("os.environ.get('SITE_TOOLS') or os.path.join(ROOT,'site')", s);
        Assert.Contains("os.environ.get('SITE_ICON') or os.path.join(ROOT,'site','icon-256.png')", s);
    }

    [Fact]
    public void RepoSiteFolder_HasWhatTheScriptNeeds()
    {
        var root = Root();
        Assert.True(File.Exists(Path.Combine(root, "site", "markdown.py")) || File.Exists(Path.Combine(root, "site", "build_site.py")));
        Assert.True(File.Exists(Path.Combine(root, "site", "icon-256.png")));
        Assert.True(File.Exists(Path.Combine(root, "OpenTabletArtist", "Assets", "manual", "manual.md")));
    }
}
