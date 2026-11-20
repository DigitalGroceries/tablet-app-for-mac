using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Services;

/// <summary>
/// V29 #141 (Zak 19 Nov 2026: "add the option where the user can add a floating keyboard to the radial pen menu so they can type
/// using the pen"; 18:44 "use the in app keyboard code to create a new and more compact one"; 18:46 "It will be good if the
/// floating keyboard can be transparency adjusted"). A small keyboard that floats over the user's work. The radial menu's Keyboard
/// slice shows and hides it; its × hides it too. Tap a key with the pen (or click it with the mouse): the key goes to the app in
/// front. Like the radial menu, the window never takes the keyboard focus or brings this app forward. The app can't press keys in
/// other apps, so each key is written to a small file the driver reads (<see cref="FloatingKeyboardFiles"/>). Ctrl, Option, Cmd and
/// Shift apply to the next key. Drag the top bar to move it; the place is remembered. The typed keys are never logged.
/// </summary>
public sealed class FloatingKeyboard : IDisposable
{
    public const string TransparencyKey = "FloatingKeyboardTransparency";
    public const string PositionKey = "FloatingKeyboardPosition";
    public const string SizeKey = "FloatingKeyboardSize";
    /// <summary>Same range as the radial menu's transparency (above 60% the keys can't be read).</summary>
    public const int MinTransparency = RadialMenuDef.MinTransparency, MaxTransparency = RadialMenuDef.MaxTransparency;
    /// <summary>V29 (Zak 20 Nov ~09:30: "The default factory transparency for the floating keyboard should be 40%"; was 20).</summary>
    public const int DefaultTransparency = 40;
    /// <summary>V29 #162 (Zak 20 Nov ~09:10: "the keyboard is microscopic and you did not include a keyboard resizing slider"):
    /// Keyboard size in percent of the first keyboard (30-point keys). 150% unless changed; the slider, the number box and
    /// dragging the keyboard's bottom-right corner all set it.</summary>
    public const int MinSize = 60, MaxSize = 300, DefaultSize = 150;
    /// <summary>One key unit, in points.</summary>
    public const double Unit = 30;
    public const double KeyHeight = 30, Gap = 3, Pad = 8, BarHeight = 22;

    public static string OpenFlagPath { get; set; } = FloatingKeyboardFiles.OpenFlag;
    public static string ToggleRequestPath { get; set; } = FloatingKeyboardFiles.ToggleRequest;

    private static FileSystemWatcher? _toggleWatcher;
    private static long _lastToggle;

