using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using OpenTabletArtist.Services;

namespace OpenTabletArtist;

public partial class App : Application
{
    // Hold a reference so the tray icon isn't garbage-collected (#72).
    private AppTray? _tray;
    // Wacom-like on-screen display when the Touch Ring mode changes.
    private RingModeHud? _ringHud;
    private RingGestureWatchdog? _ringWatchdog;

    public override void Initialize()
    {
        // The macOS app-menu title + about-panel name. Without this Avalonia shows "Avalonia Application".
        // No effect on Windows/Linux. Set before XAML load so it's in place when the native menu is built.
        Name = "Tablet App for Mac";
        AvaloniaXamlLoader.Load(this);
        // macOS app menu, set before Avalonia builds its default one: our own About Tablet App for Mac
        // (shows Home, no popup) replaces "About Avalonia"; Export / Import settings follow.
        if (System.OperatingSystem.IsMacOS())
            Avalonia.Controls.NativeMenu.SetMenu(this, AppMenu.Build());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Apply the saved Light/Dark/System theme before the window is shown (#139).
        ThemeService.ApplySaved();
        // Version 8: load UI language before any window/tray strings are built.
        Localizer.EnsureLoaded();
        // Version 15: an update leaves the old driver running; stop it before the session connects.
        Services.StaleDriverGuard.StopIfStale();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Closing the window hides it to the tray and the app keeps running, so we own exit
            // explicitly (the tray's Quit calls Shutdown). (#72)
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;

            var window = new MainWindow();
            // Quiet login leaves desktop.MainWindow unset until BringToFront; register the window so
            // AvaloniaScreensDisplayEnumerator can still read Screens once the platform handle exists
            // (Tablet › Mapping was caching "No displays detected" from the early empty enumerate).
            Services.AvaloniaScreensDisplayEnumerator.RegisterWindow(window);
            // Version 7 test 8: Avalonia shows desktop.MainWindow automatically when this method returns
            // (Avalonia discussion #12863). On a login start we create the window for the pen menu but do
            // NOT assign MainWindow until BringToFront runs (pen menu / user open).
            if (!Program.LaunchInBackground)
            {
                desktop.MainWindow = window;
                window.BringToFront("normal start");
            }
            else
            {
                MainWindow.HiddenStartUntil = System.DateTime.UtcNow.AddMinutes(1);
                // Version 7 test 9: no Dock icon while only the pen menu-bar icon is up.
                Services.MacActivationPolicy.SetAccessory();
                Services.AppLog.Info("Window: login start, MainWindow not assigned (menu-bar icon only).");
            }

            // Version 7: first-launch setup, and a check every 30 s in case a permission is later lost.
            if (!Program.LaunchInBackground)
                Avalonia.Threading.Dispatcher.UIThread.Post(Views.SetupWindow.ShowIfNeeded);
            new Avalonia.Threading.DispatcherTimer(System.TimeSpan.FromSeconds(30), Avalonia.Threading.DispatcherPriority.Background,
                (_, _) =>
                {
                    // Version 7 test 7: a login start doesn't pop up setup; it waits until the window has been opened.
                    if (Program.LaunchInBackground && !window.IsVisible) return;
                    Views.SetupWindow.ShowIfNeeded();
                }).Start();

            if (window.DataContext is ViewModels.MainViewModel vm)
                _tray = new AppTray(desktop, window, vm.Connection, vm.DeviceData, vm.SettingsCoordinator,
                    vm.ResolveUnsavedForQuitAsync, vm.CloseSessionAsync, vm.PrepareDaemonStopAsync);

            // R11: quit cleanly when the system asks (Cmd+Q, Dock, AppleScript, logout, shutdown, restart):
            // never cancel, never prompt, keep unsaved edits in a snapshot (real settings untouched), stop our own daemon.
            if (window.DataContext is ViewModels.MainViewModel qvm)
            {
                var systemQuit = new Services.SystemQuitCoordinator(
                    window.AllowCloseForQuit, qvm.SnapshotUnsavedForSystemQuitAsync, qvm.CloseSessionAsync,
                    qvm.PrepareOwnedDaemonStopForSystemQuit, Services.SystemQuitCoordinator.PumpUiUntil,
                    m => Services.AppLog.Warn(m));
                desktop.ShutdownRequested += (_, e) => e.Cancel = systemQuit.OnShutdownRequested();
            }

            // Remove the tray icon as part of a clean exit, so it doesn't ghost in the notification
            // area after Quit (Windows only clears an orphaned icon on hover otherwise). (#58)
            desktop.Exit += (_, _) => _tray?.Dispose();

            // Build P: ask once (new and upgrading users) whether to start at login. Not on a background launch.
            // Build R: an upgrade from OpenTabletArtist carries its start-at-login choice over instead of asking.
            // Version 7 test 5: start at login is the ordinary login item; remove the old launch agent (it crashed at login).
            // (Version 7 test 7: the old-agent clean-up now runs once, in Program.Main.)
            try { Services.AutoStartCoordinator.Default.ApplyLegacyChoice(); }
            catch (System.Exception ex) { Services.AppLog.Warn("Couldn't carry start at login over.", ex); }
            if (!Program.LaunchInBackground && Services.AutoStartCoordinator.Default.ShouldPrompt)
                Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
                {
                    try { await Views.AutoStartDialog.AskIfNeededAsync(Services.AutoStartCoordinator.Default, window); }
                    catch (System.Exception ex) { Services.AppLog.Warn("Start-at-login prompt failed.", ex); }
                }, Avalonia.Threading.DispatcherPriority.Background);

            _ringHud = new RingModeHud();
            _ringHud.Start();
            desktop.Exit += (_, _) => _ringHud?.Dispose();

            // Build J: safety net for the experimental smooth-rotate gesture (releases stuck R / mouse button
            // if the driver dies or hangs mid-gesture, then starts the driver again).
            _ringWatchdog = new RingGestureWatchdog();
            _ringWatchdog.Recovered += reason => Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
            {
                _ringHud?.ShowMessage("Touch ring gesture stopped. Input released, driver restarting");
                await System.Threading.Tasks.Task.Delay(1000);
                if (window.DataContext is ViewModels.MainViewModel m && !m.Connection.IsConnected
                    && m.Connection.StartDaemonCommand.CanExecute(null))
                    await m.Connection.StartDaemonCommand.ExecuteAsync(null);
            });
            _ringWatchdog.Start();
            desktop.Exit += (_, _) => _ringWatchdog?.Dispose();

