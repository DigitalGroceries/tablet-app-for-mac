using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenTabletArtist.ViewModels;

namespace OpenTabletArtist.Views;

public partial class BackupView : UserControl
{
    public BackupView() => InitializeComponent();

    private BackupViewModel? Vm => DataContext as BackupViewModel;

    // The macOS save/open panels follow the system's light/dark appearance.
    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm && TopLevel.GetTopLevel(this)?.StorageProvider is { } storage)
            await BackupPickers.ExportAsync(vm, storage);
    }

    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm && TopLevel.GetTopLevel(this)?.StorageProvider is { } storage)
            await BackupPickers.ImportAsync(vm, storage);
    }
}
