using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using OpenTabletArtist.Controls;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Services;

/// <summary>
/// Shows the radial menu on screen (TASKS #42). The driver plugin (RadialMenuState, inside the daemon) writes
/// the menu's state to a small file whenever it opens, changes level or closes; this watches that file and
/// draws the ring centred where the pen was. The window never takes focus or clicks: the drawing app keeps
/// the keyboard, and the plugin itself reads where the pen tip went down. While open, the slice under the
/// pointer lights up (polled here, so it needs nothing from the driver).
/// The Mac mouse works too (TASKS #54): a click on a slice runs it in the app underneath and the menu stays open,
/// the X (centre, top left) or Esc closes it, the pencil edits the next slice you tap, the circular arrow restores the default slices (after a Yes / No) and the hand (bottom right) drags it. The window asks macOS not to activate this
/// app when clicked, so the shortcut still goes to the app that was in front. The app can't press keys in other
/// apps, so each click is written to a small command file the driver plugin reads (<see cref="RadialMenuCommand"/>).
/// Also owns the Radial menu On/Off switch (Settings › System): Off writes a flag file the plugin checks.
/// </summary>
public sealed class RadialMenuOverlay : IDisposable
{
    public const string SettingKey = "RadialMenu";

    /// <summary>Same files the plugin uses (RadialMenuState.StateFilePath / OffFlagPath).</summary>
    public static string StateFilePath { get; } = Path.Combine(Path.GetTempPath(), "OpenTabletArtist", "radial-menu");
    public static string OffFlagPath { get; } = Path.Combine(Path.GetTempPath(), "OpenTabletArtist", "radial-menu-off");

    /// <summary>The Radial menu switch. On by default: nothing happens until a button is set to Radial menu.</summary>
    public static bool Enabled
    {
        get => AppSettings.Get(SettingKey) != "0";
        set
        {
            AppSettings.Set(SettingKey, value ? "1" : "0");
            SyncOffFlag();
        }
    }

