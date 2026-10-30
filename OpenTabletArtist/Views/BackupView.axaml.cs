using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;

namespace OpenTabletArtist.Views;

public partial class BackupView : UserControl
{
    private static readonly FilePickerFileType SettingsFileType =
        new("Tablet App settings") { Patterns = new[] { "*." + SettingsBundle.Extension } };

    public BackupView() => InitializeComponent();

    private BackupViewModel? Vm => DataContext as BackupViewModel;

    // The macOS save/open panels follow the system's light/dark appearance.
    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
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

    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
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
