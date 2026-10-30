using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>One line of the import preview as shown in the window.</summary>
public sealed record SettingsChangeRow(string Text, bool IsChange);

/// <summary>An automatic backup the user can bring back.</summary>
public sealed record AutoBackupRow(string Path, string Label);

/// <summary>
/// Settings › System › Backup: export all settings to one file, import one (preview first, then apply),
/// and the automatic backups made on each new version. All messages show in this page, never a popup.
/// </summary>
public partial class BackupViewModel : ObservableObject
{
    private readonly Func<string?> _tabletName;
    private readonly Func<Task>? _reloadDriver;
    private SettingsBundle.Bundle? _pending;

    public BackupViewModel(Func<string?>? tabletName = null, Func<Task>? reloadDriver = null)
    {
        _tabletName = tabletName ?? (() => null);
        _reloadDriver = reloadDriver;
        RefreshAutoBackups();
    }

    /// <summary>The diagnostic-file card shown under the backup (set by the shell).</summary>
    public DiagnosticsViewModel? Diagnostics { get; set; }

    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _statusIsError;
    [ObservableProperty] private bool _hasPreview;
    [ObservableProperty] private string _previewTitle = "";
    [ObservableProperty] private IReadOnlyList<SettingsChangeRow> _previewRows = Array.Empty<SettingsChangeRow>();
    [ObservableProperty] private bool _previewHasChanges;
    [ObservableProperty] private IReadOnlyList<AutoBackupRow> _autoBackups = Array.Empty<AutoBackupRow>();
    public bool HasStatus => !string.IsNullOrEmpty(Status);
    partial void OnStatusChanged(string value) => OnPropertyChanged(nameof(HasStatus));
    public bool HasAutoBackups => AutoBackups.Count > 0;
    partial void OnAutoBackupsChanged(IReadOnlyList<AutoBackupRow> value) => OnPropertyChanged(nameof(HasAutoBackups));

    // ---- labels (Localizer, English fallback)
    public string SectionTitle => L("backup.section", "Settings backup");
    public string Description => L("backup.description",
        "Save all your settings to one file: tablets, pens, presets, applications, calibration and app settings. " +
        "You can email the file to someone else, or keep it safe for a new Mac. Serial numbers are left out.");
    public string ExportLabel => L("backup.export", "Export settings…");
    public string ImportLabel => L("backup.import", "Import settings…");
    public string ApplyLabel => L("backup.apply", "Apply these settings");
    public string CancelLabel => L("app.cancel", "Cancel");
    public string AutoTitle => L("backup.auto.title", "Automatic backups");
    public string AutoDescription => L("backup.auto.description",
        "A backup is made when a new version starts for the first time, and before every import. The last 5 are kept.");
    public string RestoreLabel => L("backup.auto.restore", "Preview…");
    public string ShowFolderLabel => L("backup.auto.folder", "Show in Finder");
    public string ExportDialogTitle => L("backup.export.dialog", "Export settings");
    public string ImportDialogTitle => L("backup.import.dialog", "Import settings");

    static string L(string key, string en) => Localizer.Te(key, en);

    public string SuggestedFileName => SettingsBundle.SuggestedFileName(_tabletName(), DateTime.Now);

    public void RefreshAutoBackups() =>
        AutoBackups = SettingsBundle.AutoBackups()
            .Select(p => new AutoBackupRow(p, Path.GetFileNameWithoutExtension(p)["auto-".Length..]))
            .ToList();

    void Say(string text, bool error = false) { StatusIsError = error; Status = text; }

    /// <summary>Called by the view after the save dialog.</summary>
    public void ExportTo(string path)
    {
        try
        {
            SettingsBundle.Export(path, portable: true);
            Say(Localizer.Te("backup.export.done", "Settings saved to {path}").Replace("{path}", path));
        }
        catch (Exception ex)
        {
            AppLog.Warn("Settings export failed.", ex);
            Say(Localizer.Te("backup.export.failed", "Couldn't save the settings: {error}").Replace("{error}", ex.Message), true);
        }
    }

    /// <summary>Called by the view after the open dialog: reads the file and shows what would change.</summary>
    public void PreviewFile(string path)
    {
        try
        {
            var b = SettingsBundle.Read(path);
            var p = SettingsBundle.Preview(b);
            _pending = b;
            PreviewTitle = Localizer.Te("backup.preview.title", "{file} — made by version {version} on {date}")
                .Replace("{file}", Path.GetFileName(path))
                .Replace("{version}", p.SourceVersion)
                .Replace("{date}", p.Created == default ? "?" : p.Created.ToString("d MMM yyyy HH:mm"));
            PreviewRows = p.Changes
                .OrderBy(c => c.Kind == "same").ThenBy(c => c.Area).ThenBy(c => c.Item)
                .Select(c => new SettingsChangeRow($"{KindText(c.Kind)}  {AreaText(c.Area)}: {c.Item}", c.Kind != "same"))
                .ToList();
            PreviewHasChanges = p.HasChanges;
            HasPreview = true;
            Say(p.HasChanges
                ? L("backup.preview.check", "Check the changes below, then choose Apply.")
                : L("backup.preview.nothing", "This file matches your current settings. Nothing would change."));
        }
        catch (Exception ex)
        {
            _pending = null;
            HasPreview = false;
            Say(Localizer.Te("backup.import.failed", "Couldn't read this file: {error}").Replace("{error}", ex.Message), true);
        }
    }

    static string KindText(string kind) => kind switch
    {
        "new" => L("backup.kind.new", "New"),
        "changed" => L("backup.kind.changed", "Changed"),
        _ => L("backup.kind.same", "Same"),
    };

    static string AreaText(string area) => area switch
    {
        "tablet" => L("backup.area.tablet", "Tablet"),
        "pen" => L("backup.area.pen", "Pens"),
        "app" => L("backup.area.app", "Applications"),
        "ring" => L("backup.area.ring", "Touch Ring"),
        "preset" => L("backup.area.preset", "Preset"),
        "config" => L("backup.area.config", "Tablet configuration"),
        _ => L("backup.area.pref", "App setting"),
    };

    [RelayCommand]
    private async Task ApplyImport()
    {
        if (_pending == null) return;
        try
        {
            var safety = SettingsBundle.Apply(_pending);
            _pending = null;
            HasPreview = false;
            RefreshAutoBackups();
            if (_reloadDriver != null) await _reloadDriver();
            Say(Localizer.Te("backup.import.done",
                "Settings imported. Your previous settings were backed up first ({file}). Quit and reopen the app to finish.")
                .Replace("{file}", Path.GetFileName(safety)));
        }
        catch (Exception ex)
        {
            AppLog.Warn("Settings import failed.", ex);
            Say(Localizer.Te("backup.import.failed", "Couldn't read this file: {error}").Replace("{error}", ex.Message), true);
        }
    }

    [RelayCommand]
    private void CancelImport()
    {
        _pending = null;
        HasPreview = false;
        Say("");
    }

    [RelayCommand]
    private void PreviewAutoBackup(AutoBackupRow? row)
    {
        if (row != null) PreviewFile(row.Path);
    }

    [RelayCommand]
    private void ShowBackupsFolder()
    {
        try
        {
            Directory.CreateDirectory(SettingsBundle.BackupsDir);
            PlatformShell.RevealInFileManager(SettingsBundle.BackupsDir);
        }
        catch (Exception ex) { Say(ex.Message, true); }
    }
}
