using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    /// <summary>Same range as the radial menu's transparency (above 60% the keys can't be read).</summary>
    public const int MinTransparency = RadialMenuDef.MinTransparency, MaxTransparency = RadialMenuDef.MaxTransparency;
    public const int DefaultTransparency = 20;
    /// <summary>One key unit, in points.</summary>
    public const double Unit = 30;
    public const double KeyHeight = 30, Gap = 3, Pad = 8, BarHeight = 22;

    public static string OpenFlagPath { get; set; } = FloatingKeyboardFiles.OpenFlag;
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

    private Window? _window;
    private Border? _panel;
    private readonly List<(FloatingKey Key, Border Cap)> _caps = new();
    private (double X, double Y)? _dragStart;
    private (double X, double Y) _dragWindowStart;

    public bool Ctrl { get; private set; }
    public bool Shift { get; private set; }
    public bool Alt { get; private set; }
    public bool Cmd { get; private set; }
    public bool IsShown => _window?.IsVisible == true;
    /// <summary>The window (tests).</summary>
    internal Window? Window => _window;

    /// <summary>Show it if hidden, hide it if shown. <paramref name="nearX"/>/<paramref name="nearY"/>: where to put it the first time
    /// (global points, top centre of the keyboard); after a drag it opens where it was left.</summary>
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
            var (w, h) = Size();
            _window!.Width = w;
            _window.Height = h;
            var (x, y) = SavedPosition() ?? (nearX - w / 2, nearY);
            _window.Position = new PixelPoint((int)Math.Round(x), (int)Math.Round(y));
            ApplyLook();
            if (!_window.IsVisible) _window.Show();
            if (_window.TryGetPlatformHandle() is Avalonia.Platform.IMacOSTopLevelPlatformHandle mac && mac.NSWindow != IntPtr.Zero)
                MacDesktop.MakeOverlay(mac.NSWindow, clickThrough: false);
            WriteOpenFlag(true);
            AppLog.Info($"Floating keyboard shown at {x:0},{y:0} (transparency {Transparency}%).");
        }
        catch (Exception e)
        {
            AppLog.Warn("Floating keyboard: couldn't show it", e);
        }
    }

    public void Hide()
    {
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
            }
            RefreshCaps();
            return null;
        }
        var keys = FloatingKeyboardLayout.KeysFor(key.Value, Ctrl, Shift, Alt, Cmd);
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

    /// <summary>Width and height of the whole keyboard, in points.</summary>
    public static (double W, double H) Size() =>
        (Pad * 2 + FloatingKeyboardLayout.RowUnits * Unit,
         Pad * 2 + BarHeight + Gap + FloatingKeyboardLayout.Rows.Count * KeyHeight);

    private void ApplyLook()
    {
        if (_panel != null) _panel.Opacity = RadialMenuDef.OpacityFor(Transparency);
    }

    private static (double X, double Y)? SavedPosition()
    {
        var s = AppSettings.Get(PositionKey);
        if (string.IsNullOrWhiteSpace(s)) return null;
        var p = s.Split(',');
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return p.Length == 2 && double.TryParse(p[0], System.Globalization.NumberStyles.Float, inv, out var x)
               && double.TryParse(p[1], System.Globalization.NumberStyles.Float, inv, out var y) ? (x, y) : null;
    }

    private void EnsureWindow()
    {
        if (_window != null) return;
        var rows = new StackPanel { Spacing = 0 };
        foreach (var row in FloatingKeyboardLayout.Rows)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Height = KeyHeight };
            foreach (var key in row) line.Children.Add(MakeCap(key));
            rows.Children.Add(line);
        }
        var title = new TextBlock
        {
            Text = Localizer.Te("keyboard.title", "Keyboard"), FontSize = 12, Foreground = Brushes.Gainsboro,
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
            Content = _panel,
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
                Text = key.Display, FontSize = key.Display.Length > 2 ? 11 : 13, Foreground = KeyText,
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
            cap.Background = key.IsModifier && IsOn(key.Value) ? OnBrush : KeyBrush;
    }

    /// <summary>Is this modifier on (tests and the key colours)?</summary>
    public bool IsOn(string modifier) => modifier switch
    {
        FloatingKeyboardLayout.Ctrl => Ctrl,
        FloatingKeyboardLayout.Shift => Shift,
        FloatingKeyboardLayout.Alt => Alt,
        FloatingKeyboardLayout.Cmd => Cmd,
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

    public void Dispose()
    {
        WriteOpenFlag(false);
        _window?.Close();
        _window = null;
        if (ReferenceEquals(_instance, this)) _instance = null;
    }
}
