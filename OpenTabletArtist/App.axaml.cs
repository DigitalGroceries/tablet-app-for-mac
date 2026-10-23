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
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Apply the saved Light/Dark/System theme before the window is shown (#139).
        ThemeService.ApplySaved();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Closing the window hides it to the tray and the app keeps running, so we own exit
            // explicitly (the tray's Quit calls Shutdown). (#72)
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;

            var window = new MainWindow();
            desktop.MainWindow = window;
            if (!Program.LaunchInBackground)
                window.Show();

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
            if (System.OperatingSystem.IsMacOS())
                try { Services.MacLoginItemService.MigrateToAgent(); }
                catch (System.Exception ex) { Services.AppLog.Warn("Couldn't move start at login to the launch agent.", ex); }
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
                Avalonia.Threading.Dispatcher.UIThread.Post(window.BringToFront));

            // Version 1: on macOS a second "open" of the same app (Finder, Dock, Launchpad, Spotlight) doesn't start
            // a new process: macOS sends the running one a reopen event. Show the window then, including after a
            // silent start at login.
            if (TryGetFeature(typeof(Avalonia.Controls.ApplicationLifetimes.IActivatableLifetime)) is Avalonia.Controls.ApplicationLifetimes.IActivatableLifetime activatable)
                activatable.Activated += (_, e) =>
                {
                    if (e.Kind == Avalonia.Controls.ApplicationLifetimes.ActivationKind.Reopen)
                        Avalonia.Threading.Dispatcher.UIThread.Post(window.BringToFront);
                };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
