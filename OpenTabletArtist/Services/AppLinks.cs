namespace OpenTabletArtist.Services;

/// <summary>R18: the one place for the About links' targets. Set to the planned Pages site; a null link's button is disabled. Previous targets (upstream OpenTabletArtist, not ours):
/// Source code https://github.com/TheSevenPens/OpenTabletArtist, Releases …/releases,
/// Get help https://thesevenpens.github.io/OpenTabletArtist/HELP/, User manual https://thesevenpens.github.io/OpenTabletArtist/.</summary>
public static class AppLinks
{
    // Planned GitHub Pages site (DigitalGroceries/tablet-app-for-mac). Not live until the user publishes the repo.
    private const string Site = "https://digitalgroceries.github.io/tablet-app-for-mac/";
    public const string? Releases = Site + "releases/";
    /// <summary>Get help (Zak, 1 Nov 2026): GitHub's "new issue" chooser, where people report a bug, suggest a feature,
    /// ask a question (Discussions) or send feedback without a GitHub account. It used to open the manual, like User manual.</summary>
    public const string? GetHelp = "https://github.com/DigitalGroceries/tablet-app-for-mac/issues/new/choose";
    /// <summary>The online copy of the user manual (the manual window's "Open online version").</summary>
    public const string? Manual = Site + "help/";
    public const string? SourceCode = Site + "source/";
    public const string? CreditsAndLicences = Site + "credits/";
    /// <summary>Report a problem or suggest a feature. Next version (Zak, 27 Oct 2026): the website's Feedback
    /// section, which offers GitHub and, for people without a GitHub account, email with screenshots.</summary>
    public const string? Feedback = Site + "#feedback";
}
