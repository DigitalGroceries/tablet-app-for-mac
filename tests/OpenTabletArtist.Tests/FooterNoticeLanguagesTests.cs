using System.IO;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>#117 (Version 29, from Devin's #84 review): the notices in the settings footer were English in every
/// language, and the one after a restart did not say that the restored changes are not saved yet. They are now in
/// all 11 languages, and the restore notice names the Save and Revert buttons with the buttons' own words.</summary>
public class FooterNoticeLanguagesTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private static JObject Lang(string code) =>
        JObject.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "OpenTabletArtist", "Assets", "i18n", code + ".json")));

    public static readonly TheoryData<string> Languages = new()
    {
        "bg", "de", "en", "es", "fr", "it", "ja", "pt-BR", "ru", "sv", "zh-CN",
    };

    private static readonly string[] Keys =
    {
        "footer.restored_unsaved", "footer.driver_refreshed",
        "footer.driver_disconnected_unsaved", "footer.driver_reconnected_replaced",
    };

    [Theory]
    [MemberData(nameof(Languages))]
    public void Every_footer_notice_is_translated(string code)
    {
        var lang = Lang(code); var en = Lang("en");
        foreach (var key in Keys)
        {
            var text = (string?)lang[key];
            Assert.False(string.IsNullOrWhiteSpace(text), $"{code}: {key} is missing");
            if (code != "en") Assert.NotEqual((string?)en[key], text);
        }
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void The_restore_notice_names_the_Save_and_Revert_buttons_as_they_are_written(string code)
    {
        var lang = Lang(code);
        var notice = (string)lang["footer.restored_unsaved"]!;
        Assert.Contains((string)lang["save.button"]!, notice);
        Assert.Contains((string)lang["save.revert"]!, notice);
    }

    [Fact]
    public void Without_the_language_tables_the_English_text_is_used()
    {
        // Unit tests run without the app's assets: Localizer.Te falls back to the English text, never the raw key.
        Assert.StartsWith("Restored unsaved changes from when the app last closed.", UnsavedSnapshot.RestoredNotice);
        Assert.Contains("Revert to saved", UnsavedSnapshot.RestoredNotice);
        Assert.StartsWith("The driver disconnected.", AppSession.DisconnectedWithUnsavedChangesMessage);
        Assert.StartsWith("Reconnected to the driver.", AppSession.ReconnectedReplacedUnsavedChangesMessage);
    }
}
