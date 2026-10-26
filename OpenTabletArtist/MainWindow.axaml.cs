using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Helpers;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using OtdInterop;

namespace OpenTabletArtist;

public partial class MainWindow : Window
{
    private bool _allowClose;
    // One-time "still running in the tray" hint, persisted so it only shows on the first close (#72).
    // Skipped for --background sign-in launches (#381).
    private bool _trayHintShown = Program.LaunchInBackground || AppSettings.Get("TrayHintShown") == "true";
    private ProfileSwitchService? _switchSub;  // tracked so we can unsubscribe on DataContext change / close
    private MonitorCycleService? _cycleSub;    // ditto, for the monitor-cycle toast (#89)

    public MainWindow()
    {
        InitializeComponent();
        // Title stays the bare app name so the taskbar / Alt-Tab entry reads "OpenTabletArtist" (it also
        // shows small in the extended-chrome caption top-left); the version lives on Home's About card.
        Title = "Tablet App for Mac";
        // DataContext is set in XAML (<vm:MainViewModel/>), so it's already assigned here and the
        // DataContextChanged from that assignment fired inside InitializeComponent, before we could
        // handle it. Wire the switch subscription now for the current VM, and keep the handler for
        // any later DataContext change. (#320)
        OnDataContextChanged(this, EventArgs.Empty);
        DataContextChanged += OnDataContextChanged;
        // Regaining focus re-pulls settings so an external edit (e.g. the mapping changed in the OTD UX
        // while we were in the background) is picked up promptly rather than on the next fallback poll.
        Activated += (_, _) => (DataContext as MainViewModel)?.OnWindowActivated();
        // Version 7 test 7: log every time the window opens, and hide it again if something opened it during a
        // login start without a request (the stack says what did).
        Opened += (_, _) =>
        {
            if (_showRequested || !Program.LaunchInBackground) { AppLog.Info($"Window: opened (requested {_showRequested}, login start {Program.LaunchInBackground})."); return; }
            var who = string.Join(" < ", Environment.StackTrace.Split('\n').Skip(2).Take(6).Select(l => l.Trim()));
            AppLog.Info("Window: opened without a request during a login start: " + who);
            if (DateTime.UtcNow < HiddenStartUntil) { HideToTray("login start reopen"); }
        };
        Closed += (_, _) =>
        {
            if (_switchSub != null) { _switchSub.Switched -= OnProfileSwitched; _switchSub.SwitchFailed -= OnProfileSwitchFailed; _switchSub.RestoreFailed -= OnProfileRestoreFailed; }
            if (_cycleSub != null) _cycleSub.Cycled -= OnMonitorCycled;
            ProfileToast.Dismiss();
            (DataContext as MainViewModel)?.Dispose();
        };
    }

