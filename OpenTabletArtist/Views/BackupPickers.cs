using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;

namespace OpenTabletArtist.Views;

/// <summary>The save/open panels for settings files, shared by Settings › System and the app menu.</summary>
public static class BackupPickers
{
    public static readonly FilePickerFileType SettingsFileType =
        new("Tablet App settings") { Patterns = new[] { "*." + SettingsBundle.Extension },
            AppleUniformTypeIdentifiers = new[] { SettingsBundle.TypeIdentifier } };

    public static async Task ExportAsync(BackupViewModel vm, IStorageProvider storage)
    {
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = vm.ExportDialogTitle,
            SuggestedFileName = vm.SuggestedFileName,   // e.g. Intuos4PTK440_30Oct26; the user can rename
            DefaultExtension = SettingsBundle.Extension,
            FileTypeChoices = new[] { SettingsFileType },
        });
        var path = file?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return;
        if (!path.EndsWith("." + SettingsBundle.Extension, StringComparison.OrdinalIgnoreCase))
            path += "." + SettingsBundle.Extension;
        vm.ExportTo(path);
    }

    public static async Task ImportAsync(BackupViewModel vm, IStorageProvider storage)
    {
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = vm.ImportDialogTitle,
            AllowMultiple = false,
            FileTypeFilter = new[] { SettingsFileType, FilePickerFileTypes.All },
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return;
        vm.PreviewFile(path);
    }
}
