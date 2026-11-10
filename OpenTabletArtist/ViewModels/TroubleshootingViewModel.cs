using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletArtist.Services;
using OtdInterop;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// Settings › Troubleshooting (#25 / Wacom comparison point 5): one box for the three actions that used
/// to live apart — restart the driver (tray), create a diagnostic zip (Advanced / Backup), and reset
/// Mac permissions (Home). Existing entry points stay; this is the place that gathers them.
/// </summary>
public partial class TroubleshootingViewModel : ObservableObject
{
    private readonly AppSession _session;
    private readonly DiagnosticsViewModel _diagnostics;
    private readonly Func<IReadOnlyList<string>> _conflictLabels;

    public TroubleshootingViewModel(AppSession session, DiagnosticsViewModel diagnostics,
        Func<IReadOnlyList<string>>? conflictLabels = null)
    {
        _session = session;
        _diagnostics = diagnostics;
        _conflictLabels = conflictLabels ?? (() => Array.Empty<string>());
        Localizer.LanguageChanged += RefreshLabels;
    }

    public string Title => Localizer.Te("settings.troubleshoot.title", "Troubleshooting");
    public string Intro => Localizer.Te("settings.troubleshoot.intro",
        "Quick fixes when the tablet stops responding, a setting will not stick, or you need to send a report.");

    public string RestartTitle => Localizer.Te("settings.troubleshoot.restart_title", "Restart driver");
    public string RestartBody => Localizer.Te("settings.troubleshoot.restart_body",
        "Stops the tablet driver and starts it again. Use this when the pen stops moving or a tablet is not detected.");
    public string RestartButton => Localizer.Te("settings.troubleshoot.restart_button", "Restart driver");

    public string DiagTitle => Localizer.Te("settings.troubleshoot.diag_title", "Diagnostic report");
    public string DiagBody => Localizer.Te("settings.troubleshoot.diag_body",
        "Saves a zip on your Desktop with logs, settings and tablet info you can attach when asking for help.");
    public string DiagButton => Localizer.Te("settings.troubleshoot.diag_button", "Create diagnostic file");
    public string DiagReveal => Localizer.Te("backup.auto.folder", "Show in Finder");

    public string PermTitle => Localizer.Te("settings.troubleshoot.perm_title", "Reset permissions");
    public string PermBody => Localizer.Te("settings.troubleshoot.perm_body",
        "Clears this app’s Input Monitoring and Accessibility entries so macOS will ask again. macOS only.");
    public string PermButton => Localizer.Te("settings.troubleshoot.perm_button", "Reset permissions");

    public bool ShowPermissionReset => OperatingSystem.IsMacOS();

    public IAsyncRelayCommand RestartDaemonCommand => _session.RestartDaemonCommand;

    [ObservableProperty] private string _diagnosticStatus = "";
    [ObservableProperty] private string _diagnosticFilePath = "";
    public bool HasDiagnosticFile => !string.IsNullOrEmpty(DiagnosticFilePath);
    partial void OnDiagnosticFilePathChanged(string value) => OnPropertyChanged(nameof(HasDiagnosticFile));

    [ObservableProperty] private string _permissionsResetStatus = "";

    [RelayCommand]
    private async Task CreateDiagnosticFile()
    {
        DiagnosticStatus = Localizer.Te("diag.zip.working", "Creating…");
        try
        {
            var devices = _session.DetectedTablets.Select(t => t.Name).ToList();
            var conflicts = _conflictLabels();
            var version = _session.DaemonVersion;
            var path = await Task.Run(() =>
                DiagnosticBundle.Create(DiagnosticBundle.DefaultFolder(), devices, conflicts, version));
            DiagnosticFilePath = path;
            DiagnosticStatus = Localizer.Te("diag.zip.done", "Saved to:");
        }
        catch (Exception ex)
        {
            AppLog.Warn("Diagnostic file failed.", ex);
            DiagnosticFilePath = "";
            DiagnosticStatus = Localizer.Te("diag.zip.failed", "Couldn't create the diagnostic file: {error}")
                .Replace("{error}", ex.Message);
        }
    }

    [RelayCommand]
    private void RevealDiagnosticFile()
    {
        if (HasDiagnosticFile) PlatformShell.RevealInFileManager(DiagnosticFilePath);
    }

    [RelayCommand]
    private async Task ResetPermissions()
    {
        var ok = await PermissionReset.RunAsync();
        PermissionsResetStatus = ok
            ? Localizer.Tf("home.perm_reset.done", ("pane", MacAccessibility.PointerPaneName()))
            : Localizer.Te("home.perm_reset.failed",
                "The reset did not work. Remove the old entries by hand with −, then add this version with +.");
        if (OperatingSystem.IsMacOS())
            PlatformShell.OpenInputMonitoringSettings();
    }

    private void RefreshLabels()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Intro));
        OnPropertyChanged(nameof(RestartTitle));
        OnPropertyChanged(nameof(RestartBody));
        OnPropertyChanged(nameof(RestartButton));
        OnPropertyChanged(nameof(DiagTitle));
        OnPropertyChanged(nameof(DiagBody));
        OnPropertyChanged(nameof(DiagButton));
        OnPropertyChanged(nameof(DiagReveal));
        OnPropertyChanged(nameof(PermTitle));
        OnPropertyChanged(nameof(PermBody));
        OnPropertyChanged(nameof(PermButton));
    }
}