    // Report manual preset and monitor hotkey actions even while the app is in the tray.
    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_switchSub != null) { _switchSub.Switched -= OnProfileSwitched; _switchSub.SwitchFailed -= OnProfileSwitchFailed; _switchSub.RestoreFailed -= OnProfileRestoreFailed; }
        if (_cycleSub != null) _cycleSub.Cycled -= OnMonitorCycled;

        var vm = DataContext as MainViewModel;
        _switchSub = vm?.ProfileSwitch;
        _cycleSub = vm?.MonitorCycle;
        if (_switchSub != null) { _switchSub.Switched += OnProfileSwitched; _switchSub.SwitchFailed += OnProfileSwitchFailed; _switchSub.RestoreFailed += OnProfileRestoreFailed; }
        if (_cycleSub != null) _cycleSub.Cycled += OnMonitorCycled;
    }

    private void OnProfileSwitched(string? snapshot)
    {
        // The Switched continuation may resume off the UI thread; marshal before touching windows.
        Dispatcher.UIThread.Post(() => ProfileToast.Show(
            snapshot == null ? "Restored saved settings" : $"Switched to “{snapshot}”"));
    }

    // A hotkey whose preset has gone (deleted or moved outside the app, or while OTA was closed) used to
    // do nothing at all, which is indistinguishable from the hotkey being broken. Same toast channel as a
    // successful switch — the press happened over another app, so this is the only place it can be said.
    // The mapping is deliberately left alone: TryLoad fails for an unreadable preset as well as an absent
    // one, and DeletePreset already takes the same care not to drop a hotkey whose file is still there.
    private void OnProfileSwitchFailed(string snapshot) =>
        Dispatcher.UIThread.Post(() => ProfileToast.Show($"Couldn't load preset “{snapshot}”"));

    // A restore that didn't happen. The override cue stays up (the override really is still active), so
    // this says why — the dangerous case is the artist believing they're back on their default while the
    // tablet is still running a preset (#734).
    private void OnProfileRestoreFailed(SettingsRestoreStatus status) =>
        Dispatcher.UIThread.Post(() => ProfileToast.Show(status switch
        {
            SettingsRestoreStatus.SourceUnavailable => "Couldn't restore: saved settings unreadable",
            SettingsRestoreStatus.Disconnected => "Couldn't restore: not connected",
            // The daemon changed under the restore, so the preset it would have ended belonged to the
            // daemon that has gone — saying "preset still active" about the new one would be wrong (#803).
            _ => "Couldn't restore your saved settings",
        }));

    private void OnMonitorCycled(string message) =>
        Dispatcher.UIThread.Post(() => ProfileToast.Show(message));

    /// <summary>Permit a real close — used by the tray's Quit. Without it, closing hides to the tray.</summary>
    public void AllowCloseForQuit() => _allowClose = true;

    // The client area is extended under the title bar (#titlebar), so the top strip is our own drag
    // handle: left-drag moves the window; double-click toggles maximize (standard title-bar behaviour).
    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnTitleBarDoubleTapped(object? sender, TappedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        ClampToWorkingArea();
        // Turn off Windows' pen/touch tap-feedback rings across the app — they're just visual noise on a
        // drawing-tablet UI. Per-window; gestures (e.g. right-click) still work.
        ShellPenFeedback.DisableFor(this);
    }

    /// <summary>
    /// Shrink + re-center the window when the default size doesn't fit the current screen's working
    /// area. A 1080p display at 150% scale is only 1280x720 logical — less than the default height
    /// once the taskbar and title bar are subtracted — so without this the top edge sits above the
    /// screen and the bottom hides under the taskbar (#274).
    /// </summary>
    private void ClampToWorkingArea()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;

        var scaling = screen.Scaling;
        var wa = screen.WorkingArea;        // physical pixels, excludes the taskbar
        var workW = wa.Width / scaling;     // logical (DIPs)
        var workH = wa.Height / scaling;

        // Headroom so the title bar and window edges stay comfortably reachable.
        var maxW = Math.Max(MinWidth, workW - 24);
        var maxH = Math.Max(MinHeight, workH - 48);

        var newW = Math.Min(Width, maxW);
        var newH = Math.Min(Height, maxH);
        if (newW >= Width && newH >= Height) return;   // already fits

        Width = newW;
        Height = newH;

        // Re-center within the working area (Position is in physical pixels).
        Position = new PixelPoint(
            wa.X + (int)((wa.Width - newW * scaling) / 2),
            wa.Y + (int)((wa.Height - newH * scaling) / 2));
    }

    /// <summary>Surface the window from a hidden/minimized state and focus it. Used by the tray click
    /// and by a second app instance asking the running one to come forward (#191).</summary>
    public void BringToFront() => BringToFront("app request");

    /// <summary>Version 7 test 7/8: until this time (UTC) a login start stays hidden: automatic requests to show the
    /// window (macOS reopen event, second-copy signal) are ignored and logged. Clicks in the pen menu still show it.</summary>
    public static DateTime HiddenStartUntil { get; set; } = DateTime.MinValue;
    private bool _showRequested;

    public static bool KeepHidden(bool byUser, bool background, DateTime now, DateTime until) => !byUser && background && now < until;

    public void BringToFront(string reason, bool byUser = true)
    {
        if (KeepHidden(byUser, Program.LaunchInBackground, DateTime.UtcNow, HiddenStartUntil))
        { AppLog.Info($"Window: kept hidden at login (ignored: {reason})."); return; }
        AppLog.Info($"Window: shown ({reason}).");
        _showRequested = true;
        // Version 7 test 9: Dock icon back before the window appears.
        MacActivationPolicy.SetRegular();
        // Version 7 test 8: first user-visible show after a login start assigns MainWindow (it was left
        // unset so Avalonia would not auto-show it at startup).
        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            && !ReferenceEquals(desktop.MainWindow, this))
            desktop.MainWindow = this;
        Show();
        WindowState = WindowState.Normal;
        Activate();
        // Quiet-login tablet VMs may have enumerated displays while MainWindow was unset (empty list).
        // Re-read after Show so Avalonia Screens has a platform handle (sync call can still be empty).
        Dispatcher.UIThread.Post(() => (DataContext as MainViewModel)?.RefreshTabletDisplays());
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_allowClose)
        {
            // Close minimizes to the tray instead of exiting; Quit is via the tray menu (#72).
            e.Cancel = true;
            if (!_trayHintShown)
            {
                _trayHintShown = true;
                AppSettings.Set("TrayHintShown", "true");
                _ = ShowTrayHintThenHide();
            }
            else
            {
                HideToTray("window closed");
            }
        }
        base.OnClosing(e);
    }

    /// <summary>Version 7 test 9: hide to the pen menu and drop the Dock icon (Accessory).</summary>
    private void HideToTray(string reason)
    {
        Hide();
        MacActivationPolicy.SetAccessory();
        AppLog.Info($"Window: hidden to pen menu ({reason}).");
    }

    private async Task ShowTrayHintThenHide()
    {
        await Dialogs.ShowMessageAsync(
            "Still running in the tray",
            "Tablet App for Mac is still running in the system tray. Click the tray icon to reopen it, " +
            "or right-click it and choose Quit to exit.",
            this);
        HideToTray("tray hint closed");
    }

    // ── Developer screenshots (#437) ──────────────────────────────────────────────────────────────

    // Capture the whole window UI (backdrop + page menu + the current page), not just the content host —
    // rendering the content alone drops the backdrop and leaves cards floating on transparent.
    private Control? RootVisual => Content as Control;

    // The nav-bar on-page capture control: render whatever page is currently shown.
    private void OnCapturePage(object? sender, RoutedEventArgs e)
    {
        var saved = RootVisual is { } v
            ? PageScreenshot.Render(v, RenderScaling, "page", DeveloperSettings.Instance.ScreenshotFormat)
            : 0;
        _ = FlashCaptureButton(saved > 0);
    }

    // Briefly confirm the capture on the button itself (no room for a status line in the nav).
    private async Task FlashCaptureButton(bool ok)
    {
        var original = CapturePageButton.Content;
        CapturePageButton.Content = FlashContent(ok);
        await Task.Delay(1100);
        CapturePageButton.Content = original;
    }

    // Icon + label ("Saved"/"Failed") built in code, since the content is swapped transiently (#551).
    private static Control FlashContent(bool ok)
    {
        var brushKey = ok ? "TextPrimaryBrush" : "ErrorBrush"; // #593: success is the check icon, not green
        var brush = Application.Current?.TryFindResource(brushKey, out var b) == true && b is IBrush ib
            ? ib : Brushes.Gray;
        var geo = Application.Current?.TryFindResource(ok ? "IconCheckCircle" : "IconAlertCircle",
            out var g) == true ? g as Geometry : null;
        return new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                new PathIcon { Data = geo, Width = 13, Height = 13, Foreground = brush,
                               VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
                new TextBlock { Text = ok ? "Saved" : "Failed", Foreground = brush,
                                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
            },
        };
    }

    /// <summary>Navigate every page in turn (Home, root nav leaves, each ADVANCED sub-tab), saving a
    /// screenshot of each into one fresh timestamped sub-folder, then restore the original page. Returns
    /// how many were saved and the folder they went to. Driven from the Developer tab's "Screenshot all
    /// pages" button (which owns the visual + the shell it navigates).</summary>
    public async Task<(int Count, string Directory)> CaptureAllPagesAsync()
    {
        if (DataContext is not MainViewModel vm || RootVisual is not { } visual)
            return (0, PageScreenshot.Directory());
        double scale = RenderScaling;
        var format = DeveloperSettings.Instance.ScreenshotFormat;
        var dir = PageScreenshot.CreateSweepDirectory();
        var originalPage = vm.CurrentPage;
        var originalTab = vm.Advanced.SelectedTab;
        // Settings' tab was not restored, so a sweep started from Theme ended on Dev (#690).
        var originalSettingsTab = vm.Settings.SelectedTab;
        int saved = 0;
        try
        {
            foreach (var (slug, navigate) in vm.ScreenshotTargets())
            {
                navigate();
                await Task.Delay(240); // let the page bind + lay out + a frame compose before rendering

                // Whatever is on screen may divide again — the tablet and pen pages' tab menus, and the
                // Theme and Dev subtab rails. Capture each, then put the selected one back. Found through
                // ITabbedContent in the visual tree rather than by testing the page VM's type: the old
                // check was "CurrentPage is TabletPageViewModel, then find a TabletDetailView", which the
                // Pen page passed and then failed — PenPageViewModel derives from TabletPageViewModel but
                // the view is PenDetailView — so Pen quietly got one shot of whichever tab was open (#690).
                if (this.GetVisualDescendants().OfType<ITabbedContent>().FirstOrDefault() is { } tabbed)
                {
                    var tabs = tabbed.VisibleTabButtons();
                    var selected = tabs.FirstOrDefault(t => t.IsChecked == true);
                    foreach (var tab in tabs)
                    {
                        tab.IsChecked = true;
                        await Task.Delay(200);
                        if (PageScreenshot.RenderTo(visual, scale, $"{slug}-{TabSlug(tab)}", dir, format) > 0) saved++;
                    }
                    if (selected != null) selected.IsChecked = true;
                    continue;
                }

                if (PageScreenshot.RenderTo(visual, scale, slug, dir, format) > 0) saved++;
            }
        }
        finally
        {
            vm.Advanced.SelectedTab = originalTab;
            vm.Settings.SelectedTab = originalSettingsTab;
            vm.CurrentPage = originalPage;
        }
        return (saved, dir);
    }

    private static string TabSlug(RadioButton tab) =>
        (tab.Content?.ToString() ?? "tab").ToLowerInvariant().Replace(' ', '-');
}
