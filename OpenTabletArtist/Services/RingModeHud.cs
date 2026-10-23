using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace OpenTabletArtist.Services;

/// <summary>
/// Wacom-like on-screen display for Touch Ring modes. The ring plugin (running inside the daemon) writes
/// the new mode to a small file each time the ring button is pressed; this watches that file and shows the
/// mode's name and position ("● ○ ○") for a moment, without taking focus from the drawing app.
/// Off when the "RingModeHud" app setting is "0".
/// </summary>
public sealed class RingModeHud : IDisposable
{
    public const string SettingKey = "RingModeHud";

    /// <summary>Same place the plugin writes (RingModeState.StateFilePath): the per-user temp folder,
    /// which the daemon inherits from the app that starts it.</summary>
    public static string StateFilePath { get; } =
        Path.Combine(Path.GetTempPath(), "OpenTabletArtist", "ring-mode");

    public static bool Enabled
    {
        get => AppSettings.Get(SettingKey) != "0";
        set => AppSettings.Set(SettingKey, value ? "1" : "0");
    }

    private FileSystemWatcher? _watcher;
    private Window? _window;
    private TextBlock? _name;
    private TextBlock? _dots;
    private DispatcherTimer? _hideTimer;
    private string _lastContent = "";

    public void Start()
    {
        try
        {
            var dir = Path.GetDirectoryName(StateFilePath)!;
            Directory.CreateDirectory(dir);
            _watcher = new FileSystemWatcher(dir, Path.GetFileName(StateFilePath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            };
            _watcher.Changed += (_, _) => OnFileChanged();
            _watcher.Created += (_, _) => OnFileChanged();
            _watcher.Renamed += (_, _) => OnFileChanged();
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception e)
        {
            AppLog.Warn("Ring mode display unavailable", e);
        }
    }

    private void OnFileChanged()
    {
        string content;
        try { content = File.ReadAllText(StateFilePath); }
        catch (IOException) { return; }          // mid-rename; the next event has it
        catch (UnauthorizedAccessException) { return; }
        if (!TryParse(content, out var mode, out var count, out var name)) return;
        // Watcher events arrive on thread-pool threads; the de-dupe reads the UI-thread timer and shared
        // state, so do it on the UI thread (build G; flagged in the E review).
        Dispatcher.UIThread.Post(() =>
        {
            // Every press writes the file, so the same mode twice in a row is still a press (wrap with one
            // mode) — but a watcher can fire twice for one write, so ignore exact repeats within the timer.
            if (content == _lastContent && _hideTimer?.IsEnabled == true) return;
            _lastContent = content;
            if (!Enabled) return;
            Show(mode, count, name);
        });
    }

    /// <summary>"2 3\nZoom\n" → mode 2 of 3 named Zoom.</summary>
    public static bool TryParse(string content, out int mode, out int count, out string name)
    {
        mode = count = 0;
        name = "";
        var lines = content.Replace("\r", "").Split('\n');
        var nums = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (nums.Length < 2 || !int.TryParse(nums[0], out mode) || !int.TryParse(nums[1], out count)) return false;
        if (count < 1 || mode < 1 || mode > count) return false;
        name = lines.Length > 1 && lines[1].Trim().Length > 0 ? lines[1].Trim() : $"Mode {mode}";
        return true;
    }

    /// <summary>"● ○ ○" for mode 1 of 3.</summary>
    public static string Dots(int mode, int count)
    {
        var parts = new string[count];
        for (int i = 0; i < count; i++) parts[i] = i == mode - 1 ? "●" : "○";
        return string.Join("  ", parts);
    }

    // Build H: shown for 2.5 s, then faded out over 0.3 s. Content opacity 0.4 ("60% transparent": the dark
    // panel and its text are both drawn at 40%).
    private const double ContentOpacity = 0.4;
    private static readonly TimeSpan ShowFor = TimeSpan.FromMilliseconds(2500);
    private const int FadeSteps = 10;
    private static readonly TimeSpan FadeStep = TimeSpan.FromMilliseconds(30);
    private Border? _panel;
    private DispatcherTimer? _fadeTimer;
    private int _fadeLeft;
    private bool _nativeSetUp;

    /// <summary>Build J: show a message in the popup (e.g. the smooth-rotate watchdog's), even when mode
    /// popups are turned off — it's about input that was stuck. Safe from any thread.</summary>
    public void ShowMessage(string message) =>
        Dispatcher.UIThread.Post(() =>
        {
            _lastContent = "";
            Show(0, 0, message);
        });

    private void Show(int mode, int count, string name)
    {
        try
        {
            EnsureWindow();
            _name!.Text = name;
            _dots!.Text = Dots(mode, count);

            // Where: centred over the frontmost app's window (Photoshop) when it's on the cursor's display,
            // else centred on the display holding the cursor (the pen moves it), else the primary display.
            // On macOS Avalonia's screen and window coordinates are global points with a top-left origin —
            // the same space as CGEventGetLocation and CGWindowListCopyWindowInfo bounds.
            var (target, where) = PickTarget();

            // Size: a third of the target's width (at least 360 pt), half as tall; the text scales with it.
            double w = Math.Max(360, target.Width / 3), h = w * 0.5;
            _window!.Width = w;
            _window.Height = h;
            _window.Position = new PixelPoint((int)(target.X + (target.Width - w) / 2), (int)(target.Y + (target.Height - h) / 2));

            _fadeTimer!.Stop();
            _panel!.Opacity = ContentOpacity;
            if (!_window.IsVisible) _window.Show();
            SetUpNativeWindow();
            _hideTimer!.Stop();
            _hideTimer.Start();
            AppLog.Info($"Ring mode popup: \"{name}\" ({mode}/{count}) on {where}, {w:0}x{h:0} pt at {_window.Position.X},{_window.Position.Y}.");
        }
        catch (Exception e)
        {
            AppLog.Warn("Ring mode popup failed", e);
        }
    }

    private (Rect Target, string Where) PickTarget()
    {
        var screens = _window!.Screens;
        var cursor = MacDesktop.CursorLocation();
        var cursorScreen = cursor is { } c ? screens.ScreenFromPoint(new PixelPoint((int)c.X, (int)c.Y)) : null;
        var screen = cursorScreen ?? screens.Primary ?? (screens.All.Count > 0 ? screens.All[0] : null);
        if (screen == null) return (new Rect(0, 0, 1200, 800), "no screen info");
        var area = new Rect(screen.WorkingArea.X, screen.WorkingArea.Y, screen.WorkingArea.Width, screen.WorkingArea.Height);
        var whereScreen = cursorScreen != null ? "the cursor's display" : "the primary display";

        if (MacDesktop.FrontmostWindowBounds() is { } win && win.Width >= 200 && win.Height >= 150)
        {
            var visible = win.Intersect(area);
            if (visible.Width >= 200 && visible.Height >= 150)
                return (visible, "the frontmost window");
        }
        return (area, whereScreen);
    }

    /// <summary>Make the popup click-through and allowed over full-screen apps and on every Space (macOS).
    /// Done once, after the native window exists. Never activates the app.</summary>
    private void SetUpNativeWindow()
    {
        if (_nativeSetUp || !OperatingSystem.IsMacOS()) return;
        _nativeSetUp = true;
        try
        {
            if (_window!.TryGetPlatformHandle() is Avalonia.Platform.IMacOSTopLevelPlatformHandle mac && mac.NSWindow != IntPtr.Zero)
                MacDesktop.MakeOverlay(mac.NSWindow);
        }
        catch (Exception e)
        {
            AppLog.Warn("Ring mode popup: couldn't make the window click-through", e);
        }
    }

    private void EnsureWindow()
    {
        if (_window != null) return;
        _name = new TextBlock
        {
            FontSize = 30,
            FontWeight = FontWeight.SemiBold,
            Foreground = OpenTabletArtist.Services.UiColors.SoftWhiteBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _dots = new TextBlock
        {
            FontSize = 16,
            Foreground = OpenTabletArtist.Services.UiColors.SoftWhiteBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _panel = new Border
        {
            CornerRadius = new CornerRadius(18),
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
            Opacity = ContentOpacity,
            IsHitTestVisible = false,
            // Viewbox scales the text with the panel, which is sized to a third of the target.
            Child = new Viewbox
            {
                Stretch = Stretch.Uniform,
                Margin = new Thickness(18, 10),
                Child = new StackPanel
                {
                    Spacing = 4,
                    Margin = new Thickness(12, 6),
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "TOUCH RING",
                            FontSize = 9,
                            Foreground = OpenTabletArtist.Services.UiColors.SoftWhiteBrush,
                            HorizontalAlignment = HorizontalAlignment.Center,
                        },
                        _name,
                        _dots,
                    },
                },
            },
        };
        _window = new Window
        {
            WindowDecorations = WindowDecorations.None,
            Topmost = true,
            ShowActivated = false,
            ShowInTaskbar = false,
            CanResize = false,
            SizeToContent = SizeToContent.Manual,
            Background = Brushes.Transparent,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
            Focusable = false,
            IsHitTestVisible = false,
            Title = "Ring mode",
            Content = _panel,
        };
        _hideTimer = new DispatcherTimer { Interval = ShowFor };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            _fadeLeft = FadeSteps;
            _fadeTimer!.Start();
        };
        _fadeTimer = new DispatcherTimer { Interval = FadeStep };
        _fadeTimer.Tick += (_, _) =>
        {
            _fadeLeft--;
            _panel!.Opacity = ContentOpacity * Math.Max(0, _fadeLeft) / FadeSteps;
            if (_fadeLeft > 0) return;
            _fadeTimer.Stop();
            _window?.Hide();
            _panel.Opacity = ContentOpacity;
        };
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _hideTimer?.Stop();
        _fadeTimer?.Stop();
        _window?.Close();
    }
}