            // When a second instance is launched, it signals this (primary) one to surface instead of
            // opening a duplicate window (#191). The signal arrives off the UI thread — marshal first.
            Program.Instance.ListenForActivation(() =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() => window.BringToFront("second copy opened", byUser: false)));

            // Version 1: on macOS a second "open" of the same app (Finder, Dock, Launchpad, Spotlight) doesn't start
            // a new process: macOS sends the running one a reopen event. Show the window then, including after a
            // silent start at login.
            if (TryGetFeature(typeof(Avalonia.Controls.ApplicationLifetimes.IActivatableLifetime)) is Avalonia.Controls.ApplicationLifetimes.IActivatableLifetime activatable)
                activatable.Activated += (_, e) =>
                {
                    if (e.Kind == Avalonia.Controls.ApplicationLifetimes.ActivationKind.Reopen)
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => window.BringToFront("macOS reopen event", byUser: false));
                    // A .tabletsettings file double-clicked in Finder: show its Import preview in the window.
                    if (e is Avalonia.Controls.ApplicationLifetimes.FileActivatedEventArgs files)
                        foreach (var f in files.Files)
                            if (Avalonia.Platform.Storage.StorageProviderExtensions.TryGetLocalPath(f) is { } path && path.EndsWith("." + Services.SettingsBundle.Extension, System.StringComparison.OrdinalIgnoreCase))
                            {
                                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                                {
                                    window.BringToFront("settings file opened", byUser: true);
                                    (window.DataContext as ViewModels.MainViewModel)?.OpenSettingsFile(path);
                                });
                                break;
                            }
                };

            AppMenu.Window = window;

            // A settings file passed on the command line (e.g. `open -a … file.tabletsettings` on a cold start).
            foreach (var arg in System.Environment.GetCommandLineArgs())
                if (arg.EndsWith("." + Services.SettingsBundle.Extension, System.StringComparison.OrdinalIgnoreCase)
                    && System.IO.File.Exists(arg) && window.DataContext is ViewModels.MainViewModel avm)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => avm.OpenSettingsFile(arg));
                    break;
                }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
