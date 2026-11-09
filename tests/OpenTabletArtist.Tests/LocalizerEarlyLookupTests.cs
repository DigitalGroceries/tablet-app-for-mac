using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>#74 CI: since PR #75 the pen and ExpressKey labels are translated, so a domain label can be
/// read before Avalonia starts (as in this project, which never starts it). Such an early lookup must not
/// mark the string tables loaded; otherwise every later lookup in the app returns raw keys such as
/// "update.downloading" (32 UI tests failed that way on Ubuntu).</summary>
public class LocalizerEarlyLookupTests
{
    [Fact]
    public void ALookupBeforeAvaloniaStarts_DoesNotMarkTheTablesLoaded()
    {
        Localizer.ResetForTests();

        Assert.Equal("update.downloading", Localizer.T("update.downloading"));
        Assert.False(Localizer.IsLoaded);
    }

    [Fact]
    public void DomainLabels_FallBackToEnglish_BeforeAvaloniaStarts()
    {
        Localizer.ResetForTests();

        Assert.Equal("Double Click", Localizer.Te("binding.mouse.double_click", "Double Click"));
        Assert.Contains(AuxKeyBinding.MouseButtonOptions, o => o.Display == "Double Click");
        Assert.False(Localizer.IsLoaded);
    }
}
