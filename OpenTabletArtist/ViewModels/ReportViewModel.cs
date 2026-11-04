using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// Version 22 BETA 3 (owner's request, 2 Nov 2026): "Report a problem or send a recommendation…", reachable from the app
/// window (Home's Feedback link and the tablet page's bottom bar), the pen icon menu and the app menu (top left).
/// All three open the same sheet in the main window. A picture of the app window, taken just before the sheet
/// opened, is added unless the user unticks it; "Add screenshots…" adds more pictures. Both Send buttons make the
/// usual diagnostic zip (serial numbers and user name removed) plus the pictures and the user's text and show it in
/// Finder. "Send by email" (no account needed) opens a pre-filled email to the project's feedback address; "Send with
/// GitHub account" opens a pre-filled GitHub issue (the same no-token way as the Diagnostics page's report).
/// </summary>
public partial class ReportViewModel : ObservableObject
{
    /// <summary>At most 10 pictures go into the zip (DiagnosticBundle.AddScreenshots).</summary>
    public const int MaxPictures = 10;

    private readonly Func<(List<string> Devices, IReadOnlyList<string> Conflicts, string? Version)> _inputs;

    /// <param name="inputs">Tablets, other drivers found and driver version (DiagnosticsViewModel.ReportInputs).</param>
    /// <param name="windowPicture">The picture of the app window, or null when it couldn't be taken.</param>
    /// <param name="from">Where the report was opened from, for the log ("Home", "pen menu", "app menu", …).</param>
    public ReportViewModel(Func<(List<string> Devices, IReadOnlyList<string> Conflicts, string? Version)> inputs,
        string? windowPicture, string from)
    {
        _inputs = inputs;
        WindowPicturePath = windowPicture != null && File.Exists(windowPicture) ? windowPicture : null;
        IncludeWindowPicture = WindowPicturePath != null;
        From = from;
        Screenshots.CollectionChanged += (_, _) => OnPropertyChanged(nameof(PicturesText));
    }

    public string From { get; }

    /// <summary>False = a problem (the default), true = a recommendation.</summary>
    [ObservableProperty] private bool _isRecommendation;

    [ObservableProperty] private string _description = "";

