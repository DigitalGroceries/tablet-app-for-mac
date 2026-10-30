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
    /// <summary>The main window, set once it exists (the menu is built earlier, at app start).</summary>
    public static MainWindow? Window { get; set; }

    static bool Ready(out MainWindow window, out MainViewModel vm)
    {
        window = Window!; vm = (Window?.DataContext as MainViewModel)!;
        return Window != null && vm != null;
    }

    public static NativeMenu Build()
    {
        var about = new NativeMenuItem(L("appmenu.about", "About Tablet App for Mac"));
        about.Click += (_, _) =>
        {
            if (!Ready(out var window, out var vm)) return;
            window.BringToFront("app menu About", byUser: true);
            vm.ShowAbout();
        };

        var export = new NativeMenuItem(L("appmenu.export", "Export settings…"));
        export.Click += (_, _) => { if (Ready(out var w, out var m)) _ = ExportAsync(w, m, "app menu"); };

        var import = new NativeMenuItem(L("appmenu.import", "Import settings…"));
        import.Click += (_, _) => { if (Ready(out var w, out var m)) _ = ImportAsync(w, m, "app menu"); };

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

    /// <summary>Shared by the app menu and the pen (tray) menu: show the window at Settings › System,
    /// then open the save panel; the result shows on that page.</summary>
    public static async System.Threading.Tasks.Task ExportAsync(MainWindow window, MainViewModel vm, string from)
    {
        window.BringToFront(from + " Export settings", byUser: true);
        vm.ShowSettingsBackup();
        if (vm.Backup is { } b) await BackupPickers.ExportAsync(b, window.StorageProvider);
    }

    public static async System.Threading.Tasks.Task ImportAsync(MainWindow window, MainViewModel vm, string from)
    {
        window.BringToFront(from + " Import settings", byUser: true);
        vm.ShowSettingsBackup();
        if (vm.Backup is { } b) await BackupPickers.ImportAsync(b, window.StorageProvider);
    }

    static string L(string key, string en) => Localizer.Te(key, en);
}
