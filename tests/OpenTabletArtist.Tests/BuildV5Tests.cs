using System.IO;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Version 5: no duplicate bottom About button; no personal names in the shipped manual.</summary>
public class BuildV5Tests
{
    static string Root()
    {
        var d = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "OpenTabletArtist.slnx"))) d = d.Parent;
        return d!.FullName;
    }

    [Fact]
    public void BottomBar_HasNoAboutButton()
    {
        var x = File.ReadAllText(Path.Combine(Root(), "OpenTabletArtist", "Views", "TabletDetailView.axaml"));
        Assert.DoesNotContain("OnBottomAbout", x);
        Assert.DoesNotContain("Content=\"About\" Click", x);
    }

    [Fact]
    public void Manual_HasNoPersonalName()
    {
        var m = File.ReadAllText(Path.Combine(Root(), "OpenTabletArtist", "Assets", "manual", "manual.md"));
        Assert.DoesNotContain("Z" + "ak", m);
        Assert.Contains("App version 16", m);   // Version 16: the cover line follows the app version
        Assert.Contains("macOS 27.0.1", m);
    }
}