    public string? WindowPicturePath { get; }
    public bool HasWindowPicture => WindowPicturePath != null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PicturesText))]
    private bool _includeWindowPicture;

    /// <summary>Pictures the user added with "Add screenshots…".</summary>
    public ObservableCollection<string> Screenshots { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand), nameof(SendEmailCommand), nameof(AddScreenshotsCommand))]
    private bool _isBusy;

    [ObservableProperty] private string _status = "";

    /// <summary>The zip that Send made (empty until then).</summary>
    public string ReportFilePath { get; private set; } = "";

    /// <summary>Opens an image picker (set by the sheet). Null in tests unless supplied.</summary>
    public Func<Task<IReadOnlyList<string>>>? PickImages { get; set; }
    /// <summary>Opens a URL (defaults to the browser); swappable for tests.</summary>
    public Action<string> OpenUrl { get; set; } = PlatformShell.OpenUrl;
    /// <summary>Reveals a file (defaults to Finder); swappable for tests.</summary>
    public Action<string> Reveal { get; set; } = PlatformShell.RevealInFileManager;
    /// <summary>Where the zip is saved (defaults to the Desktop); swappable for tests.</summary>
    public Func<string> Folder { get; set; } = DiagnosticBundle.DefaultFolder;

    /// <summary>Raised when the sheet should close: true after Send, false after Cancel.</summary>
    public event Action<bool>? Finished;

    // ---- Texts (English fallbacks; translations in Assets/i18n).
    public static string MenuLabel => Localizer.Te("report.menu", "Report a problem or send a recommendation…");
    public string Title => Localizer.Te("report.title", "Report a problem or send a recommendation");
    public string ProblemLabel => Localizer.Te("report.kind.problem", "A problem");
    public string RecommendationLabel => Localizer.Te("report.kind.recommendation", "A recommendation");
    public string DescriptionLabel => Localizer.Te("report.text_label", "What happened, or what is your idea?");
    public string DescriptionHint => Localizer.Te("report.text_hint", "Optional");
    public string WindowPictureLabel => Localizer.Te("report.window_picture", "Add this picture of the app window");
    public string AddScreenshotsLabel => Localizer.Te("diag.report.screenshots", "Add screenshots…");
    public string Note => Localizer.Te("report.note", DefaultNote).Replace("{email}", DiagnosticBundle.FeedbackEmail);
    public const string DefaultNote =
        "Both Send buttons save one file on your Desktop with your pictures and the app and driver logs, and show it in Finder. Serial numbers and your user name are removed from the logs, but pictures are sent as they are: check them first.\n" +
        "Send by email opens your email app with your text, addressed to {email}: attach the file and send it. No account is needed.\n" +
        "Send with GitHub account opens a new GitHub issue with your text: drag the file into it and press Create. This needs a free GitHub account.";
    public string SendGitHubLabel => Localizer.Te("report.send_github", "Send with GitHub account");
    public string SendEmailLabel => Localizer.Te("report.send_email", "Send by email");
    public string CancelLabel => Localizer.Te("diag.report.cancel", "Cancel");

    /// <summary>The pictures that Send puts in the zip, in order: the window picture first.</summary>
    public IReadOnlyList<string> Pictures =>
        (IncludeWindowPicture && WindowPicturePath != null ? new[] { WindowPicturePath } : Array.Empty<string>())
        .Concat(Screenshots).Take(MaxPictures).ToList();

    /// <summary>"Pictures added: n" (blank when none).</summary>
    public string PicturesText => Pictures.Count == 0 ? ""
        : Localizer.Te("report.pictures", "Pictures added: {n}").Replace("{n}", Pictures.Count.ToString());

    private bool CanEdit() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task AddScreenshots()
    {
        if (PickImages == null) return;
        IReadOnlyList<string> picked;
        try { picked = await PickImages(); }
        catch (Exception ex) { AppLog.Warn("Report: the picture picker failed.", ex); return; }
        foreach (var p in picked)
            if (!Screenshots.Contains(p) && Pictures.Count < MaxPictures) Screenshots.Add(p);
    }

    /// <summary>Send with GitHub account (needs a free GitHub account).</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task Send() => SendAsync(email: false);

    /// <summary>Send by email (no account needed).</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task SendEmail() => SendAsync(email: true);

    private async Task SendAsync(bool email)
    {
        IsBusy = true;
        Status = Localizer.Te("diag.zip.working", "Creating…");
        try
        {
            var (devices, conflicts, version) = _inputs();
            var pictures = Pictures;
            var recommendation = IsRecommendation;
            var text = Description ?? "";
            var folder = Folder();
            var path = await Task.Run(() =>
            {
                var zip = DiagnosticBundle.Create(folder, devices, conflicts, version);
                DiagnosticBundle.AddScreenshots(zip, pictures);
                DiagnosticBundle.AddText(zip, "report.txt",
                    (recommendation ? "Recommendation" : "Problem") + Environment.NewLine + Environment.NewLine + text);
                return zip;
            });
            ReportFilePath = path;
            AppLog.Info($"Report: made {Path.GetFileName(path)} ({(recommendation ? "recommendation" : "problem")}, {pictures.Count} picture(s), from {From}, {(email ? "email" : "GitHub")}).");
            var name = Path.GetFileName(path);
            OpenUrl(email
                ? DiagnosticBundle.ReportMailUrl(name, recommendation, text, version, devices)
                : DiagnosticBundle.ReportIssueUrl(name, recommendation, text, version, devices));
            Reveal(path);
            DeleteWindowPicture();   // it is in the zip now
            Status = email
                ? Localizer.Te("report.mail_opened", "Opened your email app. Attach the file shown in Finder and send it.")
                : Localizer.Te("diag.report.sent", "Opened the GitHub issue page. Drag the file in and press Create to send it.");
            Finished?.Invoke(true);
        }
        catch (Exception ex)
        {
            AppLog.Warn("Report: couldn't make the report file.", ex);
            Status = Localizer.Te("report.failed", "Couldn't create the report: {error}").Replace("{error}", ex.Message);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void Cancel()
    {
        DeleteWindowPicture();
        Finished?.Invoke(false);
    }

    /// <summary>The window picture is a temporary file: once it is in the zip, or the report is cancelled, it goes.
    /// Only a file in the temporary folder is ever deleted (never a picture the user added).</summary>
    private void DeleteWindowPicture()
    {
        try
        {
            if (WindowPicturePath is { } p && File.Exists(p)
                && Path.GetFullPath(p).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.Ordinal))
                File.Delete(p);
        }
        catch (Exception ex) { AppLog.Debug("Report: couldn't delete the temporary window picture.", ex); }
    }
}
