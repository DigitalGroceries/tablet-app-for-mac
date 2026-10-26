using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;

namespace OpenTabletArtist;

/// <summary>
/// System-tray icon + background mode (#72). While the app runs, a tray icon reflects daemon status
/// and offers Show / daemon control / Quit. Closing the window hides it to the tray (see
/// <see cref="MainWindow"/>); the app keeps running until Quit is chosen here.
///
/// The tray also surfaces a few tablet actions so they're reachable with the window closed:
/// an "Active Tablet" picker (#190) and a "Switch display" submenu for the active tablet (#187).
/// </summary>
public sealed class AppTray : IDisposable
{
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private readonly MainWindow _window;
    private readonly IConnectionState _conn;
    private readonly IDeviceData _deviceData;
    private readonly ISettingsCoordinator _settingsCoord;
    private readonly Func<Task<bool>>? _onQuitAsync; // resolve unsaved settings before exit
    private readonly Func<TimeSpan, Task<bool>>? _onCloseAsync; // settle settings work before exit (#828)
    private readonly Func<Task<Func<Task>?>>? _onPrepareStopAsync; // capture the stop target (#828)

    /// <summary>Menu-bar icon: 36 px (18 pt @2x) black-alpha pen outline; trayicon.png is the 18 px @1x copy.</summary>
    public const string TrayIconUri = "avares://OpenTabletArtist/Assets/trayicon@2x.png";

    /// <summary>The old full-colour icon, used only if the template icon can't be loaded.</summary>
    public const string FallbackTrayIconUri = "avares://OpenTabletArtist/Assets/appicon.png";

    /// <summary>Sets the menu-bar icon: the template pen outline (tinted by macOS for light/dark menu bars),
    /// falling back to the old app icon. Never throws — a tray icon problem must not stop startup (R10).
    /// Returns which icon was applied: "template", "fallback" or "none".</summary>
    public static string ApplyTrayIcon(TrayIcon tray, string? iconUri = null)
    {
        try
        {
            using var s = AssetLoader.Open(new Uri(iconUri ?? TrayIconUri));
            tray.Icon = new WindowIcon(s);
            try { MacOSProperties.SetIsTemplateIcon(tray, true); }
            catch (Exception ex) { AppLog.Warn("Tray icon: couldn't mark it as a template image", ex); }
            return "template";
        }
        catch (Exception ex) { AppLog.Warn("Tray icon: template icon failed, using the app icon", ex); }
        try
        {
            using var s = AssetLoader.Open(new Uri(FallbackTrayIconUri));
            tray.Icon = new WindowIcon(s);
            try { MacOSProperties.SetIsTemplateIcon(tray, false); } catch { }
            return "fallback";
        }
        catch (Exception ex) { AppLog.Warn("Tray icon: no icon could be loaded", ex); return "none"; }
    }

    private readonly TrayIcon _tray;
    private readonly NativeMenuItem _activeTabletItem;

    private readonly NativeMenu _activeTabletMenu;
    private readonly NativeMenuItem _switchDisplayItem;
    private readonly NativeMenu _displayMenu;
    private readonly NativeMenuItemSeparator _tabletSeparator;
    private readonly NativeMenuItem _startItem;
    private readonly NativeMenuItem _stopItem;
    private readonly NativeMenuItem _restartItem;
    private readonly NativeMenuItem _quitStopItem; // Quit + stop the daemon (#596)
    private readonly NativeMenuItem _showItem;
    private readonly NativeMenuItem _checkUpdatesItem;
    private readonly NativeMenuItem _updateAvailableItem;
    private readonly NativeMenuItem _quitItem;

    // Signatures of the last-built submenus (so the 3s data poll doesn't churn an open menu): the
    // display submenu (active tablet + monitor geometry + mapped display) and the active-tablet
    // picker (connected tablets + which is active).
    private string _displaySignature = "";
    private string _activeTabletSignature = "";

