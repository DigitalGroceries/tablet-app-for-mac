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
    /// <summary>V29 #154 (Zak 20 Nov: the radial menu and the colour palette can be open at the same time): the palette has its
    /// own window, fed by its own state file and answering through its own command file (RadialMenuState.Palette*FilePath).</summary>
    public static string PaletteStateFilePath { get; set; } = Path.Combine(Path.GetTempPath(), "OpenTabletArtist", "radial-palette");
    public static string PaletteCommandFilePath { get; set; } = Path.Combine(Path.GetTempPath(), "OpenTabletArtist", "radial-palette-cmd");

    private readonly bool _isPalette;
    /// <summary>The overlay for the radial menu (false) or for the colour palette (true).</summary>
    public RadialMenuOverlay(bool palette = false) => _isPalette = palette;
    private string MyStateFile => _isPalette ? PaletteStateFilePath : StateFilePath;
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
    public static void SendCommand(string op, int slice = -1, double x = 0, double y = 0, string? menu = null) =>
        SendCommandTo(false, op, slice, x, y, menu);

    /// <summary>V29 #154: the same, to the radial menu (false) or the colour palette (true).</summary>
    public static void SendCommandTo(bool palette, string op, int slice = -1, double x = 0, double y = 0, string? menu = null)
    {
        try
        {
            lock (CommandGate)
            {
                _commandSeq = Math.Max(_commandSeq + 1, DateTime.UtcNow.Ticks);
                var cmd = new RadialMenuCommand { Seq = _commandSeq, Op = op, Slice = slice, X = x, Y = y, Menu = menu };
                var file = palette ? PaletteCommandFilePath : CommandFilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                var tmp = file + ".tmp";
                File.WriteAllText(tmp, cmd.ToJson());
                File.Move(tmp, file, overwrite: true);
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
    private ColourPaletteWheel? _paletteWheel;   // V29 #142
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

    /// <summary>The overlay the app started (null in tests that don't start one).</summary>
    internal static RadialMenuOverlay? Current { get; private set; }
    /// <summary>V29 #154: the colour palette's overlay (null in tests that don't start one).</summary>
    internal static RadialMenuOverlay? CurrentPalette { get; private set; }

    private void Send(string op, int slice = -1, double x = 0, double y = 0, string? menu = null) => SendCommandTo(_isPalette, op, slice, x, y, menu);

    private MenuState? _shown;
    private long _lastLiveLog;

    /// <summary>v27 (Zak 13 Nov 16:02): a menu that is already open on screen follows the Menu transparency and Menu size
    /// sliders at once, not only after it is closed and opened again. When the open menu is the same menu (the same
    /// slices) as <paramref name="previewMenuJson"/>, the driver is told to use it now; the driver writes its state and the
    /// window follows, so the pen measures taps on the same size as the picture shows. Nothing happens when no menu is
    /// open, when another menu is open, or while a slice is being edited or a Yes / No question shows. UI thread.</summary>
    public static bool ShowLookLive(string previewMenuJson)
    {
        bool menu = Current?.TryLook(previewMenuJson) ?? false;
        bool palette = CurrentPalette?.TryLook(previewMenuJson) ?? false;   // V29 #154/#155: the palette's own window
        return menu || palette;
    }

    internal bool TryLook(string previewMenuJson)
    {
        try
        {
            if (_window?.IsVisible != true || _shown is not { State: "open" } st) return false;
            if (st.Editing || st.Confirm || st.Busy) return false;
            if (!SameSlices(st.Menu, previewMenuJson)) return false;
            var preview = RadialMenuJson.Parse(previewMenuJson);
            // V29 #155 (Zak 20 Nov): an open colour palette follows its own size slider live too (its transparency is fixed).
            if (st.IsPalette ? preview.PaletteSize == st.Size
                             : preview.Size == st.Size && preview.Transparency == st.Transparency) return true;
            Send(RadialMenuCommand.MenuChanged, menu: previewMenuJson);
            if (Environment.TickCount64 - _lastLiveLog > 2000)
            {
                _lastLiveLog = Environment.TickCount64;
                AppLog.Info(st.IsPalette ? $"Colour palette: the open palette now follows the settings (size {preview.PaletteSize})."
                    : $"Radial menu: the open menu now follows the settings (size {preview.Size}, transparency {preview.Transparency}%).");
            }
            return true;
        }
        catch (Exception e)
        {
            AppLog.Warn("Radial menu: couldn't show the new size / transparency on the open menu", e);
            return false;
        }
    }

    /// <summary>V29 #161 (Zak 20 Nov ~09:10: "The update to keyboard did not update on the already opened radial menu"): a
    /// button's radial menu was changed in its Change… window (e.g. Put the keyboard there). An open menu (or palette) that
    /// shows the menu as it was before gets the new one at once, so it doesn't need closing and opening again. A menu opened
    /// from another button (other slices) is left alone. UI thread.</summary>
    public static bool ReplaceLive(AuxBinding before, AuxBinding after)
    {
        if (before.Kind != AuxKind.RadialMenu || after.Kind != AuxKind.RadialMenu) return false;
        if (string.IsNullOrWhiteSpace(before.Menu) || string.IsNullOrWhiteSpace(after.Menu) || before.Menu == after.Menu) return false;
        bool menu = Current?.TryReplace(before.Menu, after.Menu) ?? false;
        // One MenuChanged updates both when the palette is open from the same button (the driver shares the menu).
        bool palette = !menu && (CurrentPalette?.TryReplace(before.Menu, after.Menu) ?? false);
        return menu || palette;
    }

    internal bool TryReplace(string beforeJson, string afterJson)
    {
        try
        {
            if (_window?.IsVisible != true || _shown is not { State: "open" } st) return false;
            if (st.Editing || st.Confirm || st.Busy) return false;
            if (!SameSlices(st.Menu, beforeJson)) return false;
            Send(RadialMenuCommand.MenuChanged, menu: afterJson);
            AppLog.Info(st.IsPalette ? "Colour palette: the open palette now has the menu just saved."
                : "Radial menu: the open menu now shows the slices just saved.");
            return true;
        }
        catch (Exception e)
        {
            AppLog.Warn("Radial menu: couldn't show the saved menu on the open menu", e);
            return false;
        }
    }

    /// <summary>Same slices (labels, actions, submenus), whatever the size and transparency of the menu and the palette.</summary>
    internal static bool SameSlices(string? openMenuJson, string previewMenuJson)
    {
        if (string.IsNullOrWhiteSpace(openMenuJson)) return false;
        var a = RadialMenuJson.Parse(openMenuJson);
        var b = RadialMenuJson.Parse(previewMenuJson);
        a.Size = b.Size; a.Transparency = b.Transparency; a.PaletteSize = b.PaletteSize;   // V29 #155
        return RadialMenuJson.Serialize(a) == RadialMenuJson.Serialize(b);
    }

    public void Start()
    {
        if (_isPalette) CurrentPalette = this; else Current = this;
        SyncOffFlag();
        try
        {
            var dir = Path.GetDirectoryName(MyStateFile)!;
            Directory.CreateDirectory(dir);
            _watcher = new FileSystemWatcher(dir, Path.GetFileName(MyStateFile))
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
        int Transparency = RadialMenuDef.DefaultTransparency, UsedSlice? Used = null, bool Busy = false, bool StepAside = false,
        string Kind = "menu")
    {
        /// <summary>V29 #142: the colour palette is showing instead of the menu.</summary>
        public bool IsPalette => Kind == "palette";
    }

    /// <summary>The slice the driver ran last and whether it worked (TASKS #65). <see cref="Seq"/> is new for each run.</summary>
    public sealed record UsedSlice(long Seq, string Label, string Keys, string Action, bool Ok);

    /// <summary>The pen or the mouse asked for something only the app can do: choose a slice's command ("edit") or
    /// restore the default slices ("restore", after the driver's Yes / No). Sent once; <see cref="Seq"/> only grows.</summary>
    public sealed record RadialRequest(long Seq, string Kind, int Slice, IReadOnlyList<int> Path);

    /// <summary>Set by the app: carries out a <see cref="RadialRequest"/> for the menu with this JSON, and answers the
    /// driver (the new menu, or the same one when nothing changed). Not set in tests.</summary>
    public static Func<RadialRequest, string, System.Threading.Tasks.Task>? RequestHandler { get; set; }

    private long _lastRequestSeq;
    private bool _wasEditing;
    private long _lastUsedSeq = -1;
    private readonly RadialMenuToast _toast = new();

    private async System.Threading.Tasks.Task RunRequestAsync(Func<RadialRequest, string, System.Threading.Tasks.Task> handler, RadialRequest req, string menuJson)
    {
        try { await handler(req, menuJson); }
        catch (Exception e)
        {
            AppLog.Warn("Radial menu: couldn't carry out the " + req.Kind + " request", e);
            Send(RadialMenuCommand.MenuChanged, menu: menuJson);   // let the pen back in
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
            UsedSlice? used = null;
            if (r.TryGetProperty("used", out var us) && us.ValueKind == JsonValueKind.Object)
                used = new UsedSlice(us.TryGetProperty("seq", out var usq) ? usq.GetInt64() : 0,
                    us.TryGetProperty("label", out var ul) ? ul.GetString() ?? "" : "",
                    us.TryGetProperty("keys", out var uk) ? uk.GetString() ?? "" : "",
                    us.TryGetProperty("action", out var ua) ? ua.GetString() ?? "" : "",
                    !us.TryGetProperty("ok", out var uo) || uo.ValueKind != JsonValueKind.False);
            if (state != "open") return new MenuState(state, seq, 0, 0, RadialMenuDef.DefaultSize, 0,
                Array.Empty<string>(), Array.Empty<bool>(), Array.Empty<bool>(), Used: used);
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
                    ? Math.Clamp(tr.GetInt32(), RadialMenuDef.MinTransparency, RadialMenuDef.MaxTransparency) : RadialMenuDef.DefaultTransparency,
                used,
                r.TryGetProperty("busy", out var bz) && bz.ValueKind == JsonValueKind.True,
                r.TryGetProperty("stepAside", out var sa) && sa.ValueKind == JsonValueKind.True,
                r.TryGetProperty("kind", out var kd) ? kd.GetString() ?? "menu" : "menu");
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void OnFileChanged()
    {
        string content;
        try { content = File.ReadAllText(MyStateFile); }
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
            ShowUsedIfNew(st);
            if (st.Request is { } req && req.Seq > _lastRequestSeq)
            {
                _lastRequestSeq = req.Seq;
                AppLog.Info($"Radial menu: the driver asked the app to {req.Kind}" + (req.Slice >= 0 ? $" slice {req.Slice + 1}" : "") + $" (request {req.Seq}).");
                if (RequestHandler is { } handler)
                    _ = RunRequestAsync(handler, req, st.Menu ?? "");
                else AppLog.Warn("Radial menu: no handler for the driver's " + req.Kind + " request, so nothing was opened.");
            }
        });
    }

    private void Show(MenuState st)
    {
        try
        {
            EnsureWindow();
            bool wasVisible = _window!.IsVisible;
            _shown = st;
            _cx = st.X; _cy = st.Y; _size = st.Size;
            double r = RadialMenuGeometry.OuterRadius(st.Size);
            double d = 2 * r + 4;
            _window!.Width = d;
            _window.Height = d;
            // On macOS Avalonia's window coordinates are global points with a top-left origin, the same
            // space as CGEventGetLocation, which is where the plugin took the centre from (see RingModeHud).
            _window.Position = new PixelPoint((int)Math.Round(st.X - d / 2), (int)Math.Round(st.Y - d / 2));
            // V29 #142: the colour palette takes the menu's place in the same window (same size, same centre).
            // While its Yes / No question shows, the palette uses the menu's ring for the answer (right Yes, left No).
            if (st.IsPalette && !st.Confirm)
            {
                if (!ReferenceEquals(_window.Content, _paletteWheel)) _window.Content = _paletteWheel;
                _paletteWheel!.MenuSize = st.Size;
                _paletteWheel.Editing = st.Editing;
                _paletteWheel.Menu = string.IsNullOrEmpty(st.Menu) ? null : RadialMenuJson.Parse(st.Menu);
                _paletteWheel.LastColour = AppSettings.Get(ColourPalette.LastColourKey);   // Zak 20:21: the middle dot
                _paletteWheel.Highlight = HighlightNow();
            }
            else if (!ReferenceEquals(_window.Content, _pie)) _window.Content = _pie;
            _pie!.Editing = st.Editing;
            // TASKS #64: the pencil's hint sits under the menu itself (not a pop-up over the app that was in front) and stays until a slice
            // is chosen, the pencil is put down, or the menu closes.
            if (st.Editing) _toast.Show(st.IsPalette ? Localizer.Te("palette.edit_hint", "Tap the colour to change")
                                                     : Localizer.Te("confirm.radial_edit_hint", "Edit the slice you tap next"), st.X, st.Y, r, hold: true);
            else if (_wasEditing) _toast.HideHint();
            _wasEditing = st.Editing;
            var labels = st.Labels;
            if (st.Confirm)
            {
                // The driver sends only which two slices are live; the words are ours (translated).
                var l = Enumerable.Repeat("", RadialMenuDef.SliceCount).ToList();
                l[2] = Localizer.Te("radial.yes", "Yes");
                l[6] = Localizer.Te("radial.no", "No");
                labels = l;
                _pie.CentreText = st.IsPalette ? Localizer.Te("palette.restore_prompt", "Put the palette's own colours back?")
                                               : Localizer.Te("radial.restore_prompt", "Restore the 8 default slices?");
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
                // V29 #131: while a Click slice clicks, clicks go through the menu to the work below (it stays on screen).
                MacDesktop.MakeOverlay(mac.NSWindow, clickThrough: st.StepAside);
            _pointerTimer!.Start();
            if (!wasVisible)
            {
                double opacity = RadialMenuDef.OpacityFor(st.Transparency);
                AppLog.Info($"Radial menu shown at {st.X:0},{st.Y:0} (size {st.Size}, transparency {st.Transparency}%, opacity {opacity:0.00}, level {st.Depth}).");
            }
        }
        catch (Exception e)
        {
            AppLog.Warn("Radial menu display failed", e);
        }
    }

    private void Hide()
    {
        _press = null;
        _shown = null;
        _pointerTimer?.Stop();
        _toast.HideHint();
        if (_window?.IsVisible == true) _window.Hide();
    }

    /// <summary>TASKS #65: the driver ran a slice: say what happened next to the menu (where it was), or say that it did not work. One text per
    /// run; the same <c>used</c> in later state writes is not shown again. Failures are logged as well.</summary>
    internal void ShowUsedIfNew(MenuState st)
    {
        if (st.Used is not { } u || u.Seq == _lastUsedSeq) return;
        _lastUsedSeq = u.Seq;
        try
        {
            if (u.Ok && u.Action == ColourPalette.Action)
            {
                // V29 #142: a swatch: set it in the app in front where that app allows it (Photoshop), and always copy its code.
                var picked = ColourPick.Apply(u.Keys, u.Label, _window);
                // Zak 19 Nov 20:21: the middle dot shows the colour just picked, also the next time the palette opens.
                if (ColourPalette.IsHex(u.Keys))
                {
                    var hex = u.Keys!.ToUpperInvariant();
                    AppSettings.Set(ColourPalette.LastColourKey, hex);
                    if (_paletteWheel != null) _paletteWheel.LastColour = hex;
                }
                _toast.Show(picked, _cx, _cy, RadialMenuGeometry.OuterRadius(_size), hold: false);
                return;
            }
            if (u.Ok && u.Action == RadialActions.Keyboard)
            {
                // V29 #141: the Keyboard slice shows or hides the floating keyboard (it appearing is the confirmation), under the menu.
                FloatingKeyboard.Instance.Toggle(_cx, _cy + RadialMenuGeometry.OuterRadius(_size) + 12);
                return;
            }
            var text = RadialMenuConfirm.Text(Localizer.Te, u.Label, u.Keys, u.Ok);
            if (!u.Ok) AppLog.Warn($"Radial menu: the slice \"{u.Label}\" ({u.Action}) did not run; the menu says so.");
            else AppLog.Info($"Radial menu: slice \"{u.Label}\" ran, message \"{text}\".");
            _toast.Show(text, _cx, _cy, RadialMenuGeometry.OuterRadius(_size), hold: false);
        }
        catch (Exception e)
        {
            AppLog.Warn("Radial menu: couldn't show what the slice did", e);
        }
    }

    /// <summary>The text bubble (tests).</summary>
    internal RadialMenuToast Toast => _toast;

    private int HighlightNow()
    {
        if (MacDesktop.CursorLocation() is not { } p) return -3;
        return _shown is { IsPalette: true, Confirm: false } ? ColourPalette.SwatchAt(p.X - _cx, p.Y - _cy, _size)
                                         : RadialMenuGeometry.SliceAt(p.X - _cx, p.Y - _cy, _size);
    }

    private void EnsureWindow()
    {
        if (_window != null) return;
        _pie = new RadialMenuPie();
        _pie.PointerPressed += OnPiePressed;
        _pie.PointerMoved += OnPieMoved;
        _pie.PointerReleased += OnPieReleased;
        _pie.PointerCaptureLost += (_, _) => _press = null;
        _paletteWheel = new ColourPaletteWheel();
        _paletteWheel.PointerPressed += OnPalettePressed;
        _paletteWheel.PointerMoved += OnPieMoved;
        _paletteWheel.PointerReleased += OnPieReleased;
        _paletteWheel.PointerCaptureLost += (_, _) => _press = null;
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
        if (_shown is { IsPalette: true, Confirm: false }) { if (_paletteWheel != null && h != _paletteWheel.Highlight) _paletteWheel.Highlight = h; }
        else if (h != _pie.Highlight) _pie.Highlight = h;
        // Esc closes the menu. The driver closes it (it owns the state); the window is hidden when it says so.
        bool esc = EscapeProbe();
        // V29 #154: the menu's and the palette's windows each watch Esc, so one Esc closes both (the X closes only its own).
        if (esc && !_escWasDown) Send(RadialMenuCommand.Close);
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

    /// <summary>V29 #142: the Mac mouse on the colour palette: a swatch is a "slice" (the driver reports its colour back); the
    /// centre's X, pencil, circular arrow and hand do what they do on the menu.</summary>
    private void OnPalettePressed(object? sender, PointerPressedEventArgs e)
    {
        if (_paletteWheel == null || _window == null) return;
        if (!e.GetCurrentPoint(_paletteWheel).Properties.IsLeftButtonPressed) return;
        var local = e.GetPosition(_paletteWheel);
        int hit = ColourPalette.SwatchAt(local.X - _paletteWheel.Bounds.Width / 2, local.Y - _paletteWheel.Bounds.Height / 2, _size);
        RadialTarget? target = hit >= 0 ? RadialTarget.Slice : hit switch
        {
            RadialMenuGeometry.Centre => RadialTarget.Close,
            RadialMenuGeometry.Hand => RadialTarget.Hand,
            RadialMenuGeometry.Edit => RadialTarget.Edit,
            RadialMenuGeometry.Restore => RadialTarget.Restore,
            _ => null,
        };
        if (target is not { } t || GlobalPointer(e) is not { } g) return;
        _press = new RadialMenuPress((g.X, g.Y), WindowOrigin(), t, hit);
        e.Pointer.Capture(_paletteWheel);
        e.Handled = true;
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
            Send(RadialMenuCommand.Move, x: cx, y: cy);
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
            Send(RadialMenuCommand.Move, x: cx, y: cy);   // the last position, whatever was throttled
            return;
        }
        switch (press.Target)
        {
            case RadialTarget.Close:
                Send(RadialMenuCommand.Centre);
                break;
            case RadialTarget.Edit:
                AppLog.Info("Radial menu: mouse click on the pencil.");
                Send(RadialMenuCommand.Edit);
                break;
            case RadialTarget.Restore:
                Send(RadialMenuCommand.Restore);
                break;
            case RadialTarget.Slice:
                if (_pie?.Editing == true) AppLog.Info($"Radial menu: mouse click on slice {press.Slice + 1} while the pencil is on: asking the driver to edit it.");
                // macOS shouldn't activate this app for the click; if it did, the keyboard goes back first.
                if (MacDesktop.GiveFocusBack()) Task.Delay(150).ContinueWith(_ => Send(RadialMenuCommand.Tap, press.Slice));
                else Send(RadialMenuCommand.Tap, press.Slice);
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
        if (ReferenceEquals(Current, this)) Current = null;
        if (ReferenceEquals(CurrentPalette, this)) CurrentPalette = null;
        _pointerTimer?.Stop();
        _watcher?.Dispose();
        _toast.Dispose();
        _window?.Close();
    }
}
