using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// View model for the About page. First step of the page-view-model split (#14 phase 2):
/// the About page owns no shared state, so it moves cleanly out of <see cref="MainViewModel"/>.
/// The view is re-pointed to this VM via a DataContext binding, so navigation is unchanged.
/// </summary>
public partial class AboutViewModel : ObservableObject
{
    private readonly IDialogService? _dialogs;
    private readonly Func<string?>? _detectedTabletName;

    /// <param name="dialogs">Used by the RESOURCES "Supported tablets" link to open the in-app catalog.
    /// Optional so the parameterless case (e.g. tests) keeps working.</param>
    /// <param name="detectedTabletName">Supplies the connected tablet to highlight in that dialog.</param>
    public AboutViewModel(IDialogService? dialogs = null, Func<string?>? detectedTabletName = null)
    {
        _dialogs = dialogs;
        _detectedTabletName = detectedTabletName;
    }

    // R18: targets live in Services.AppLinks (TODO placeholders until the user's pages exist).
    public string? RepoUrl => AppLinks.SourceCode;
    public string? ReleasesUrl => AppLinks.Releases;
    public string? CreditsUrl => AppLinks.CreditsAndLicences;
    public string? HelpUrl => AppLinks.GetHelp;
    public bool HasRepoUrl => RepoUrl != null;
    public bool HasReleasesUrl => ReleasesUrl != null;
    public string? FeedbackUrl => AppLinks.Feedback;
    public bool HasFeedbackUrl => FeedbackUrl != null;
    public bool HasCreditsUrl => CreditsUrl != null;
    public string HelpTip => HelpUrl ?? "Open the user manual";

    /// <summary>Raised when Get help (or User manual) should open the in-app manual.</summary>
    public event System.Action? ManualRequested;

    [RelayCommand]
    private void OpenHelp()
    {
        if (HelpUrl != null) Services.PlatformShell.OpenUrl(HelpUrl);
        else ManualRequested?.Invoke();
    }

    /// <summary>App version, read from the assembly so it never drifts (the release workflow stamps
    /// the tag version at build). Moved here from the sidebar footer so version info lives on About.</summary>
    public string AppVersion { get; } = AppVersionInfo.Format(
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>"Version v0.6.0 BETA." — a closing sentence for the About paragraph rather than a line or
    /// a heading suffix of its own. The version is reference matter: worth having to hand when reporting a
    /// problem, not worth its own row in the column.</summary>
    public string AppVersionSentence => $"{AppVersion}.";

    /// <summary>Opens a URL in the user's default browser.</summary>
    /// <remarks>
    /// The four links this serves are constants on this class, so the helper's https-only rule changes
    /// nothing here today (#889). It is the default being the safe one rather than each caller being
    /// trusted to stay that way.
    /// </remarks>
    [RelayCommand]
    private void OpenUrl(string? url) { if (url != null) Services.PlatformShell.OpenUrl(url); }

    /// <summary>Show OTD's built-in supported-tablets catalog in an in-app dialog (#155), highlighting the
    /// connected tablet. The RESOURCES-card entry point — replaces the former standalone Home card.</summary>
    [RelayCommand]
    private async Task OpenSupportedTablets()
    {
        if (_dialogs == null) return;
        await _dialogs.ShowSupportedTabletsAsync(_detectedTabletName?.Invoke());
    }
}
