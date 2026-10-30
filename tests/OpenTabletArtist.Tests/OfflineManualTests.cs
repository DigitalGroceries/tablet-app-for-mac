using System.Linq;
using OpenTabletArtist.Views;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Version 19 BETA 2: the manual opens offline, in the user's language when a translation exists.</summary>
public class OfflineManualTests
{
    [Theory]
    [InlineData("de", new[] { "manual.de.md", "manual.md" })]
    [InlineData("en", new[] { "manual.md" })]
    [InlineData(null, new[] { "manual.md" })]
    public void TriesTheTranslationFirst_ThenEnglish(string? lang, string[] expected)
        => Assert.Equal(expected, ManualWindow.ManualFileNames(lang).ToArray());
}
