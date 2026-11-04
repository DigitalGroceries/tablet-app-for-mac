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
/// opened, is added unless the user unticks it; "Add screenshots…" adds more pictures. Both buttons make the usual
/// diagnostic zip (serial numbers and user name removed) plus the pictures and the user's text.
/// Version 23 (owner's request, 4 Nov 2026): "Send with file" (no account needed) asks where to save the zip, saves it
/// there and then says where it is and which address to email it to. It opens no email app. "Send with GitHub
/// account" saves the zip on the Desktop, shows it in Finder and opens a pre-filled GitHub issue (the same no-token
/// way as the Diagnostics page's report).
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
    [NotifyCanExecuteChangedFor(nameof(SendCommand), nameof(SendFileCommand), nameof(AddScreenshotsCommand))]
    private bool _isBusy;

    [ObservableProperty] private string _status = "";

    /// <summary>The zip that a Send button made (empty until then).</summary>
    public string ReportFilePath { get; private set; } = "";

    /// <summary>Where "Send with file" saved the zip (empty until then). When set, the sheet shows the saved message.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSaved), nameof(SavedMessage))]
    private string _savedPath = "";

    public bool IsSaved => SavedPath.Length > 0;

    /// <summary>Opens an image picker (set by the sheet). Null in tests unless supplied.</summary>
    public Func<Task<IReadOnlyList<string>>>? PickImages { get; set; }
    /// <summary>Opens the save panel with a suggested file name and returns the chosen path, or null when the user
    /// closed it (set by the sheet; tests supply their own).</summary>
    public Func<string, Task<string?>>? PickSaveFile { get; set; }
    /// <summary>Opens a URL (defaults to the browser); swappable for tests.</summary>
    public Action<string> OpenUrl { get; set; } = PlatformShell.OpenUrl;
    /// <summary>Reveals a file (defaults to Finder); swappable for tests.</summary>
    public Action<string> Reveal { get; set; } = PlatformShell.RevealInFileManager;
    /// <summary>Where "Send with GitHub account" saves the zip (defaults to the Desktop); swappable for tests.</summary>
    public Func<string> Folder { get; set; } = DiagnosticBundle.DefaultFolder;

    /// <summary>Raised when the sheet should close: true after a Send (or Close on the saved message), false after Cancel.</summary>
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
        "Send with file asks where to save one file with your text, your pictures and the app and driver logs. Then attach that file to an email to {email}. No account is needed.\n" +
        "Send with GitHub account saves the file on your Desktop and opens a new GitHub issue with your text: drag the file into it and press Create. This needs a free GitHub account.\n" +
        "Serial numbers and your user name are removed from the logs, but pictures are sent as they are: check them first.";
    public string SendGitHubLabel => Localizer.Te("report.send_github", "Send with GitHub account");
    public string SendFileLabel => Localizer.Te("report.send_file", "Send with file");
    public string SaveDialogTitle => Localizer.Te("report.save_title", "Save the report file");
    public string SavedTitle => Localizer.Te("report.saved_title", "Report saved");
    /// <summary>Where the file is and where to email it (owner's choice, 4 Nov 2026: a message only, no email app).</summary>
    public string SavedMessage => !IsSaved ? "" : Localizer.Te("report.saved_text", DefaultSavedText)
        .Replace("{path}", SavedPath).Replace("{email}", DiagnosticBundle.FeedbackEmail);
    public const string DefaultSavedText =
        "Your report file is saved here:\n{path}\n\nTo send it, write an email to {email} and attach this file. Your text and pictures are already in it.";
    public string ShowFileLabel => Localizer.Te("report.show_file", "Show in Finder");
    public string CloseLabel => Localizer.Te("app.close", "Close");
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

    /// <summary>Send with GitHub account (needs a free GitHub account): saves on the Desktop, shows the file in Finder and
    /// opens a pre-filled GitHub issue.</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task Send()
    {
        IsBusy = true;
        Status = Localizer.Te("diag.zip.working", "Creating…");
        try
        {
            var inputs = _inputs();
            var path = await MakeZipAsync(Folder(), inputs);
            ReportFilePath = path;
            AppLog.Info($"Report: made {Path.GetFileName(path)} ({Kind}, {Pictures.Count} picture(s), from {From}, GitHub).");
            OpenUrl(DiagnosticBundle.ReportIssueUrl(Path.GetFileName(path), IsRecommendation, Description ?? "", inputs.Version, inputs.Devices));
            Reveal(path);
            DeleteWindowPicture();   // it is in the zip now
            Status = Localizer.Te("diag.report.sent", "Opened the GitHub issue page. Drag the file in and press Create to send it.");
            Finished?.Invoke(true);
        }
        catch (Exception ex) { Failed(ex); }
        finally { IsBusy = false; }
    }

    /// <summary>Send with file (no account needed): asks where to save the zip, saves it there, then shows where it is
    /// and the address to email it to. Closing the save panel saves nothing and leaves the report open.</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task SendFile()
    {
        if (PickSaveFile == null) return;
        string? target;
        try { target = await PickSaveFile(DiagnosticBundle.SuggestedFileName(DateTime.Now)); }
        catch (Exception ex) { AppLog.Warn("Report: the save panel failed.", ex); Failed(ex); return; }
        if (string.IsNullOrWhiteSpace(target)) return;
        if (!target.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) target += ".zip";

        IsBusy = true;
        Status = Localizer.Te("diag.zip.working", "Creating…");
        var temp = Path.Combine(Path.GetTempPath(), "TabletApp-report-" + Guid.NewGuid().ToString("N"));
        try
        {
            var made = await MakeZipAsync(temp, _inputs());
            await Task.Run(() => File.Move(made, target, overwrite: true));   // the save panel already asked about replacing
            ReportFilePath = target;
            AppLog.Info($"Report: saved {Path.GetFileName(target)} ({Kind}, {Pictures.Count} picture(s), from {From}, file).");
            DeleteWindowPicture();   // it is in the zip now
            Status = "";
            SavedPath = target;
        }
        catch (Exception ex) { Failed(ex); }
        finally
        {
            IsBusy = false;
            try { if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true); }
            catch (Exception ex) { AppLog.Debug("Report: couldn't remove the temporary folder.", ex); }
        }
    }

    /// <summary>"Show in Finder" on the saved message.</summary>
    [RelayCommand]
    private void ShowFile() { if (IsSaved) Reveal(SavedPath); }

    /// <summary>"Close" on the saved message.</summary>
    [RelayCommand]
    private void Done() => Finished?.Invoke(true);

    string Kind => IsRecommendation ? "recommendation" : "problem";

    /// <summary>Makes the diagnostic zip in <paramref name="folder"/> with the pictures and the user's text.</summary>
    private Task<string> MakeZipAsync(string folder,
        (List<string> Devices, IReadOnlyList<string> Conflicts, string? Version) inputs)
    {
        var (devices, conflicts, version) = inputs;
        var pictures = Pictures;
        var recommendation = IsRecommendation;
        var text = Description ?? "";
        return Task.Run(() =>
        {
            var zip = DiagnosticBundle.Create(folder, devices, conflicts, version);
            DiagnosticBundle.AddScreenshots(zip, pictures);
            DiagnosticBundle.AddText(zip, "report.txt",
                (recommendation ? "Recommendation" : "Problem") + Environment.NewLine + Environment.NewLine + text);
            return zip;
        });
    }

    private void Failed(Exception ex)
    {
        AppLog.Warn("Report: couldn't make the report file.", ex);
        Status = Localizer.Te("report.failed", "Couldn't create the report: {error}").Replace("{error}", ex.Message);
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
