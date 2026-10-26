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
        Services.Localizer.EnsureLoaded();
        Services.Localizer.LanguageChanged += () =>
        {
            OnPropertyChanged(nameof(GetHelpLabel));
            OnPropertyChanged(nameof(SupportedTabletsLabel));
            OnPropertyChanged(nameof(SourceCodeLabel));
            OnPropertyChanged(nameof(ReleasesLabel));
            OnPropertyChanged(nameof(FeedbackLabel));
            OnPropertyChanged(nameof(UserManualLabel));
            OnPropertyChanged(nameof(CreditsLabel));
            OnPropertyChanged(nameof(CreditsBlurb));
            OnPropertyChanged(nameof(FeedbackNote));
            OnPropertyChanged(nameof(AboutBlurb));
            OnPropertyChanged(nameof(AppVersion));
            OnPropertyChanged(nameof(AppVersionSentence));
            OnPropertyChanged(nameof(AboutLineText));
            OnPropertyChanged(nameof(CheckUpdatesLabel));
            OnPropertyChanged(nameof(UpdateNowLabel));
            OnPropertyChanged(nameof(UpdateAutomaticallyLabel));
        };
        // Keep Home's update UI in sync with the pen-menu tray (shared AppUpdateState).
        Services.AppUpdateState.Shared.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is null or nameof(Services.AppUpdateState.Status))
                UpdateStatus = Services.AppUpdateState.Shared.Status;
            if (e.PropertyName is null or nameof(Services.AppUpdateState.UpdateReady))
                UpdateReady = Services.AppUpdateState.Shared.UpdateReady;
        };
        UpdateStatus = Services.AppUpdateState.Shared.Status;
        UpdateReady = Services.AppUpdateState.Shared.UpdateReady;
    }

    public string GetHelpLabel => Services.Localizer.T("home.get_help");
    public string SupportedTabletsLabel => Services.Localizer.T("home.supported_tablets");
    public string SourceCodeLabel => Services.Localizer.T("home.source_code");
    public string ReleasesLabel => Services.Localizer.T("home.releases");
    public string FeedbackLabel => Services.Localizer.T("home.feedback");
    public string UserManualLabel => Services.Localizer.T("home.user_manual");
    public string CreditsLabel => Services.Localizer.T("home.credits");
    public string CreditsBlurb => Services.Localizer.T("home.licence_blurb");
    public string FeedbackNote => Services.Localizer.T("home.feedback_note");
    public string AboutBlurb => Services.Localizer.T("home.about_blurb");
    public string AboutLineText => AboutBlurb.TrimEnd() + " " + AppVersionSentence;
    public string CheckUpdatesLabel => Services.Localizer.T("update.check_now");
    public string UpdateNowLabel => Services.Localizer.T("update.now");
    public string UpdateAutomaticallyLabel => Services.Localizer.T("update.automatic");

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

    /// <summary>Bare version token from the assembly (release workflow stamps the tag at build).</summary>
    readonly string _versionNumber = AppVersionInfo.Number(
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>Localised "Version N" for Home's About line.</summary>
    public string AppVersion => Services.Localizer.Tf("home.version", ("version", _versionNumber));

    /// <summary>"Version N." — a closing sentence for the About paragraph rather than a line or
    /// a heading suffix of its own. The version is reference matter: worth having to hand when reporting a
    /// problem, not worth its own row in the column.</summary>
    public string AppVersionSentence => AppVersion + ".";

    // Version 7: updates (state shared with the pen-menu tray via AppUpdateState).
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string _updateStatus = "";
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool _updateReady;
    public bool UpdateAutomatically { get => Services.Updater.Automatic; set => Services.Updater.Automatic = value; }

    [RelayCommand]
    private async Task CheckForUpdates() => await Services.AppUpdateState.Shared.CheckAsync();

    [RelayCommand]
    private async Task UpdateNow() => await Services.AppUpdateState.Shared.InstallPendingAsync();

    /// <summary>Called on launch: the weekly check. With "Update automatically" on, downloads then asks
    /// OK to install; otherwise asks before downloading.</summary>
    public async Task MonthlyCheckAsync()
    {
        if (!Services.Updater.IsDueNow()) return;
        await CheckForUpdates();
        var pending = Services.AppUpdateState.Shared.Pending;
        if (pending == null) return;
        if (UpdateAutomatically)
        {
            var err = await Services.AppUpdateState.Shared.DownloadPendingAsync();
            if (err != null) return;
            if (await Views.UpdatePromptWindow.AskReadyAsync(pending.Version))
                await Services.AppUpdateState.Shared.ApplyPendingAsync();
            return;
        }
        if (await Views.UpdatePromptWindow.AskAsync(pending.Version)) await UpdateNow();
    }

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