    /// <summary>V29 #160: watch for the driver's "pen button 1 was held" request (App start). The first look only notes the number
    /// already there, so an old request never opens the keyboard.</summary>
    public static void WatchHoldRequests()
    {
        try
        {
            _lastToggle = ReadToggle();
            var dir = Path.GetDirectoryName(ToggleRequestPath)!;
            Directory.CreateDirectory(dir);
            _toggleWatcher?.Dispose();
            _toggleWatcher = new FileSystemWatcher(dir, Path.GetFileName(ToggleRequestPath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            };
            _toggleWatcher.Changed += (_, _) => OnToggleFile();
            _toggleWatcher.Created += (_, _) => OnToggleFile();
            _toggleWatcher.Renamed += (_, _) => OnToggleFile();
            _toggleWatcher.EnableRaisingEvents = true;
        }
        catch (Exception e)
        {
            AppLog.Warn("Floating keyboard: can't watch for pen button 1 being held", e);
        }
    }

    private static long ReadToggle()
    {
        try { return File.Exists(ToggleRequestPath) ? FloatingKeyboardFiles.ParseToggle(File.ReadAllText(ToggleRequestPath)) : 0; }
        catch (IOException) { return 0; }
    }

    private static void OnToggleFile()
    {
        var n = ReadToggle();
        if (n == 0 || n == Interlocked.Exchange(ref _lastToggle, n)) return;
        Dispatcher.UIThread.Post(HandleHoldRequest);
    }

    /// <summary>Pen button 1 was held: show the keyboard just under the pointer (where the pen is), or hide it.</summary>
    internal static void HandleHoldRequest()
    {
        var p = MacDesktop.CursorLocation();
        double x = p?.X ?? 400, y = p?.Y ?? 300;
        AppLog.Info("Floating keyboard: pen button 1 was held.");
        Instance.Toggle(x, y + 24);
    }

    public static void StopWatchingHoldRequests()
    {
        _toggleWatcher?.Dispose();
        _toggleWatcher = null;
    }
    public static string CommandFilePath { get; set; } = FloatingKeyboardFiles.CommandFile;

    private static FloatingKeyboard? _instance;
    /// <summary>The one keyboard (made on first use). UI thread.</summary>
    public static FloatingKeyboard Instance => _instance ??= new FloatingKeyboard();
    internal static FloatingKeyboard? Current => _instance;

    /// <summary>How see-through the keyboard is, 0 (solid) to 60. Saved; an open keyboard follows at once.</summary>
    public static int Transparency
    {
        get => int.TryParse(AppSettings.Get(TransparencyKey), out var t) ? Math.Clamp(t, MinTransparency, MaxTransparency) : DefaultTransparency;
        set
        {
            var v = Math.Clamp(value, MinTransparency, MaxTransparency);
            AppSettings.Set(TransparencyKey, v.ToString(System.Globalization.CultureInfo.InvariantCulture));
            _instance?.ApplyLook();
        }
    }

    /// <summary>V29 #162: the keyboard's size in percent (60-300). Saved; an open keyboard follows at once.</summary>
    public static int SizePercent
    {
        get => int.TryParse(AppSettings.Get(SizeKey), out var t) ? Math.Clamp(t, MinSize, MaxSize) : DefaultSize;
        set
        {
            var v = Math.Clamp(value, MinSize, MaxSize);
            AppSettings.Set(SizeKey, v.ToString(System.Globalization.CultureInfo.InvariantCulture));
            _instance?.ApplySize();
            SizeChanged?.Invoke();
        }
    }

    /// <summary>Raised when the size changes (a corner drag), so the Pen page's slider follows.</summary>
    public static event Action? SizeChanged;

    /// <summary>V29: Restore factory settings puts the keyboard's look back (40% see-through, 150% size); where it was left is kept.</summary>
    public static void ResetToFactory()
    {
        AppSettings.Remove(TransparencyKey);
        AppSettings.Remove(SizeKey);
        _instance?.ApplyLook();
        _instance?.ApplySize();
        SizeChanged?.Invoke();
    }

    private Window? _window;
    private Border? _panel;
    private LayoutTransformControl? _scaler;
    private (double X, double Y)? _resizeStart;
    private double _resizeStartWidth;
    private int _resizeStartSize;
    private readonly List<(FloatingKey Key, Border Cap)> _caps = new();
    private (double X, double Y)? _dragStart;
    private (double X, double Y) _dragWindowStart;

    public bool Ctrl { get; private set; }
    public bool Shift { get; private set; }
    public bool Alt { get; private set; }
    public bool Cmd { get; private set; }
    /// <summary>The keyboard's Caps Lock: letters go as capitals until it is tapped again.</summary>
    public bool CapsLock { get; private set; }
    public bool IsShown => _window?.IsVisible == true;

    // ── V29 #171: the keys follow the Mac's selected keyboard layout and shape (read when shown, then every second while shown) ──
    /// <summary>Reads the Mac's selected layout; tests replace it.</summary>
    public static Func<IKeyLabels?> LayoutReader { get; set; } = MacKeyboardLayout.Current;
    private IKeyLabels? _labels;
    private (string? Id, KeyboardShape Shape)? _layoutShown;
    private KeyboardShape _shape = KeyboardShape.Ansi;
    private StackPanel? _rowsPanel;
    private TextBlock? _title;
    private DispatcherTimer? _layoutTimer;

    /// <summary>The layout the keys show now (null: the US labels).</summary>
    public IKeyLabels? Layout => _labels;
    public KeyboardShape Shape => _shape;

    /// <summary>Read the Mac's layout; when it changed (another input source, or a keyboard of the other shape), redraw the keys.</summary>
    internal void RefreshLayout()
    {
        IKeyLabels? labels;
        try { labels = LayoutReader(); }
        catch (Exception e) { AppLog.Warn("Floating keyboard: reading the keyboard layout failed", e); labels = null; }
        var shape = labels?.Shape ?? KeyboardShape.Ansi;
        if (_layoutShown == (labels?.Id, shape)) return;
        bool reshape = shape != _shape || _rowsPanel?.Children.Count == 0;
        _layoutShown = (labels?.Id, shape);
        _labels = labels;
        _shape = shape;
        if (reshape) BuildRows();
        if (_title != null) _title.Text = labels == null ? KeyboardTitle : $"{KeyboardTitle} · {labels.Name}";
        RefreshCaps();
        AppLog.Info($"Floating keyboard: keys follow the Mac's layout '{labels?.Name ?? "US (not read)"}' ({shape}).");
    }

    private static string KeyboardTitle => Localizer.Te("keyboard.title", "Keyboard");

    private void BuildRows()
    {
        if (_rowsPanel == null) return;
        _rowsPanel.Children.Clear();
        _caps.Clear();
        foreach (var row in FloatingKeyboardLayout.RowsFor(_shape))
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Height = KeyHeight };
            foreach (var key in row) line.Children.Add(MakeCap(key));
            _rowsPanel.Children.Add(line);
        }
    }
    /// <summary>The window (tests).</summary>
    internal Window? Window => _window;
    /// <summary>The keyboard's panel, the part that fades with the transparency (tests).</summary>
    internal Border? Panel => _panel;

    /// <summary>Show it if hidden, hide it if shown. <paramref name="nearX"/>/<paramref name="nearY"/>: where to put it the first time
    /// (global points, top centre of the keyboard): it always opens there, under the pen (Zak 20 Nov).</summary>
    public void Toggle(double nearX, double nearY)
    {
        if (IsShown) Hide();
        else Show(nearX, nearY);
    }

    public void Show(double nearX, double nearY)
    {
        try
        {
            EnsureWindow();
            RefreshLayout();
            ApplySize();
            var (w, _) = Size();
            // V29 (Zak 20 Nov, build 7: "the keyboard doesnt know where to show"; chose "Under the pen"): every time just under the
            // pen, on the pen's screen; above it when there is no room below; kept inside the screen (as the palette hint is placed).
            var (_, h) = Size();
            double x = nearX - w / 2, y = nearY;
            var screen = _window.Screens?.ScreenFromPoint(new PixelPoint((int)nearX, (int)nearY));
            if (screen != null)
            {
                var wa = screen.WorkingArea;
                if (y + h > wa.Bottom) y = nearY - 48 - h;                 // no room under the pen: above it
                x = Math.Clamp(x, wa.X + 4, Math.Max(wa.X + 4, wa.Right - w - 4));
                y = Math.Clamp(y, wa.Y + 4, Math.Max(wa.Y + 4, wa.Bottom - h - 4));
            }
            _window.Position = new PixelPoint((int)Math.Round(x), (int)Math.Round(y));
            ApplyLook();
            if (!_window.IsVisible) _window.Show();
            if (_window.TryGetPlatformHandle() is Avalonia.Platform.IMacOSTopLevelPlatformHandle mac && mac.NSWindow != IntPtr.Zero)
                MacDesktop.MakeOverlay(mac.NSWindow, clickThrough: false);
            WriteOpenFlag(true);
            _layoutTimer ??= new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => RefreshLayout());
            _layoutTimer.Start();
            AppLog.Info($"Floating keyboard shown at {x:0},{y:0} (transparency {Transparency}%, size {SizePercent}%).");
        }
        catch (Exception e)
        {
            AppLog.Warn("Floating keyboard: couldn't show it", e);
        }
    }

    public void Hide()
    {
        _layoutTimer?.Stop();
        CapsLock = false;
        ClearModifiers();
        WriteOpenFlag(false);
        if (_window?.IsVisible == true)
        {
            _window.Hide();
            AppLog.Info("Floating keyboard hidden.");
        }
    }

    /// <summary>A key was tapped. A modifier is switched on or off; any other key is sent with the modifiers that are on, which then
    /// switch off. Returns what was sent (null for a modifier). The text is not logged: it may be a password.</summary>
    public string? Tap(FloatingKey key)
    {
        if (key.IsModifier)
        {
            switch (key.Value)
            {
                case FloatingKeyboardLayout.Ctrl: Ctrl = !Ctrl; break;
                case FloatingKeyboardLayout.Shift: Shift = !Shift; break;
                case FloatingKeyboardLayout.Alt: Alt = !Alt; break;
                case FloatingKeyboardLayout.Cmd: Cmd = !Cmd; break;
                case FloatingKeyboardLayout.CapsLock: CapsLock = !CapsLock; break;
            }
            RefreshCaps();
            return null;
        }
        bool shift = Shift || (CapsLock && FloatingKeyboardLayout.IsLetter(key, _labels));
        var keys = FloatingKeyboardLayout.KeysFor(key.Value, Ctrl, shift, Alt, Cmd);
        SendKeys(keys);
        ClearModifiers();
        return keys;
    }

    private void ClearModifiers()
    {
        Ctrl = Shift = Alt = Cmd = false;
        RefreshCaps();
    }

    private static long _seq;
    private static readonly object FileGate = new();

    /// <summary>Hand a key to the driver: added to the key file, which keeps the last few (so quick taps are never lost).</summary>
    internal static void SendKeys(string keys)
    {
        try
        {
            lock (FileGate)
            {
                _seq = Math.Max(_seq + 1, DateTime.UtcNow.Ticks);
                var cmd = new RadialMenuCommand { Seq = _seq, Op = RadialMenuCommand.Key, Keys = keys };
                Directory.CreateDirectory(Path.GetDirectoryName(CommandFilePath)!);
                string? old = null;
                try { if (File.Exists(CommandFilePath)) old = File.ReadAllText(CommandFilePath); }
                catch (IOException) { }
                var lines = FloatingKeyboardFiles.ParseLines(old).Select(c => c.ToJson()).ToList();
                lines.Add(cmd.ToJson());
                if (lines.Count > FloatingKeyboardFiles.KeepLast) lines.RemoveRange(0, lines.Count - FloatingKeyboardFiles.KeepLast);
                var tmp = CommandFilePath + ".tmp";
                File.WriteAllText(tmp, string.Join("\n", lines) + "\n");
                File.Move(tmp, CommandFilePath, overwrite: true);
            }
        }
        catch (Exception e)
        {
            AppLog.Warn("Floating keyboard: couldn't pass a key to the driver", e);
        }
    }

    /// <summary>The driver only reads the key file while this flag is there. Removed when the keyboard hides and when the app starts
    /// or quits, so a keyboard that is gone never types.</summary>
    internal static void WriteOpenFlag(bool open)
    {
        try
        {
            if (open)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(OpenFlagPath)!);
                File.WriteAllText(OpenFlagPath, "open");
            }
            else if (File.Exists(OpenFlagPath)) File.Delete(OpenFlagPath);
        }
        catch (Exception e)
        {
            AppLog.Warn("Floating keyboard: couldn't update the open flag file", e);
        }
    }

    /// <summary>Width and height of the whole keyboard at 100%, in points.</summary>
    public static (double W, double H) BaseSize() =>
        (Pad * 2 + FloatingKeyboardLayout.RowUnits * Unit,
         Pad * 2 + BarHeight + Gap + FloatingKeyboardLayout.Rows.Count * KeyHeight);

    /// <summary>Width and height of the whole keyboard at its <see cref="SizePercent"/>, in points.</summary>
    public static (double W, double H) Size()
    {
        var (w, h) = BaseSize();
        double k = SizePercent / 100.0;
        return (Math.Round(w * k), Math.Round(h * k));
    }

    /// <summary>V29 #162: the size in percent a corner drag gives, from the width the keyboard had when the drag began.</summary>
    public static int SizeForDrag(int startPercent, double startWidth, double dx)
    {
        if (startWidth <= 0) return startPercent;
        var v = (int)Math.Round(startPercent * (startWidth + dx) / startWidth);
        return Math.Clamp(v, MinSize, MaxSize);
    }

    private void ApplySize()
    {
        if (_window == null || _scaler == null) return;
        double k = SizePercent / 100.0;
        _scaler.LayoutTransform = new ScaleTransform(k, k);
        var (w, h) = Size();
        _window.Width = w;
        _window.Height = h;
    }

    private void ApplyLook()
    {
        if (_panel != null) _panel.Opacity = RadialMenuDef.OpacityFor(Transparency);
    }

    private void EnsureWindow()
    {
        if (_window != null) return;
        var rows = _rowsPanel = new StackPanel { Spacing = 0 };
        BuildRows();
        var title = _title = new TextBlock
        {
            Text = KeyboardTitle, FontSize = 12, Foreground = Brushes.Gainsboro,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0),
        };
        var close = new Border
        {
            Width = BarHeight, Height = BarHeight, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(Color.Parse("#3A3A3A")),
            HorizontalAlignment = HorizontalAlignment.Right, Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock { Text = "×", FontSize = 15, Foreground = KeyText, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        close.PointerPressed += (_, e) => { e.Handled = true; Hide(); };
        var grip = new Border
        {
            Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.SizeAll),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { new TextBlock { Text = "⠿", FontSize = 14, Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center }, title },
            },
        };
        grip.PointerPressed += OnGripPressed;
        grip.PointerMoved += OnGripMoved;
        grip.PointerReleased += OnGripReleased;
        var bar = new Grid { Height = BarHeight, ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, Gap) };
        bar.Children.Add(grip);
        Grid.SetColumn(close, 1);
        bar.Children.Add(close);
        _panel = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#1E1E1E")), CornerRadius = new CornerRadius(10), Padding = new Thickness(Pad),
            BorderBrush = new SolidColorBrush(Color.Parse("#4A4A4A")), BorderThickness = new Thickness(1),
            Child = new StackPanel { Children = { bar, rows } },
        };
        // V29 #162 (Zak 20 Nov ~09:12: "Let a corner dragging resize the floating keyboard too"): the bottom-right corner.
        var corner = new Border
        {
            Width = 16, Height = 16, Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom, Cursor = new Cursor(StandardCursorType.BottomRightCorner),
            Child = new TextBlock { Text = "◢", FontSize = 11, Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Right,
                                    VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 3, 1) },
        };
        ToolTip.SetTip(corner, Localizer.Te("keyboard.resize_tip", "Drag to make the keyboard bigger or smaller"));
        corner.PointerPressed += OnCornerPressed;
        corner.PointerMoved += OnCornerMoved;
        corner.PointerReleased += OnCornerReleased;
        _scaler = new LayoutTransformControl { Child = _panel };
        var root = new Grid { Children = { _scaler, corner } };
        _window = new Window
        {
            WindowDecorations = WindowDecorations.None,
            SizeToContent = SizeToContent.Manual,
            Topmost = true,
            ShowActivated = false,
            ShowInTaskbar = false,
            CanResize = false,
            Background = Brushes.Transparent,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
            Focusable = false,
            Content = root,
            Title = "Keyboard",
        };
        RefreshCaps();
    }

    private Border MakeCap(FloatingKey key)
    {
        var cap = new Border
        {
            Width = key.Units * Unit - Gap, Height = KeyHeight - Gap, Margin = new Thickness(0, 0, Gap, 0),
            CornerRadius = new CornerRadius(5), Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock
            {
                Text = FloatingKeyboardLayout.LabelFor(key, Shift, CapsLock, _labels), FontSize = key.Display.Length > 2 ? 11 : 13, Foreground = KeyText,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
        cap.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            Tap(key);
            if (!key.IsModifier) Flash(cap);
        };
        _caps.Add((key, cap));
        return cap;
    }

    private static readonly IBrush KeyText = new SolidColorBrush(Color.Parse("#E4E4E4"));   // the app's lightest text grey (R6)
    private static readonly IBrush KeyBrush = new SolidColorBrush(Color.Parse("#3A3A3A"));
    private static readonly IBrush OnBrush = new SolidColorBrush(Color.Parse("#2F6FD6"));
    private static readonly IBrush FlashBrush = new SolidColorBrush(Color.Parse("#6A6A6A"));

    private void RefreshCaps()
    {
        foreach (var (key, cap) in _caps)
        {
            cap.Background = key.IsModifier && IsOn(key.Value) ? OnBrush : KeyBrush;
            if (!key.IsModifier && cap.Child is TextBlock label)
            {
                label.Text = FloatingKeyboardLayout.LabelFor(key, Shift, CapsLock, _labels);
                label.FontSize = label.Text.Length > 2 ? 11 : 13;
            }
        }
    }

    /// <summary>Is this modifier on (tests and the key colours)?</summary>
    public bool IsOn(string modifier) => modifier switch
    {
        FloatingKeyboardLayout.Ctrl => Ctrl,
        FloatingKeyboardLayout.Shift => Shift,
        FloatingKeyboardLayout.Alt => Alt,
        FloatingKeyboardLayout.Cmd => Cmd,
        FloatingKeyboardLayout.CapsLock => CapsLock,
        _ => false,
    };

    private static void Flash(Border cap)
    {
        cap.Background = FlashBrush;
        DispatcherTimer.RunOnce(() => cap.Background = KeyBrush, TimeSpan.FromMilliseconds(120));
    }

    private (double X, double Y)? Pointer(PointerEventArgs e)
    {
        if (MacDesktop.CursorLocation() is { } p) return (p.X, p.Y);
        if (_window == null) return null;
        var s = _window.PointToScreen(e.GetPosition(_window));
        return (s.X, s.Y);
    }

    private void OnGripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_window == null || Pointer(e) is not { } p) return;
        _dragStart = p;
        _dragWindowStart = (_window.Position.X, _window.Position.Y);
        e.Pointer.Capture(sender as IInputElement);
        e.Handled = true;
    }

    private void OnGripMoved(object? sender, PointerEventArgs e)
    {
        if (_dragStart is not { } start || _window == null || Pointer(e) is not { } p) return;
        _window.Position = new PixelPoint((int)Math.Round(_dragWindowStart.X + p.X - start.X), (int)Math.Round(_dragWindowStart.Y + p.Y - start.Y));
        e.Handled = true;
    }

    private void OnGripReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragStart == null || _window == null) return;
        _dragStart = null;
        e.Pointer.Capture(null);
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        AppSettings.Set(PositionKey, _window.Position.X.ToString(inv) + "," + _window.Position.Y.ToString(inv));
        e.Handled = true;
    }

    private void OnCornerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_window == null || Pointer(e) is not { } p) return;
        _resizeStart = p;
        _resizeStartWidth = _window.Width;
        _resizeStartSize = SizePercent;
        e.Pointer.Capture(sender as IInputElement);
        e.Handled = true;
    }

    private void OnCornerMoved(object? sender, PointerEventArgs e)
    {
        if (_resizeStart is not { } start || _window == null || Pointer(e) is not { } p) return;
        // Width decides (the keyboard keeps its shape); the top-left corner stays where it is.
        var v = SizeForDrag(_resizeStartSize, _resizeStartWidth, p.X - start.X);
        if (v != SizePercent) AppSettings.Set(SizeKey, v.ToString(System.Globalization.CultureInfo.InvariantCulture));
        ApplySize();
        e.Handled = true;
    }

    private void OnCornerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_resizeStart == null) return;
        _resizeStart = null;
        e.Pointer.Capture(null);
        SizePercent = SizePercent;   // saves it and lets the Pen page's slider follow
        AppLog.Info($"Floating keyboard resized by its corner to {SizePercent}%.");
        e.Handled = true;
    }

    public void Dispose()
    {
        WriteOpenFlag(false);
        _window?.Close();
        _window = null;
        if (ReferenceEquals(_instance, this)) _instance = null;
    }
}
