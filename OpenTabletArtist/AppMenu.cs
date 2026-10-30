using Avalonia.Controls;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;

namespace OpenTabletArtist;

/// <summary>
/// The macOS application menu (top-left "Tablet App for Mac"). Avalonia puts these items at the top of
/// that menu, above Services / Hide / Quit, replacing its own "About Avalonia". Every item shows its
/// result in the main window (no popups).
/// </summary>
public static class AppMenu
{
    public static NativeMenu Build(MainWindow window, MainViewModel vm)
    {
        var about = new NativeMenuItem(L("appmenu.about", "About Tablet App for Mac"));
        about.Click += (_, _) => { window.BringToFront("app menu About", byUser: true); vm.ShowAbout(); };

        var export = new NativeMenuItem(L("appmenu.export", "Export settings…"));
        export.Click += async (_, _) =>
        {
            window.BringToFront("app menu Export", byUser: true);
            vm.ShowSettingsBackup();
            if (vm.Backup is { } b) await BackupPickers.ExportAsync(b, window.StorageProvider);
        };

        var import = new NativeMenuItem(L("appmenu.import", "Import settings…"));
        import.Click += async (_, _) =>
        {
            window.BringToFront("app menu Import", byUser: true);
            vm.ShowSettingsBackup();
            if (vm.Backup is { } b) await BackupPickers.ImportAsync(b, window.StorageProvider);
        };

        var menu = new NativeMenu();
        menu.Items.Add(about);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(export);
        menu.Items.Add(import);

        // Follow a language change.
        Localizer.LanguageChanged += () =>
        {
            about.Header = L("appmenu.about", "About Tablet App for Mac");
            export.Header = L("appmenu.export", "Export settings…");
            import.Header = L("appmenu.import", "Import settings…");
        };
        return menu;
    }

    static string L(string key, string en) => Localizer.Te(key, en);
}
