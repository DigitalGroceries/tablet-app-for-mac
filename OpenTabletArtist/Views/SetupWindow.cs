using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.Views;

/// <summary>Version 7: hand-held first-launch setup. One big numbered step at a time, each with a picture of
/// the exact switch. Checks every second and moves on with a green tick when a step is done. Shown again by
/// <see cref="ShowIfNeeded"/> if a permission is later lost. Wacom leftovers show once per launch; closing with permissions OK does not reopen every 30 s.</summary>
public sealed class SetupWindow : Window
{
    public enum Step { Wacom, ReadPen, MovePointer, Restart, Done }

    /// <summary>The steps still to do, in order. Pure, unit-tested.</summary>
    public static List<Step> Plan(bool wacom, bool pen, bool pointer, bool grantedThisRun)
    {
        // Permissions first: a leftover Wacom Applications folder must not block Input Monitoring /
        // Accessibility, and must not sit in front of System Settings while those are granted.
        var s = new List<Step>();
        if (!pen) s.Add(Step.ReadPen);
        if (!pointer) s.Add(Step.MovePointer);
        if (wacom) s.Add(Step.Wacom);
        if (grantedThisRun || !pen || !pointer) s.Add(Step.Restart);
        s.Add(Step.Done);
        return s;
    }

    /// <summary>After this long with the settings open and still no grant, the row in the list is probably
    /// an old entry from an earlier version: say how to switch it off and on again.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(15);

    static SetupWindow? _open;
    readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly bool _penAtStart, _pointerAtStart;
    int _total => _steps.Count;
    readonly List<Step> _steps;
    Step _shown = (Step)(-1);
    DateTime? _openedAt;
    readonly StackPanel _body = new() { Spacing = 16 };

    static IBrush B(string hex) => new SolidColorBrush(Color.Parse(hex));

    public SetupWindow()
    {
        Title = Localizer.T("setup.title");
        Width = 640; SizeToContent = SizeToContent.Height; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = B("#1E1E1E");
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        _penAtStart = MacAccessibility.CanReadPen();
        _pointerAtStart = MacAccessibility.IsTrusted();
        _steps = Plan(WacomBlocksSetup(), _penAtStart, _pointerAtStart, false);
        _steps.Remove(Step.Done);
        // Ask macOS first, so the app is already in each list and the user only flips one switch.
        if (!_penAtStart && !_askedReadPenThisRun)
        {
            _askedReadPenThisRun = true;
            AppLog.Info("Permission: asked macOS for Input Monitoring (once per run).");
            MacAccessibility.RequestReadPen();
        }
        Content = new Border { Padding = new Thickness(32), Child = _body };
        Refresh();
        _poll.Tick += (_, _) => Refresh();
        _poll.Start();
        Closed += (_, _) =>
        {
            _poll.Stop();
            // Permissions OK + close = user put Wacom leftovers aside for now. Don't reopen every 30 s.
            if (MacAccessibility.CanReadPen() && MacAccessibility.IsTrusted())
                _wacomDismissedThisSession = true;
            _open = null;
        };
    }

    void Refresh()
    {
        bool pen = MacAccessibility.CanReadPen(), pointer = MacAccessibility.IsTrusted();
        var plan = Plan(WacomBlocksSetup(), pen, pointer, (pen && !_penAtStart) || (pointer && !_pointerAtStart));
        var step = plan[0];
        if (step != _shown) { _shown = step; _openedAt = null; Show(step, plan.Count - 1); }
        else if (_openedAt is { } t && DateTime.Now - t > StaleAfter && _staleHint is { IsVisible: false }) _staleHint.IsVisible = true;
    }

    TextBlock? _staleHint;

    /// <summary>Version 7 test 3: steps keep the numbers they were given when setup opened, so the counter
    /// never jumps (it showed "1 of 3", then "1 of 2"). A step that wasn't planned is added at the end. Unit-tested.</summary>
    public static int StepNumber(List<Step> steps, Step step)
    {
        if (step == Step.Done) return steps.Count;
        if (!steps.Contains(step)) steps.Add(step);
        return steps.IndexOf(step) + 1;
    }

