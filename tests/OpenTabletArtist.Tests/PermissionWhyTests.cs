using System.IO;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>#26 (Wacom comparison point 5): setup says in one plain line why macOS asks for each permission,
/// in every language; the pane name ({pane}) is filled in by the app.</summary>
public class PermissionWhyTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    [Theory]
    [InlineData("en")] [InlineData("bg")] [InlineData("de")] [InlineData("es")]
    [InlineData("fr")] [InlineData("ja")] [InlineData("ru")] [InlineData("zh-CN")]
    public void EachLanguage_HasBothWhyLines(string code)
    {
        var lang = JObject.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "OpenTabletArtist", "Assets", "i18n", code + ".json")));
        var pen = (string?)lang["setup.pen.why"];
        var pointer = (string?)lang["setup.pointer.why"];
        Assert.False(string.IsNullOrWhiteSpace(pen));
        Assert.Contains("Input Monitoring", pen);          // macOS names stay English
        Assert.Contains("{pane}", pointer);
    }

    [Fact]
    public void Setup_ShowsThem_UnderEachPermissionStep()
    {
        var setup = File.ReadAllText(Path.Combine(RepoRoot(), "OpenTabletArtist", "Views", "SetupWindow.cs"));
        Assert.Contains("Why(Localizer.Te(\"setup.pen.why\"", setup);
        Assert.Contains("Why(Localizer.Tf(\"setup.pointer.why\", (\"pane\", MacAccessibility.PointerPaneName()))", setup);
    }
}