    /// <summary>Make the plugin's flag file match the switch (it can't read the app's settings).</summary>
    public static void SyncOffFlag()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(OffFlagPath)!);
            if (Enabled) { if (File.Exists(OffFlagPath)) File.Delete(OffFlagPath); }
            else File.WriteAllText(OffFlagPath, "off");
        }
        catch (Exception e)
        {
            AppLog.Warn("Radial menu: couldn't update the off switch file", e);
        }
    }

    /// <summary>Where mouse clicks are handed to the driver plugin (same file as RadialMenuState.CommandFilePath).</summary>
    public static string CommandFilePath { get; set; } = Path.Combine(Path.GetTempPath(), "OpenTabletArtist", "radial-menu-cmd");

    private static long _commandSeq;
    private static readonly object CommandGate = new();

    /// <summary>Hand a mouse command to the driver plugin. The sequence number only grows, so each is acted on once.</summary>
    public static void SendCommand(string op, int slice = -1, double x = 0, double y = 0, string? menu = null)
    {
        try
        {
            lock (CommandGate)
            {
                _commandSeq = Math.Max(_commandSeq + 1, DateTime.UtcNow.Ticks);
                var cmd = new RadialMenuCommand { Seq = _commandSeq, Op = op, Slice = slice, X = x, Y = y, Menu = menu };
                Directory.CreateDirectory(Path.GetDirectoryName(CommandFilePath)!);
                var tmp = CommandFilePath + ".tmp";
                File.WriteAllText(tmp, cmd.ToJson());
                File.Move(tmp, CommandFilePath, overwrite: true);
            }
        }
        catch (Exception e)
        {
            AppLog.Warn("Radial menu: couldn't pass the mouse click to the driver", e);
        }
    }

    private FileSystemWatcher? _watcher;
    private Window? _window;
    private RadialMenuPie? _pie;
    private DispatcherTimer? _pointerTimer;
    private long _lastSeq = -1;
    private double _cx, _cy;
    private int _size = RadialMenuDef.DefaultSize;
    private RadialMenuPress? _press;
    private long _lastMoveSent;
    private bool _escWasDown;

    /// <summary>Is Esc down right now? Tests replace it.</summary>
    internal Func<bool> EscapeProbe { get; set; } = MacDesktop.IsEscapeDown;
    /// <summary>The pointer in global points: the OS cursor on a Mac; tests replace it.</summary>
    internal Func<Point?> CursorProbe { get; set; } = MacDesktop.CursorLocation;

    public void Start()
    {
        SyncOffFlag();
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
            AppLog.Warn("Radial menu display unavailable", e);
        }
    }

    /// <summary>What the state file says (null when it can't be read).</summary>
    public sealed record MenuState(string State, long Seq, double X, double Y, int Size, int Depth,
        IReadOnlyList<string> Labels, IReadOnlyList<bool> Set, IReadOnlyList<bool> Submenu,
        bool Editing = false, bool Confirm = false, string? Menu = null, RadialRequest? Request = null,
        int Transparency = RadialMenuDef.DefaultTransparency);

    /// <summary>The pen or the mouse asked for something only the app can do: choose a slice's command ("edit") or
    /// restore the default slices ("restore", after the driver's Yes / No). Sent once; <see cref="Seq"/> only grows.</summary>
    public sealed record RadialRequest(long Seq, string Kind, int Slice, IReadOnlyList<int> Path);

    /// <summary>Set by the app: carries out a <see cref="RadialRequest"/> for the menu with this JSON, and answers the
    /// driver (the new menu, or the same one when nothing changed). Not set in tests.</summary>
    public static Func<RadialRequest, string, System.Threading.Tasks.Task>? RequestHandler { get; set; }

    private long _lastRequestSeq;
    private bool _wasEditing;

    private static async System.Threading.Tasks.Task RunRequestAsync(Func<RadialRequest, string, System.Threading.Tasks.Task> handler, RadialRequest req, string menuJson)
    {
        try { await handler(req, menuJson); }
        catch (Exception e)
        {
            AppLog.Warn("Radial menu: couldn't carry out the " + req.Kind + " request", e);
            SendCommand(RadialMenuCommand.MenuChanged, menu: menuJson);   // let the pen back in
        }
    }

    public static MenuState? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            var state = r.GetProperty("state").GetString() ?? "closed";
            long seq = r.TryGetProperty("seq", out var sq) ? sq.GetInt64() : 0;
            if (state != "open") return new MenuState(state, seq, 0, 0, RadialMenuDef.DefaultSize, 0,
                Array.Empty<string>(), Array.Empty<bool>(), Array.Empty<bool>());
            var labels = new List<string>(); var set = new List<bool>(); var sub = new List<bool>();
            if (r.TryGetProperty("slices", out var slices))
                foreach (var s in slices.EnumerateArray())
                {
                    labels.Add(s.TryGetProperty("label", out var l) ? l.GetString() ?? "" : "");
                    set.Add(s.TryGetProperty("set", out var st) && st.GetBoolean());
                    sub.Add(s.TryGetProperty("action", out var a) && a.GetString() == RadialActions.Submenu);
                }
            RadialRequest? request = null;
            if (r.TryGetProperty("request", out var rq) && rq.ValueKind == JsonValueKind.Object)
            {
                var path = new List<int>();
                if (rq.TryGetProperty("path", out var pa) && pa.ValueKind == JsonValueKind.Array)
                    foreach (var n in pa.EnumerateArray()) path.Add(n.GetInt32());
                request = new RadialRequest(rq.GetProperty("seq").GetInt64(), rq.GetProperty("kind").GetString() ?? "",
                    rq.TryGetProperty("slice", out var sl) ? sl.GetInt32() : -1, path);
            }
            return new MenuState(state, seq,
                r.GetProperty("x").GetDouble(), r.GetProperty("y").GetDouble(),
                r.TryGetProperty("size", out var sz) ? sz.GetInt32() : RadialMenuDef.DefaultSize,
                r.TryGetProperty("depth", out var d) ? d.GetInt32() : 0,
                labels, set, sub,
                r.TryGetProperty("editing", out var ed) && ed.ValueKind == JsonValueKind.True,
                r.TryGetProperty("confirm", out var cf) && cf.ValueKind == JsonValueKind.True,
                r.TryGetProperty("menu", out var mn) ? mn.GetString() : null, request,
                r.TryGetProperty("transparency", out var tr) && tr.ValueKind == JsonValueKind.Number
                    ? Math.Clamp(tr.GetInt32(), RadialMenuDef.MinTransparency, RadialMenuDef.MaxTransparency) : RadialMenuDef.DefaultTransparency);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void OnFileChanged()
    {
        string content;
        try { content = File.ReadAllText(StateFilePath); }
        catch (IOException) { return; }
        catch (UnauthorizedAccessException) { return; }
        var st = Parse(content);
        if (st == null) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (st.Seq == _lastSeq) return;   // a watcher can fire twice for one write
            _lastSeq = st.Seq;
            if (st.State == "open" && Enabled) Show(st);
            else { Hide(); _lastRequestSeq = 0; _wasEditing = false; }   // a new session numbers its requests afresh
            if (st.Request is { } req && req.Seq > _lastRequestSeq)
            {
                _lastRequestSeq = req.Seq;
                if (RequestHandler is { } handler)
                    _ = RunRequestAsync(handler, req, st.Menu ?? "");
            }
        });
    }

    private void Show(MenuState st)
    {
        try
        {
            EnsureWindow();
            bool wasVisible = _window!.IsVisible;
            _cx = st.X; _cy = st.Y; _size = st.Size;
            double r = RadialMenuGeometry.OuterRadius(st.Size);
            double d = 2 * r + 4;
            _window!.Width = d;
            _window.Height = d;
            // On macOS Avalonia's window coordinates are global points with a top-left origin, the same
            // space as CGEventGetLocation, which is where the plugin took the centre from (see RingModeHud).
            _window.Position = new PixelPoint((int)Math.Round(st.X - d / 2), (int)Math.Round(st.Y - d / 2));
            _pie!.Editing = st.Editing;
            if (st.Editing && !_wasEditing) ActionConfirmation.Show("confirm.radial_edit_hint", "Edit the slice you tap next");
            _wasEditing = st.Editing;
            var labels = st.Labels;
            if (st.Confirm)
            {
                // The driver sends only which two slices are live; the words are ours (translated).
                var l = Enumerable.Repeat("", RadialMenuDef.SliceCount).ToList();
                l[2] = Localizer.Te("radial.yes", "Yes");
                l[6] = Localizer.Te("radial.no", "No");
                labels = l;
                _pie.CentreText = Localizer.Te("radial.restore_prompt", "Restore the 8 default slices?");
            }
            else _pie.CentreText = null;
            _pie.Labels = labels;
            _pie.SetFlags = st.Set;
            _pie.Transparency = st.Transparency;
            _pie.SubmenuFlags = st.Submenu;
            _pie.Highlight = HighlightNow();
            if (!_window.IsVisible)
            {
                MacDesktop.RememberFrontmost();   // the app a slice's shortcut must reach
                _escWasDown = EscapeProbe();      // an Esc that is already down isn't "close"
                _window.Show();
            }
            if (_window.TryGetPlatformHandle() is Avalonia.Platform.IMacOSTopLevelPlatformHandle mac && mac.NSWindow != IntPtr.Zero)
                MacDesktop.MakeOverlay(mac.NSWindow, clickThrough: false);
            _pointerTimer!.Start();
            if (!wasVisible) AppLog.Info($"Radial menu shown at {st.X:0},{st.Y:0} (size {st.Size}, level {st.Depth}).");
        }
        catch (Exception e)
        {
            AppLog.Warn("Radial menu display failed", e);
        }
    }

    private void Hide()
    {
        _press = null;
        _pointerTimer?.Stop();
        if (_window?.IsVisible == true) _window.Hide();
    }

    private int HighlightNow()
    {
        if (MacDesktop.CursorLocation() is not { } p) return -3;
        return RadialMenuGeometry.SliceAt(p.X - _cx, p.Y - _cy, _size);
    }

    private void EnsureWindow()
    {
        if (_window != null) return;
        _pie = new RadialMenuPie();
        _pie.PointerPressed += OnPiePressed;
        _pie.PointerMoved += OnPieMoved;
        _pie.PointerReleased += OnPieReleased;
        _pie.PointerCaptureLost += (_, _) => _press = null;
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
            Content = _pie,
            Title = "Radial menu",
        };
        _pointerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _pointerTimer.Tick += (_, _) => Tick();
    }

    /// <summary>One look at the pointer and the Esc key (every 33 ms while the menu is shown).</summary>
    internal void Tick()
    {
        if (_pie == null) return;
        var h = HighlightNow();
        if (h != _pie.Highlight) _pie.Highlight = h;
        // Esc closes the menu. The driver closes it (it owns the state); the window is hidden when it says so.
        bool esc = EscapeProbe();
        if (esc && !_escWasDown) SendCommand(RadialMenuCommand.Close);
        _escWasDown = esc;
    }

    // --- the Mac mouse ---

    private (double X, double Y) WindowOrigin() =>
        _window == null ? (0, 0) : (_window.Position.X, _window.Position.Y);

    private Point? GlobalPointer(PointerEventArgs e)
    {
        if (CursorProbe() is { } p) return p;
        if (_window == null) return null;
        var s = _window.PointToScreen(e.GetPosition(_window));
        return new Point(s.X, s.Y);
    }

    private void OnPiePressed(object? sender, PointerPressedEventArgs e)
    {
        if (_pie == null || _window == null) return;
        if (!e.GetCurrentPoint(_pie).Properties.IsLeftButtonPressed) return;
        var local = e.GetPosition(_pie);
        var hit = RadialMenuMouse.HitTest(local.X - _pie.Bounds.Width / 2, local.Y - _pie.Bounds.Height / 2, _size);
        if (hit.Target == RadialTarget.Outside || GlobalPointer(e) is not { } g) return;
        _press = new RadialMenuPress((g.X, g.Y), WindowOrigin(), hit.Target, hit.Slice);
        e.Pointer.Capture(_pie);
        e.Handled = true;
    }

    private void OnPieMoved(object? sender, PointerEventArgs e)
    {
        if (_press == null || _window == null || GlobalPointer(e) is not { } g) return;
        if (_press.Move((g.X, g.Y)) is not { } origin) return;
        _window.Position = new PixelPoint((int)Math.Round(origin.X), (int)Math.Round(origin.Y));
        var (cx, cy) = Centre();
        _cx = cx; _cy = cy;
        // Tell the driver the new centre (its pen taps are measured from it), not more than ~20 times a second.
        if (Environment.TickCount64 - _lastMoveSent > 50)
        {
            _lastMoveSent = Environment.TickCount64;
            SendCommand(RadialMenuCommand.Move, x: cx, y: cy);
        }
        e.Handled = true;
    }

    private void OnPieReleased(object? sender, PointerReleasedEventArgs e)
    {
        var press = _press;
        _press = null;
        e.Pointer.Capture(null);
        if (press == null) return;
        e.Handled = true;
        if (press.Dragging)
        {
            var (cx, cy) = Centre();
            _cx = cx; _cy = cy;
            SendCommand(RadialMenuCommand.Move, x: cx, y: cy);   // the last position, whatever was throttled
            return;
        }
        switch (press.Target)
        {
            case RadialTarget.Close:
                SendCommand(RadialMenuCommand.Centre);
                break;
            case RadialTarget.Edit:
                SendCommand(RadialMenuCommand.Edit);
                break;
            case RadialTarget.Restore:
                SendCommand(RadialMenuCommand.Restore);
                break;
            case RadialTarget.Slice:
                // macOS shouldn't activate this app for the click; if it did, the keyboard goes back first.
                if (MacDesktop.GiveFocusBack()) Task.Delay(150).ContinueWith(_ => SendCommand(RadialMenuCommand.Tap, press.Slice));
                else SendCommand(RadialMenuCommand.Tap, press.Slice);
                break;
        }
    }

    /// <summary>Centre of the window in global points.</summary>
    private (double X, double Y) Centre()
    {
        var (ox, oy) = WindowOrigin();
        return (ox + (_window?.Width ?? 0) / 2, oy + (_window?.Height ?? 0) / 2);
    }

    /// <summary>The window once it exists (tests: screenshots).</summary>
    internal Window? MenuWindow => _window;

    /// <summary>Show a menu right now, on the UI thread (tests).</summary>
    internal void ShowNowForTest(MenuState st) => Show(st);

    public void Dispose()
    {
        _pointerTimer?.Stop();
        _watcher?.Dispose();
        _window?.Close();
    }
}