    void Show(Step step, int left)
    {
        _body.Children.Clear();
        _staleHint = null;
        int number = StepNumber(_steps, step);
        TextBlock Big(string t) => new() { Text = t, FontSize = 26, FontWeight = FontWeight.SemiBold, Foreground = B("#E4E4E4"), TextWrapping = TextWrapping.Wrap };
        TextBlock Plain(string t) => new() { Text = t, FontSize = 16, Foreground = B("#D0D0D0"), TextWrapping = TextWrapping.Wrap, LineHeight = 24 };
        Button Main(string t, Action a) { var b = new Button { Content = t, FontSize = 16, Padding = new Thickness(18, 10), Background = B("#3D6BE0"), Foreground = B("#E4E4E4") }; b.Click += (_, _) => a(); return b; }
        Button Second(string t, Action a) { var b = new Button { Content = t, FontSize = 14, Padding = new Thickness(14, 8) }; b.Click += (_, _) => a(); return b; }
        void Header(string title)
        {
            if (step != Step.Done && _total > 1)
                _body.Children.Add(new TextBlock { Text = Localizer.Tf("setup.step_of", ("number", number), ("total", _total)), FontSize = 14, Foreground = B("#8FB0FF") });
            _body.Children.Add(Big(title));
        }
        void Picture(string asset)
        {
            try
            {
                using var s = AssetLoader.Open(new Uri($"avares://OpenTabletArtist/Assets/{asset}"));
                _body.Children.Add(new Border { CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = new Image { Source = new Bitmap(s), MaxHeight = 300 } });
            }
            catch (Exception) { /* picture missing: the words still say what to do */ }
        }
        void Row(params Control[] c) { var r = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 }; r.Children.AddRange(c); _body.Children.Add(r); }
        void Stale(string text) { _staleHint = new TextBlock { Text = text, IsVisible = false, FontSize = 15, Foreground = B("#FFC266"), TextWrapping = TextWrapping.Wrap }; _body.Children.Add(_staleHint); }