    public AppTray(IClassicDesktopStyleApplicationLifetime desktop, MainWindow window,
        IConnectionState conn, IDeviceData deviceData, ISettingsCoordinator settingsCoord,
        Func<Task<bool>>? onQuitAsync = null, Func<TimeSpan, Task<bool>>? onCloseAsync = null,
        Func<Task<Func<Task>?>>? onPrepareStopAsync = null)
    {
        _desktop = desktop;
        _window = window;
        _conn = conn;
        _deviceData = deviceData;
        _settingsCoord = settingsCoord;
        _onQuitAsync = onQuitAsync;
        _onCloseAsync = onCloseAsync;
        _onPrepareStopAsync = onPrepareStopAsync;

        Current = this;
        _tray = new TrayIcon { ToolTipText = Localizer.T("tray.tooltip"), IsVisible = true };
        ApplyTrayIcon(_tray);

        _showItem = new NativeMenuItem(Localizer.T("tray.show"));
        _showItem.Click += (_, _) => ShowWindow();

        // Active-tablet picker (#190 phase 3): only shown when more than one tablet is connected; the
        // tablet actions below all target the active tablet.
        _activeTabletMenu = new NativeMenu();
        _activeTabletItem = new NativeMenuItem(Localizer.T("tray.active_tablet")) { Menu = _activeTabletMenu };

        // Tablet group (#187): the "Switch Display" submenu for the active tablet.
        _displayMenu = new NativeMenu();
        _switchDisplayItem = new NativeMenuItem(Localizer.T("tray.switch_display")) { Menu = _displayMenu };
        _tabletSeparator = new NativeMenuItemSeparator();

        _startItem = new NativeMenuItem(Localizer.T("tray.start_daemon")) { Command = _conn.StartDaemonCommand };
        _restartItem = new NativeMenuItem(Localizer.T("tray.restart_daemon")) { Command = _conn.RestartDaemonCommand };
        _stopItem = new NativeMenuItem(Localizer.T("tray.stop_daemon")) { Command = _conn.StopDaemonCommand };

        // Updates: always offer a check; show "Update available" only when a newer release is known
        // (shared with Home/About via AppUpdateState). Own section above Quit — Quit stays last.
        _checkUpdatesItem = new NativeMenuItem(Localizer.T("update.check_now"));
        _checkUpdatesItem.Click += (_, _) => _ = CheckUpdatesFromTrayAsync();
        _updateAvailableItem = new NativeMenuItem(Localizer.T("update.prompt_title")) { IsVisible = false };
        _updateAvailableItem.Click += (_, _) => _ = InstallUpdateFromTrayAsync();

        // Quit leaves the daemon running (it's a separate process); "Quit and stop the daemon" also
        // stops it (#596), shown only when there's a running daemon to stop.
        _quitItem = new NativeMenuItem(Localizer.T("tray.quit"));
        _quitItem.Click += (_, _) => Quit();
        _quitStopItem = new NativeMenuItem(Localizer.T("tray.quit_stop"));
        _quitStopItem.Click += (_, _) => Quit(stopDaemon: true);

        var menu = new NativeMenu();
        menu.Items.Add(_showItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(_activeTabletItem);
        menu.Items.Add(_switchDisplayItem);
        menu.Items.Add(_tabletSeparator);
        menu.Items.Add(_startItem);
        menu.Items.Add(_restartItem);
        menu.Items.Add(_stopItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(_checkUpdatesItem);
        menu.Items.Add(_updateAvailableItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(_quitItem);
        menu.Items.Add(_quitStopItem);
        _tray.Menu = menu;

        _tray.Clicked += (_, _) => ShowWindow();

        _conn.PropertyChanged += OnConnectionChanged;
        _deviceData.DataLoaded += OnDataLoaded;
        AppUpdateState.Shared.PropertyChanged += OnUpdateStateChanged;
        // NativeMenuItem headers are set once at construction; refresh them when the language changes
        // (the main window already did; the pen menu stayed on the old language until this).
        Localizer.LanguageChanged += OnLanguageChanged;
        UpdateMenu();
        SyncUpdateMenu();

        TrayIcon.SetIcons(Application.Current!, new TrayIcons { _tray });
        AppLog.Info($"Tray icon created ({TrayDiagnostics()}).");
        // Version 7: the app now starts through a small launcher. Set the icon again once the app has
        // finished launching, so the menu-bar item is (re)registered with the running app.
        DispatcherTimer.RunOnce(() =>
        {
            try
            {
                _tray.IsVisible = false; _tray.IsVisible = true;
                TrayIcon.SetIcons(Application.Current!, new TrayIcons { _tray });
                AppLog.Info("Tray icon set again after launch.");
            }
            catch (Exception ex) { AppLog.Warn("Tray icon: couldn't set it again after launch", ex); }
        }, TimeSpan.FromSeconds(2));
    }

    /// <summary>What the log needs to explain a missing menu-bar icon: our process path and bundle.</summary>
    public static string TrayDiagnostics()
        => $"exe {Environment.ProcessPath}, args0 {Environment.GetCommandLineArgs().FirstOrDefault()}";

    private void OnConnectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Status came from a daemon callback (possibly off the UI thread) — marshal before touching UI.
        Dispatcher.UIThread.Post(UpdateMenu);
    }

    // The data load already raises this on the UI thread, but Post keeps it uniform with the
    // connection callback above and harmless if a future caller isn't on the dispatcher.
    private void OnDataLoaded() => Dispatcher.UIThread.Post(UpdateMenu);

    private void OnLanguageChanged() => Dispatcher.UIThread.Post(() =>
    {
        ApplyLocalizedLabels();
        // Force display submenu rebuild so "Display N" strings pick up the new language.
        _displaySignature = "";
        UpdateMenu();
    });

    /// <summary>Re-apply tray menu titles from Localizer after a language change. Headers are not
    /// bindings — Avalonia NativeMenuItem keeps the string from construction until we set Header.</summary>
    private void ApplyLocalizedLabels()
    {
        _showItem.Header = Localizer.T("tray.show");
        _activeTabletItem.Header = Localizer.T("tray.active_tablet");
        _switchDisplayItem.Header = Localizer.T("tray.switch_display");
        _startItem.Header = Localizer.T("tray.start_daemon");
        _restartItem.Header = Localizer.T("tray.restart_daemon");
        _stopItem.Header = Localizer.T("tray.stop_daemon");
        _checkUpdatesItem.Header = Localizer.T("update.check_now");
        _updateAvailableItem.Header = Localizer.T("update.prompt_title");
        _quitItem.Header = Localizer.T("tray.quit");
        _quitStopItem.Header = Localizer.T("tray.quit_stop");
    }

    private void UpdateMenu()
    {
        var connected = _conn.IsConnected;

        // Daemon controls: Start whenever nothing is connected (#955), Stop/Restart only when it is.
        _startItem.IsVisible = _conn.ShowStartButton;
        _restartItem.IsVisible = connected;
        _stopItem.IsVisible = connected;
        _quitStopItem.IsVisible = connected; // only offer "quit + stop" when there's a daemon to stop

        // And the same busy gate the Daemon page applies. Without it the tray offered an enabled Start
        // while a Restart was mid-flight: pressing it did nothing, because StartDaemon returns early
        // when IsDaemonBusy, so the menu was advertising an action it would silently decline (#956).
        //
        // "Quit and stop the daemon" takes the gate too, and that one is not cosmetic. It stops the PID
        // captured when the quit began, and a Restart in flight replaces that PID part-way through:
        //
        //     Restart: stop PID 1  ->  close session  ->  Restart: launch PID 2
        //     Quit and stop: stop captured PID 1  ->  quit returns, PID 2 still running
        //
        // So the action that exists to leave nothing behind leaves the replacement running (#957).
        //
        // This gate removes the OFFERED path and nothing more. UpdateMenu is posted, so between a busy
        // state changing and the dispatcher running this, the menu still shows what it showed before --
        // which is correct presentation updating and not an atomic safeguard. The fault underneath is
        // that RestartDaemon launches even after its stop wait is cancelled, and that is #958.
        //
        // Plain Quit is not gated, so this does not trap anyone in the application: there is still a
        // way out while an operation runs, it just does not also promise to stop a daemon it cannot
        // reliably identify yet.
        var busy = _conn.IsDaemonBusy;
        _startItem.IsEnabled = !busy;
        _restartItem.IsEnabled = !busy;
        _stopItem.IsEnabled = !busy;
        _quitStopItem.IsEnabled = !busy;
        _tray.ToolTipText = Localizer.Tf("tray.tooltip_status", ("status", _conn.DaemonStatusText));

        UpdateTabletItems(connected);
        SyncUpdateMenu();
    }

    private void OnUpdateStateChanged(object? sender, PropertyChangedEventArgs e)
        => Dispatcher.UIThread.Post(SyncUpdateMenu);

    /// <summary>Show "Update available" only when AppUpdateState knows a newer release.</summary>
    private void SyncUpdateMenu()
    {
        var ready = AppUpdateState.Shared.UpdateReady;
        _updateAvailableItem.IsVisible = ready;
        var busy = AppUpdateState.Shared.IsBusy;
        _checkUpdatesItem.IsEnabled = !busy;
        _updateAvailableItem.IsEnabled = ready && !busy;
    }

    private async Task CheckUpdatesFromTrayAsync()
    {
        await AppUpdateState.Shared.CheckAsync();
        // Tray can't show Home's status line — open the window and, when already latest, say so in a dialog.
        ShowWindow();
        if (AppUpdateState.Shared.UpdateReady) { await ChooseUpdateFromTrayAsync(); return; }
        try
        {
            await Helpers.Dialogs.ShowMessageAsync(
                Localizer.T("update.check_now"),
                Localizer.T("update.latest"),
                _window);
        }
        catch (Exception ex) { AppLog.Warn("Tray update check: couldn't show the latest-version dialog", ex); }
    }

    private async Task InstallUpdateFromTrayAsync()
    {
        if (!AppUpdateState.Shared.UpdateReady) return;
        ShowWindow();
        await ChooseUpdateFromTrayAsync();
    }

    /// <summary>Version 14: the choice window (newer stable, BETA not tested yet, or back to stable), then install
    /// the chosen one.</summary>
    private async Task ChooseUpdateFromTrayAsync()
    {
        Services.Updater.Release? chosen;
        try { chosen = await Views.UpdatePromptWindow.ChooseAsync(AppUpdateState.Shared.Choices); }
        catch (Exception ex) { AppLog.Warn("Tray update: couldn't show the choice window", ex); return; }
        if (chosen == null) return;
        await AppUpdateState.Shared.InstallAsync(chosen);
        // If install returned an error string (app still running), surface it.
        var status = AppUpdateState.Shared.Status;
        if (!string.IsNullOrEmpty(status) && status != Localizer.T("update.installing")
            && status != Localizer.T("update.downloading"))
        {
            try { await Helpers.Dialogs.ShowMessageAsync(Localizer.T("update.prompt_title"), status, _window); }
            catch (Exception ex) { AppLog.Warn("Tray update: couldn't show the install-error dialog", ex); }
        }
    }

    /// <summary>Refresh the active-tablet picker, the dynamics-reveal line, and the tablet actions —
    /// all targeting the active tablet (#190 phase 3).</summary>
    private void UpdateTabletItems(bool connected)
    {
        var activeProfile = connected ? ActiveProfile() : null;

        // #187 — switch the active tablet's mapped display. Only when it's in an Absolute mode we can
        // map (otherwise there's no display area to set).
        var mappable = activeProfile != null && IsAbsoluteMappable(activeProfile);
        _switchDisplayItem.IsVisible = mappable;
        if (mappable)
            RebuildDisplayMenu(activeProfile!);
        else
            _displaySignature = ""; // force a rebuild next time it becomes mappable

        // #190 — active-tablet picker, only when there's a choice to make (>1 connected).
        var multipleTablets = connected && _deviceData.DetectedTablets.Count > 1;
        _activeTabletItem.IsVisible = multipleTablets;
        if (multipleTablets)
            RebuildActiveTabletMenu();
        else
            _activeTabletSignature = "";

        _tabletSeparator.IsVisible = mappable || multipleTablets;
    }

    /// <summary>The detected profile for the active tablet, falling back to any detected one.</summary>
    private OpenTabletDriver.Desktop.Profiles.Profile? ActiveProfile()
    {
        var name = _deviceData.ActiveTabletName;
        return _deviceData.Profiles.FirstOrDefault(p => p.IsDetected && p.Profile.Tablet == name)?.Profile
            ?? _deviceData.Profiles.FirstOrDefault(p => p.IsDetected)?.Profile;
    }

    private void RebuildActiveTabletMenu()
    {
        var names = _deviceData.DetectedTablets.Select(t => t.Name).ToList();
        var active = _deviceData.ActiveTabletName;

        var signature = (active ?? "-") + "|" + string.Join(";", names);
        if (signature == _activeTabletSignature) return;
        _activeTabletSignature = signature;

        _activeTabletMenu.Items.Clear();
        foreach (var name in names)
        {
            var item = new NativeMenuItem(name)
            {
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = name == active,
            };
            var target = name; // capture per-iteration
            item.Click += (_, _) => { _deviceData.SetActiveTablet(target); UpdateMenu(); };
            _activeTabletMenu.Items.Add(item);
        }
    }

    private static bool IsAbsoluteMappable(OpenTabletDriver.Desktop.Profiles.Profile profile) =>
        profile.AbsoluteModeSettings != null &&
        (profile.OutputMode?.Path?.Contains("Absolute", StringComparison.OrdinalIgnoreCase) ?? false);

    private void RebuildDisplayMenu(OpenTabletDriver.Desktop.Profiles.Profile profile)
    {
        var displays = DisplayEnumerator.Enumerate();
        var mapped = DisplayMappingApplier.CurrentlyMapped(profile, displays);

        var signature = profile.Tablet + "|" + (mapped?.Number.ToString() ?? "-") + "|" +
            string.Join(";", displays.Select(d => $"{d.Number},{d.X},{d.Y},{d.Width},{d.Height}"));
        if (signature == _displaySignature) return;
        _displaySignature = signature;

        _displayMenu.Items.Clear();
        foreach (var d in displays)
        {
            var label = Localizer.Tf("tray.display_n", ("number", d.Number));
            if (!string.IsNullOrWhiteSpace(d.Name)) label += $" — {d.Name}";
            label += $"  ({d.Width}×{d.Height})";
            if (d.IsPrimary) label += " · primary";

            var item = new NativeMenuItem(label)
            {
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = mapped != null && d.Number == mapped.Number,
            };
            var target = d; // capture per-iteration
            item.Click += (_, _) => _ = SwitchDisplayAsync(target);
            _displayMenu.Items.Add(item);
        }
    }

    private async Task SwitchDisplayAsync(DisplayInfo display)
    {
        var settings = _settingsCoord.CurrentSettings;
        var activeName = _deviceData.ActiveTabletName;
        if (settings == null || string.IsNullOrEmpty(activeName)) return;

        // Mutate the live profile inside CurrentSettings, then apply the settings —
        // same path the tablet dialog's "Apply mapping" uses.
        var profile = settings.Profiles.FirstOrDefault(p => p.Tablet == activeName);
        if (profile == null) return;

        var digitizer = _deviceData.GetTabletDigitizer(activeName);
        // Re-enumerate so the area is placed against the current monitor layout (the menu the click came
        // from may be a moment stale); the full set is needed for OTD's virtual-screen coordinates.
        var displays = DisplayEnumerator.Enumerate();
        if (!DisplayMappingApplier.ApplyToProfile(profile, digitizer, display, displays)) return;

        try { await _settingsCoord.ApplySettingsAsync(settings); }
        catch { /* best-effort; the next data load will resync the menu's checkmark */ }
    }

    private void ShowWindow() => _window.BringToFront("pen menu");

    /// <summary>Build P: the warning shown before quitting (the tablet needs the app running).</summary>
    public const string QuitWarning = "Your tablet will stop working until you open Tablet App for Mac again.";

    /// <summary>The running tray (null before startup finishes).</summary>
    public static AppTray? Current { get; private set; }

    /// <summary>Version 7 test 3: used by setup's "Restart the app". Our daemon must stop too, or it keeps the
    /// old permission state and the pen still can't move the pointer after the restart.</summary>
    public void QuitAndStopDaemon() => Quit(stopDaemon: true);

    private async void Quit(bool stopDaemon = false)
    {
        // A dialog needs a visible owner; the window may be hidden in the menu bar, so show it first.
        _window.BringToFront("quit");
        if (!await Helpers.Dialogs.ShowConfirmAsync(Localizer.T("tray.quit_title"), Localizer.T("tray.quit_warning") + " " + Localizer.T("tray.quit_now"), _window)) return;
        if (_onQuitAsync is not null && !await _onQuitAsync()) return;
        _window.AllowCloseForQuit();

        // Decided here, while the daemon is still connected, and performed by the sequence after the
        // close. StopDaemonCommand resolves its own target when it runs, so deferring it wholesale would
        // have it ask a session that has gone -- and stop every daemon on the machine instead of ours.
        var stop = stopDaemon && _onPrepareStopAsync != null ? await _onPrepareStopAsync() : null;

        // The order matters and is documented where it lives, in QuitSequence.
        await QuitSequence.RunAsync(
            closeSession: _onCloseAsync,
            stopDaemon: stop,
            warn: message => Trace.TraceWarning(message));

        Dispose();
        _desktop.Shutdown(); // closes the window (→ MainViewModel.Dispose) and exits the app
    }

    public void Dispose()
    {
        Localizer.LanguageChanged -= OnLanguageChanged;
        _conn.PropertyChanged -= OnConnectionChanged;
        _deviceData.DataLoaded -= OnDataLoaded;
        AppUpdateState.Shared.PropertyChanged -= OnUpdateStateChanged;
        _tray.IsVisible = false;
        if (Application.Current is { } app)
            TrayIcon.SetIcons(app, new TrayIcons());
        _tray.Dispose();
    }
}