        switch (step)
        {
            case Step.Wacom:
                Header(Localizer.T("setup.wacom.header"));
                _body.Children.Add(Plain(Localizer.T("setup.wacom.warning")));
                bool hasUtility = WacomDriverCheck.HasUtilityOnThisMac();
                var removable = WacomDriverCheck.RemovableOnThisMac();
                _body.Children.Add(Plain(Localizer.T(hasUtility ? "setup.wacom.howto" : "setup.wacom.howto_no_utility")));
                var wnote = new TextBlock { FontSize = 14, Foreground = B("#FFC266"), TextWrapping = TextWrapping.Wrap, IsVisible = false };
                void DoRemoveLeftovers()
                {
                    var result = WacomDriverCheck.RemoveRemovableOnThisMac();
                    if (result.Failed.Count > 0)
                    {
                        wnote.Text = Localizer.T("setup.wacom.remove_failed");
                        wnote.IsVisible = true;
                        return;
                    }
                    // Force the next poll to rebuild; when Find is clear the wizard advances.
                    _shown = (Step)(-1);
                    Refresh();
                }
                void OpenGuide() => PlatformShell.OpenUrl(WacomDriverCheck.RemovalHelp);
                if (hasUtility)
                {
                    var secondary = removable.Count > 0
                        ? Second(Localizer.T("setup.wacom.remove_leftovers"), DoRemoveLeftovers)
                        : Second(Localizer.T("setup.wacom.open_guide"), OpenGuide);
                    Row(Main(Localizer.T("setup.wacom.open_utility"), () => { if (!OpenWacom()) { wnote.Text = Localizer.T("setup.wacom.no_uninstaller"); wnote.IsVisible = true; } }), secondary);
                }
                else if (removable.Count > 0)
                {
                    Row(Main(Localizer.T("setup.wacom.remove_leftovers"), DoRemoveLeftovers), Second(Localizer.T("setup.wacom.open_guide"), OpenGuide));
                }
                else
                {
                    Row(Main(Localizer.T("setup.wacom.open_guide"), OpenGuide));
                }
                _body.Children.Add(wnote);
                break;
            case Step.ReadPen:
                Header(Localizer.T("setup.pen.header"));
                _body.Children.Add(Plain(Localizer.T("setup.pen.body")));
                Picture("setup-read-pen.png");
                Row(Main(Localizer.T("setup.pen.open"), () => { _openedAt = DateTime.Now; PlatformShell.OpenInputMonitoringSettings(); }));
                Stale(Localizer.T("setup.pen.stale"));
                break;
            case Step.MovePointer:
                Header(Localizer.T("setup.pointer.header"));
                _body.Children.Add(Plain(Localizer.Tf("setup.pointer.body", ("pane", MacAccessibility.PointerPaneName()))));
                Picture("setup-move-pointer.png");
                // Version 15 (Zak, double prompt): only open System Settings; no macOS permission box on top.
                Row(Main(Localizer.Tf("setup.pointer.open", ("pane", MacAccessibility.PointerPaneName())), () => { _openedAt = DateTime.Now; PlatformShell.OpenAccessibilitySettings(); }));
                Stale(Localizer.T("setup.pointer.stale"));
                break;
            case Step.Restart:
                Header(Localizer.T("setup.restart.header"));
                _body.Children.Add(Plain(Localizer.T("setup.restart.body")));
                Row(Main(Localizer.T("setup.restart.button"), Restart));
                break;
            default:
                _body.Children.Add(Big(Localizer.T("setup.done.header")));
                _body.Children.Add(Plain(Localizer.T("setup.done.body")));
                if (WacomDriverCheck.LeftoversOnThisMac().Count > 0)
                {
                    _body.Children.Add(new TextBlock { Text = Localizer.T("setup.wacom.leftover"), FontSize = 14, Foreground = B("#9A9A9A"), TextWrapping = TextWrapping.Wrap });
                    Row(Second(Localizer.T("setup.wacom.open_guide"), () => PlatformShell.OpenUrl(WacomDriverCheck.RemovalHelp)));
                }
                Row(Main(Localizer.T("app.close"), Close));
                break;
        }
    }

    /// <summary>Opens Wacom's own utility (it has the uninstaller), or Wacom's removal page.</summary>
    /// <returns>False when the uninstaller wasn't found (Wacom's guide was opened instead).</returns>
    static bool OpenWacom()
    {
        try
        {
            foreach (var w in new[] { WacomDriverCheck.UtilityApp, WacomDriverCheck.UtilityAppAlt })
            {
                if (System.IO.Directory.Exists(w))
                {
                    Process.Start(new ProcessStartInfo("open", $"\"{w}\"") { UseShellExecute = false });
                    return true;
                }
            }
            PlatformShell.OpenUrl(WacomDriverCheck.RemovalHelp);
        }
        catch (Exception ex) { AppLog.Warn("Couldn't open the Wacom utility.", ex); }
        return false;
    }

    /// <summary>Relaunches this app cleanly: a small helper waits for us to quit normally, then reopens the
    /// bundle. Only this app quits; nothing else is touched.</summary>
    static void Restart()
    {
        var exe = Environment.ProcessPath ?? "";
        var i = exe.IndexOf(".app/", StringComparison.Ordinal);
        if (i > 0)
            try
            {
                var psi = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
                psi.ArgumentList.Add("-c");
                psi.ArgumentList.Add(RestartScript(Environment.ProcessId));
                psi.ArgumentList.Add(exe[..(i + 4)]);
                Process.Start(psi);
            }
            catch (Exception ex) { AppLog.Warn("Couldn't restart the app.", ex); }
        if (AppTray.Current is { } tray) tray.QuitAndStopDaemon();
        else (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }

    /// <summary>Waits for this app and our daemon to quit normally (up to 30 s; never kills anything), then
    /// opens the app again, which starts a fresh daemon with the new permissions. Pure, unit-tested.</summary>
    public static string RestartScript(int pid) =>
        $"n=0; while {{ kill -0 {pid} 2>/dev/null || pgrep -x OpenTabletDriver.Daemon >/dev/null; }} && [ $n -lt 60 ]; do sleep 0.5; n=$((n+1)); done; sleep 1; open \"$0\"";

    /// <summary>True when setup should open. Pure, unit-tested. After the user closes setup with both
    /// permissions already granted, Wacom leftovers alone do not reopen it until the next app launch.</summary>
    public static bool ShouldShow(bool penOk, bool pointerOk, bool wacomPresent, bool wacomDismissedThisSession)
    {
        if (!penOk || !pointerOk) return true;
        if (!wacomPresent) return false;
        return !wacomDismissedThisSession;
    }

    static bool _wacomDismissedThisSession;

    /// <summary>Version 14: macOS's own Input Monitoring box is asked for once per run, even when setup is
    /// closed and opened again (each new setup window used to ask again).</summary>
    static bool _askedReadPenThisRun;

    /// <summary>Version 14: only a live Wacom driver opens setup (Utility app, preference pane, kexts, launch
    /// files, running processes). An empty /Applications/Wacom Tablet(.localized) folder is a harmless leftover
    /// that the app usually can't delete (it belongs to the system), and it used to bring setup back at every
    /// start.</summary>
    public static bool WacomBlocksSetup() => WacomDriverCheck.LiveOnThisMac().Count > 0;

    /// <summary>Pure form of <see cref="WacomBlocksSetup()"/> for tests.</summary>
    public static bool WacomBlocksSetup(Func<string, bool> exists, Func<string, IEnumerable<string>> list, IEnumerable<string> running)
        => WacomDriverCheck.ClassifyLive(exists, list, running).Count > 0;

    /// <summary>Shows setup when a permission is missing or the Wacom driver is found (first launch, or later).
    /// Closing with permissions already OK suppresses Wacom-only reopens for this session.</summary>
    public static void ShowIfNeeded()
    {
        if (!OperatingSystem.IsMacOS() || _open != null) return;
        bool penOk = MacAccessibility.CanReadPen(), pointerOk = MacAccessibility.IsTrusted();
        bool wacom = WacomBlocksSetup();
        if (!wacom && penOk && pointerOk) _wacomDismissedThisSession = false;
        if (!ShouldShow(penOk, pointerOk, wacom, _wacomDismissedThisSession)) return;
        _open = new SetupWindow();
        OpenTabletArtist.Helpers.InWindowSheet.Show(_open);
    }
}
