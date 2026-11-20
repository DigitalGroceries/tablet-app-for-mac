using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using OpenTabletArtist.Domain;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.DependencyInjection;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Platform.Keyboard;
using OpenTabletDriver.Plugin.Platform.Pointer;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletArtist.Dynamics;

/// <summary>
/// Radial menu (TASKS #42), like Wacom's: a pen button or ExpressKey set to it shows an 8-slice menu centred
/// on the pointer. Tap a slice with the pen tip to use it. V29 #131 (Zak 19 Nov: "The radial menu should never close if not
/// closed by the user by clicking the X"): the menu stays open after a slice is used, an empty slice does nothing, a tap
/// outside the ring goes to the app underneath (as a mouse click does) and the menu never closes by itself; the X in the
/// centre closes it, and so do pressing the button again and Esc (Zak: "Just dont close it when non of this is engaged").
/// A Click slice makes the menu step aside for a moment (the app lets clicks through it) so its click lands on the work below. The menu lives in this binding's "Menu" setting (JSON), so it is
/// saved with the tablet's settings, per application and per pen like any other binding.
/// The app draws the menu (Services/RadialMenuOverlay) from the state file this writes. The pen taps are read here;
/// the Mac mouse's clicks come back from the app through a small command file (TASKS #54, <see cref="RadialMenuState.HandleCommand"/>).
/// The pen tip is kept from the app underneath by <see cref="RadialMenuFilter"/> while the menu is open.
/// </summary>
[PluginName("Tablet App for Mac - Radial menu")]
public class RadialMenuBinding : IStateBinding
{
    [Property("Menu"), ToolTip("The menu, as JSON. Edit it in Tablet App for Mac.")]
    public string? Menu { set; get; }

    [Resolved] public IVirtualKeyboard? Keyboard { set; get; }
    [Resolved] public IMouseButtonHandler? Pointer { set; get; }

    private bool _warned;

    public void Press(TabletReference tablet, IDeviceReport report)
    {
        if (!RadialMenuFilter.Installed && !_warned)
        {
            Log.Write("Radial menu", "The radial menu filter isn't in this profile; save the binding again in Tablet App for Mac.",
                LogLevel.Warning);
            _warned = true;
        }
        FloatingKeyboardKeys.Remember(Keyboard);   // V29 #141: the floating keyboard's keys are pressed on this device
        RadialMenuState.ButtonDown(this);
    }

    public void Release(TabletReference tablet, IDeviceReport report) => RadialMenuState.ButtonUp(this);

    public override string ToString() => "Radial menu";
}

/// <summary>Keeps the pen tip from reaching the app under the menu while it is open (pressure 0 for the whole
/// stroke that started on the menu), and tells the menu where the tip went down.</summary>
[PluginName("Tablet App for Mac - Radial menu filter")]
public class RadialMenuFilter : IPositionedPipelineElement<IDeviceReport>
{
    private static long _constructed;
    public static bool Installed => Interlocked.Read(ref _constructed) != 0;

    public RadialMenuFilter() => Interlocked.Exchange(ref _constructed, 1);

    /// <summary>V29 #141: lets the floating keyboard type even before the radial menu's button was pressed (e.g. after a driver restart).</summary>
    [Resolved] public IVirtualKeyboard? Keyboard { set; get; }

    public PipelinePosition Position => PipelinePosition.PreTransform;
    public event Action<IDeviceReport>? Emit;

    private bool _down;
    private bool _swallow;
    private bool _passThrough;   // V29 #131: a stroke that started outside the open menu goes to the app underneath

    public void Consume(IDeviceReport value)
    {
        FloatingKeyboardKeys.Remember(Keyboard);
        FloatingKeyboardKeys.LookNow();
        if (value is OutOfRangeReport)
        {
            if (_swallow) RadialMenuState.TipUp();
            _down = false;
            _swallow = false;
            _passThrough = false;
        }
        else if (RingGestureGate.IsPenOnly(value) && value is ITabletReport pen)
        {
            bool down = pen.Pressure > 0;
            if (down && !_down && RadialMenuState.SwallowsPen)
            {
                // On the menu: this whole stroke belongs to it. Outside it: the stroke goes to the app (V29 #131).
                if (RadialMenuState.TipDown()) _swallow = true;
                else _passThrough = true;
            }
            else if (down && _swallow) RadialMenuState.PenMoved();   // a drag by the hand
            if (!down)
            {
                if (_swallow) RadialMenuState.TipUp();
                _swallow = false;
                _passThrough = false;
            }
            _down = down;
            if (_swallow || (down && !_passThrough && RadialMenuState.SwallowsPen)) pen.Pressure = 0;
        }
        Emit?.Invoke(value);
    }
}

/// <summary>The one radial menu that can be open, shared by the binding and the filter. Thread-safe.</summary>
public static class RadialMenuState
{

    /// <summary>Per-user temp folder, shared with the app (same place as the ring-mode file).</summary>
    public static string StateFilePath { get; set; } =
        Path.Combine(Path.GetTempPath(), "OpenTabletArtist", "radial-menu");

    /// <summary>The app writes this file while its Radial menu switch is Off; then the function does nothing.</summary>
    public static string OffFlagPath { get; set; } =
        Path.Combine(Path.GetTempPath(), "OpenTabletArtist", "radial-menu-off");

    /// <summary>The app writes the Mac mouse's clicks here (<see cref="RadialMenuCommand"/>); polled while the menu is open.</summary>
    public static string CommandFilePath { get; set; } =
        Path.Combine(Path.GetTempPath(), "OpenTabletArtist", "radial-menu-cmd");

    /// <summary>V29 #154: the colour palette is its own surface (Zak 20 Nov: the radial menu and the palette can be open at the same
    /// time), with its own state and command files, read by the app's second overlay window.</summary>
    public static string PaletteStateFilePath { get; set; } =
        Path.Combine(Path.GetTempPath(), "OpenTabletArtist", "radial-palette");
    public static string PaletteCommandFilePath { get; set; } =
        Path.Combine(Path.GetTempPath(), "OpenTabletArtist", "radial-palette-cmd");

    /// <summary>How often the command file is looked at while the menu is open.</summary>
    public static int CommandPollMs { get; set; } = 40;

    /// <summary>Where the pointer is (macOS global points); tests replace it.</summary>
    public static Func<(double X, double Y)?> CursorProvider { get; set; } = MacCursor.Location;

    /// <summary>Runs an action; tests replace it. Default: keys, clicks and opening apps for real.</summary>
    public static Action<RadialMenuBinding, RadialSlice>? ActionRunner { get; set; }

    private static readonly object Gate = new();

    /// <summary>V29 #154: one open thing on screen, the radial menu or the colour palette. Both can be open at once; every call
    /// works on one of them (<see cref="_s"/>, set under <see cref="Gate"/>).</summary>
    private sealed class Surface
    {
        public Surface(bool palette) => IsPalette = palette;
        public readonly bool IsPalette;
        public RadialMenuBinding? Owner;
        public RadialMenuDef? Top;
        public readonly List<int> Path = new();
        public double Cx, Cy;
        public bool Held, StepAside, Parked, Editing, Confirm, Busy;
        public long BusySince;
        public (long Seq, string Label, string Keys, string Action, bool Ok)? Used;
        public (long Seq, string Kind, int Slice, int[] Path)? Request;
        public bool PenDrag, PenDragMoved;
        public double DragPx, DragPy, DragCx, DragCy;
        public long LastDragWrite;
        public Timer? CommandTimer;
        public long CommandSeqSeen;
        public long OpenedAt;
        public string StateFile => IsPalette ? PaletteStateFilePath : StateFilePath;
        public string CommandFile => IsPalette ? PaletteCommandFilePath : CommandFilePath;
    }

    private static readonly Surface MenuSurface = new(false), PaletteSurface = new(true);
    private static Surface _s = MenuSurface;
    private static Surface? _tipSurface;   // the one the pen tip went down on (a drag stays on it)

    // The fields the menu code always used; they now belong to the surface a call works on.
    private static RadialMenuBinding? _owner { get => _s.Owner; set => _s.Owner = value; }
    private static RadialMenuDef? _top { get => _s.Top; set => _s.Top = value; }
    private static List<int> _path => _s.Path;
    private static double _cx { get => _s.Cx; set => _s.Cx = value; }
    private static double _cy { get => _s.Cy; set => _s.Cy = value; }
    private static bool _held { get => _s.Held; set => _s.Held = value; }
    private static bool _stepAside { get => _s.StepAside; set => _s.StepAside = value; }   // V29 #131
    private static bool _palette => _s.IsPalette;
    private static bool _parked { get => _s.Parked; set => _s.Parked = value; }   // the mouse used or moved it: it stays until closed
    private static bool _editing { get => _s.Editing; set => _s.Editing = value; }   // the pencil was tapped
    private static bool _confirm { get => _s.Confirm; set => _s.Confirm = value; }   // the Yes / No question is showing
    private static bool _busy { get => _s.Busy; set => _s.Busy = value; }   // the app is editing: the pen goes through to the app
    private static long _busySince { get => _s.BusySince; set => _s.BusySince = value; }
    private static (long Seq, string Label, string Keys, string Action, bool Ok)? _used { get => _s.Used; set => _s.Used = value; }
    private static (long Seq, string Kind, int Slice, int[] Path)? _request { get => _s.Request; set => _s.Request = value; }
    private static bool _penDrag { get => _s.PenDrag; set => _s.PenDrag = value; }
    private static bool _penDragMoved { get => _s.PenDragMoved; set => _s.PenDragMoved = value; }
    private static double _dragPx { get => _s.DragPx; set => _s.DragPx = value; }
    private static double _dragPy { get => _s.DragPy; set => _s.DragPy = value; }
    private static double _dragCx { get => _s.DragCx; set => _s.DragCx = value; }
    private static double _dragCy { get => _s.DragCy; set => _s.DragCy = value; }
    private static long _lastDragWrite { get => _s.LastDragWrite; set => _s.LastDragWrite = value; }

    // V29 #142 / #154: one press opens or closes the menu; a quick second press undoes that and opens or closes the palette.
    private static long _lastPressAt = long.MinValue / 2;
    private static (RadialMenuBinding Pressed, bool Open, RadialMenuBinding? Owner, RadialMenuDef? Top, int[] Path, double X, double Y,
        bool Parked)? _menuBeforePress;
    private static long _openedSeq;

    /// <summary>Two presses closer than this count as a double press.</summary>
    public static int DoublePressMs { get; set; } = 400;
    /// <summary>Tests: is the colour palette showing (rather than the menu)?</summary>
    public static bool PaletteOpen { get { lock (Gate) return PaletteSurface.Top != null; } }
    /// <summary>Tests: is the radial menu open (the palette may be open too)?</summary>
    public static bool MenuOpen { get { lock (Gate) return MenuSurface.Top != null; } }

    /// <summary>A Click slice waits this long for the app to let clicks through the menu, and keeps it so this long after.</summary>
    public static int StepAsideMs { get; set; } = 150;
    public static int AfterClickMs { get; set; } = 100;
    private static long _lastActivity;
    private static long _seq;
    private static readonly KeyPulse Pulse = new() { HoldMs = 30 };
    private static long _reqSeq;
    // TASKS #65: the slice that ran last (for the app's short "Selection copied" text) and whether it worked. Sent in the next state write.
    private static long _usedSeq;
    /// <summary>The app edits a slice for at most this long before the pen is taken over by the menu again.</summary>
    public static int BusyMaxMs { get; set; } = 300_000;
    public const double DragThreshold = 4;

    public static bool IsOpen { get { lock (Gate) return MenuSurface.Top != null || PaletteSurface.Top != null; } }

    /// <summary>The menu is open and takes the pen tip (it doesn't while the app is editing a slice, so the pen can click there).</summary>
    public static bool SwallowsPen
    {
        get
        {
            lock (Gate)
            {
                bool any = false;
                foreach (var x in new[] { MenuSurface, PaletteSurface })
                {
                    if (x.Top == null) continue;
                    if (x.Busy && Environment.TickCount64 - x.BusySince > BusyMaxMs) x.Busy = false;
                    any |= !x.Busy;
                }
                return any;
            }
        }
    }

    // V29 #154: true when either surface is in that state (both can be open).
    public static bool Editing { get { lock (Gate) return MenuSurface.Editing || PaletteSurface.Editing; } }
    public static bool Confirming { get { lock (Gate) return MenuSurface.Confirm || PaletteSurface.Confirm; } }
    public static bool Busy { get { lock (Gate) return MenuSurface.Busy || PaletteSurface.Busy; } }

    /// <summary>Slice numbers of the open submenus, from the top (tests).</summary>
    public static IReadOnlyList<int> OpenPath { get { lock (Gate) return MenuSurface.Path.ToArray(); } }

    internal static void ButtonDown(RadialMenuBinding binding)
    {
        lock (Gate)
        {
            var now = Environment.TickCount64;
            // Zak 19 Nov: a double press of the SAME button (Grok's review: another button pressed quickly is two single presses).
            bool quick = now - _lastPressAt <= DoublePressMs && _menuBeforePress is { } first && ReferenceEquals(first.Pressed, binding);
            if (quick)
            {
                // V29 #154 (Zak 20 Nov): the second press of a double press: the first press's menu change is undone and the
                // palette opens or closes instead; the menu stays as it was.
                _lastPressAt = long.MinValue / 2;
                var before = _menuBeforePress!.Value;
                _menuBeforePress = null;
                _s = MenuSurface;
                if (before.Open)
                {
                    if (MenuSurface.Top == null) OpenLocked(before.Owner ?? binding, before.Top, before.X, before.Y, beside: false);
                    // Back exactly as it was (Grok's review): not held by this press, parked as before, same submenu.
                    MenuSurface.Held = false;
                    MenuSurface.Parked = before.Parked;
                    MenuSurface.Path.Clear(); MenuSurface.Path.AddRange(before.Path);
                    WriteStateLocked();
                }
                else if (MenuSurface.Top != null) CloseLocked("double press: the palette instead");
                _s = PaletteSurface;
                if (PaletteSurface.Top != null) { CloseLocked("double press again"); return; }
                if (File.Exists(OffFlagPath) || CursorProvider() is not { } p) return;
                OpenLocked(binding, null, p.X, p.Y, beside: true);
                Log.Write("Radial menu", "Double press: colour palette.", LogLevel.Debug);
                return;
            }
            _lastPressAt = now;
            _s = MenuSurface;
            _menuBeforePress = (binding, MenuSurface.Top != null, MenuSurface.Owner, MenuSurface.Top, MenuSurface.Path.ToArray(),
                MenuSurface.Cx, MenuSurface.Cy, MenuSurface.Parked);
            if (MenuSurface.Top != null)
            {
                // Pressing the button again closes the menu (Wacom), whichever button opened it. The palette stays.
                CloseLocked("button pressed again");
                return;
            }
            if (File.Exists(OffFlagPath)) return;
            var at = CursorProvider();
            if (at is not { } c) return;   // not macOS, or no pointer: do nothing
            OpenLocked(binding, null, c.X, c.Y, beside: true);
        }
    }

    /// <summary>Open the surface <see cref="_s"/> at (x, y) with <paramref name="binding"/>'s menu (or <paramref name="menu"/>).
    /// With <paramref name="beside"/>, it moves beside the other surface when the two circles would overlap (Zak 20 Nov).</summary>
    private static void OpenLocked(RadialMenuBinding binding, RadialMenuDef? menu, double x, double y, bool beside)
    {
        var other = ReferenceEquals(_s, MenuSurface) ? PaletteSurface : MenuSurface;
        _owner = binding;
        _top = menu ?? RadialMenuJson.Parse(binding.Menu);
        if (other.Top != null && beside)
        {
            var (bx, by) = Beside(x, y, RadiusOf(_s, _top), other.Cx, other.Cy, RadiusOf(other, other.Top));
            x = bx; y = by;
        }
        _path.Clear();
        _cx = x; _cy = y;
        _held = true;
        _parked = false;
        _editing = false; _confirm = false; _busy = false; _penDrag = false; _request = null; _used = null;
        _s.OpenedAt = ++_openedSeq;
        Touch();
        WriteStateLocked();
        StartCommandPollingLocked();
    }

    private static double RadiusOf(Surface x, RadialMenuDef def) =>
        RadialMenuGeometry.OuterRadius(x.IsPalette ? def.PaletteSize : def.Size);

    /// <summary>Zak 20 Nov (build 8): "the app had difficulty deciding am I clicking on the radial menu or on the palette when they were
    /// covering some portions of each other". After the surface <see cref="_s"/> was dragged, it is the one on top (taps ask it first),
    /// and if it now covers the other one it moves beside it, as opening does. Returns true when it moved.</summary>
    private static bool AfterDragLocked()
    {
        var other = ReferenceEquals(_s, MenuSurface) ? PaletteSurface : MenuSurface;
        _s.OpenedAt = ++_openedSeq;
        if (_top == null || other.Top == null) return false;
        var (bx, by) = Beside(_cx, _cy, RadiusOf(_s, _top), other.Cx, other.Cy, RadiusOf(other, other.Top));
        if (Math.Abs(bx - _cx) < 0.5 && Math.Abs(by - _cy) < 0.5) return false;
        Log.Write("Radial menu", $"{(_palette ? "The palette" : "The menu")} was dragged over the other one; it moves beside it.", LogLevel.Info);
        _cx = bx; _cy = by;
        return true;
    }

    /// <summary>The screens (macOS global points, top-left origin); tests replace it. Empty when unknown.</summary>
    public static Func<IReadOnlyList<(double X, double Y, double W, double H)>> ScreensProvider { get; set; } = MacScreens.All;

    /// <summary>Where a circle of radius <paramref name="r"/> wanted at (x, y) goes so it doesn't cover the circle at (ox, oy)
    /// of radius <paramref name="or"/> (Zak 20 Nov: "If the palette would cover the menu, it opens beside it"): unchanged when
    /// they don't touch; else beside it on the pointer's side, then the other side, then below, then above - the first
    /// place that is wholly on one screen (Grok's review); if none is, where it was asked for.</summary>
    public static (double X, double Y) Beside(double x, double y, double r, double ox, double oy, double or)
    {
        const double Gap = 12;
        double dx = x - ox, dy = y - oy;
        if (Math.Sqrt(dx * dx + dy * dy) >= r + or + Gap) return (x, y);
        double side = dx >= 0 ? 1 : -1, d = r + or + Gap;
        var tries = new[] { (ox + side * d, oy), (ox - side * d, oy), (ox, oy + d), (ox, oy - d) };
        IReadOnlyList<(double X, double Y, double W, double H)> screens;
        try { screens = ScreensProvider(); } catch (Exception) { screens = Array.Empty<(double, double, double, double)>(); }
        if (screens.Count == 0) return tries[0];
        // The screen the pen is on first (Grok's review: don't let the palette jump to another screen when it fits here).
        var ordered = screens.OrderBy(sc => x >= sc.X && x < sc.X + sc.W && y >= sc.Y && y < sc.Y + sc.H ? 0 : 1).ToList();
        foreach (var sc in ordered)
            foreach (var (tx, ty) in tries)
                if (tx - r >= sc.X && ty - r >= sc.Y && tx + r <= sc.X + sc.W && ty + r <= sc.Y + sc.H) return (tx, ty);
        return (x, y);
    }

    internal static void ButtonUp(RadialMenuBinding binding)
    {
        lock (Gate)
            foreach (var x in new[] { MenuSurface, PaletteSurface })
                if (ReferenceEquals(binding, x.Owner)) x.Held = false;
    }

    /// <summary>The pen tip went down while the menu is open: act on whatever is under the pointer. Returns false when the
    /// tip is not on the menu: that stroke then goes to the app underneath and the menu stays open (V29 #131, like the mouse).</summary>
    public static bool TipDown()
    {
        RadialSlice? run = null;
        RadialMenuBinding? owner;
        Surface surface;
        lock (Gate)
        {
            var at = CursorProvider();
            if (at is not { } c) return false;
            // V29 #154: the radial menu and the palette can both be open; the one opened last is on top, so it is asked first.
            var order = MenuSurface.OpenedAt > PaletteSurface.OpenedAt
                ? new[] { MenuSurface, PaletteSurface } : new[] { PaletteSurface, MenuSurface };
            int slice = RadialMenuGeometry.Outside;
            surface = MenuSurface;
            foreach (var x in order)
            {
                if (x.Top == null || x.Busy) continue;
                int hit = x.IsPalette ? ColourPalette.SwatchAt(c.X - x.Cx, c.Y - x.Cy, x.Top.PaletteSize)
                                      : RadialMenuGeometry.SliceAt(c.X - x.Cx, c.Y - x.Cy, x.Top.Size);
                if (hit != RadialMenuGeometry.Outside) { surface = x; slice = hit; break; }
            }
            if (slice == RadialMenuGeometry.Outside)
            {
                Log.Write("Radial menu", "Pen tap outside the menu: it goes to the app, the menu stays open.", LogLevel.Debug);
                return false;
            }
            _s = surface;
            _tipSurface = surface;
            owner = _owner;
            Touch();
            if (slice == RadialMenuGeometry.Hand)
            {
                // The hand: only a drag does anything (see PenMoved); a tap that doesn't move changes nothing.
                _penDrag = true; _penDragMoved = false;
                _dragPx = c.X; _dragPy = c.Y; _dragCx = _cx; _dragCy = _cy;
                return true;
            }
            if (_palette)
            {
                // V29 #142: the centre works as on the menu (Zak ~20:05): X closes, the pencil then a swatch changes that colour,
                // the circular arrow asks Yes / No to put the palette's colours back. A swatch: the app sets the colour (Photoshop)
                // and copies its code; the palette stays open. While the Yes / No shows, the right half is Yes, the left half No.
                if (_confirm)
                {
                    int yn = RadialMenuGeometry.SliceAt(c.X - _cx, c.Y - _cy, _top.PaletteSize);
                    if (yn >= 0) PaletteAnswerLocked(yn);
                    return true;
                }
                if (slice == RadialMenuGeometry.Centre) CloseLocked("centre");
                else if (slice is RadialMenuGeometry.Edit or RadialMenuGeometry.Restore) CentreQuarterLocked(slice);
                else if (slice >= 0) PaletteTapLocked(slice);
                // ColourPalette.Dot (the middle dot showing the last colour picked; Zak 19 Nov 20:21): a tap there does nothing.
                return true;
            }
            if (slice < 0) { CentreQuarterLocked(slice); return true; }
            run = SliceTappedLocked(slice, fromMouse: false);
            // A Click slice clicks at the pointer, which is on the menu: the menu steps aside (lets clicks through) for it.
            if (run is { Action: RadialActions.Click }) _stepAside = true;
            if (run != null) WriteStateLocked();   // flash the choice; the menu stays open
        }
        if (run != null && owner != null)
        {
            if (run.Action == RadialActions.Click) ClickSteppingAside(owner, run, surface);
            else RunAndReport(owner, run, surface);
        }
        return true;
    }

    /// <summary>V29 #142: a swatch was tapped (pen or mouse): with the pencil on, ask the app to change its colour; otherwise report
    /// its colour to the app (as a run slice, so it shows next to the palette).</summary>
    private static void PaletteTapLocked(int swatch)
    {
        if (_top == null || swatch < 0 || swatch >= ColourPalette.Count) return;
        if (_editing)
        {
            _editing = false;
            _busy = true;
            _busySince = Environment.TickCount64;
            NewRequestLocked("palette-edit", swatch);
            return;
        }
        var hex = ColourPalette.HexFor(_top, swatch);
        _used = (++_usedSeq, ColourPalette.NameOf(swatch), hex, ColourPalette.Action, true);
        WriteStateLocked();
        Log.Write("Radial menu", $"Colour palette: {hex} tapped.", LogLevel.Debug);
    }

    /// <summary>V29 #142: the answer to "Put the palette's colours back?": slice 2 (right) is Yes.</summary>
    private static void PaletteAnswerLocked(int slice)
    {
        _confirm = false;
        if (slice == 2) NewRequestLocked("palette-restore", -1);
        else WriteStateLocked();
    }

    /// <summary>The X, the pencil or the circular arrow was tapped (pen or mouse).</summary>
    private static void CentreQuarterLocked(int quarter)
    {
        switch (quarter)
        {
            case RadialMenuGeometry.Centre:
                CloseLocked("centre");
                break;
            case RadialMenuGeometry.Edit:
                _confirm = false;
                _editing = !_editing;   // tapping the pencil again puts it down
                WriteStateLocked();
                break;
            case RadialMenuGeometry.Restore:
                _editing = false;
                _confirm = true;
                WriteStateLocked();
                break;
        }
    }

    /// <summary>A slice was tapped. Normal: its action to run (null when it only opened a submenu, did nothing or was
    /// used up by edit / restore). An empty slice does nothing, for the pen and the mouse alike (V29 #131).</summary>
    private static RadialSlice? SliceTappedLocked(int slice, bool fromMouse)
    {
        if (_top == null) return null;
        if (_confirm)
        {
            _confirm = false;
            if (slice == 2) NewRequestLocked("restore", -1);   // Yes is on the right, No on the left
            else WriteStateLocked();
            return null;
        }
        var level = RadialMenuJson.Level(_top, _path) ?? _top;
        if (_editing)
        {
            _editing = false;
            _busy = true;
            _busySince = Environment.TickCount64;
            NewRequestLocked("edit", slice);
            return null;
        }
        var s = level.Slices[slice];
        if (!s.IsSet) return null;
        if (s.Action == RadialActions.Submenu)
        {
            _path.Add(slice);
            WriteStateLocked();
            return null;
        }
        if (s.Action == RadialActions.Click && fromMouse)
        {
            // "Click at the pointer" would land on the menu itself.
            Log.Write("Radial menu", "A click slice needs the pen: the mouse pointer is on the menu.", LogLevel.Debug);
            return null;
        }
        return s;
    }

    private static void NewRequestLocked(string kind, int slice)
    {
        _request = (++_reqSeq, kind, slice, _path.ToArray());
        WriteStateLocked();
        _request = null;
    }

    /// <summary>The pen moved while its tip is down on the hand: once it has moved a few points the menu follows it.</summary>
    public static void PenMoved()
    {
        lock (Gate)
        {
            if (_tipSurface == null) return;
            _s = _tipSurface;
            if (_top == null || !_penDrag) return;
            if (CursorProvider() is not { } c) return;
            double dx = c.X - _dragPx, dy = c.Y - _dragPy;
            if (!_penDragMoved && Math.Sqrt(dx * dx + dy * dy) > DragThreshold) _penDragMoved = true;
            if (!_penDragMoved) return;
            _cx = _dragCx + dx; _cy = _dragCy + dy;
            _parked = true;
            Touch();
            if (Environment.TickCount64 - _lastDragWrite >= 30)
            {
                _lastDragWrite = Environment.TickCount64;
                WriteStateLocked();
            }
        }
    }

    /// <summary>The pen tip came up (or the pen left): a drag ends where it is.</summary>
    public static void TipUp()
    {
        lock (Gate)
        {
            if (_tipSurface == null) return;
            _s = _tipSurface;
            if (!_penDrag) return;
            _penDrag = false;
            if (_penDragMoved && _top != null) { AfterDragLocked(); WriteStateLocked(); }
            _penDragMoved = false;
        }
    }


    public static void Close(string why)
    {
        lock (Gate)
            foreach (var x in new[] { MenuSurface, PaletteSurface }) { _s = x; CloseLocked(why); }
    }

    private static void CloseLocked(string why)
    {
        if (_top == null) return;
        _top = null;
        _owner = null;
        _path.Clear();
        _held = false;
        _parked = false;
        _stepAside = false;
        _editing = false; _confirm = false; _busy = false; _penDrag = false; _request = null;
        if (ReferenceEquals(_tipSurface, _s)) _tipSurface = null;
        StopCommandPollingLocked();
        WriteStateLocked();
        Log.Write("Radial menu", (_palette ? "Palette closed: " : "Closed: ") + why, LogLevel.Debug);
    }

    private static void Touch() => Interlocked.Exchange(ref _lastActivity, Environment.TickCount64);

    private static string Describe(RadialSlice s) => string.IsNullOrWhiteSpace(s.Label) ? s.Action : s.Label;

    // --- the Mac mouse (TASKS #54) ---

    private static void StartCommandPollingLocked()
    {
        StopCommandPollingLocked();
        var x = _s;
        x.CommandSeqSeen = ReadCommandFile(x)?.Seq ?? 0;   // whatever is there now is old news
        x.CommandTimer = new Timer(_ => PollCommands(x), null, CommandPollMs, CommandPollMs);
    }

    private static void StopCommandPollingLocked()
    {
        _s.CommandTimer?.Dispose();
        _s.CommandTimer = null;
    }

    private static RadialMenuCommand? ReadCommandFile(Surface x)
    {
        try { return File.Exists(x.CommandFile) ? RadialMenuCommand.Parse(File.ReadAllText(x.CommandFile)) : null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>Look at both command files once (the timers do this while a surface is open; tests call it).</summary>
    public static void PollCommands()
    {
        PollCommands(MenuSurface);
        PollCommands(PaletteSurface);
    }

    private static void PollCommands(Surface x)
    {
        var cmd = ReadCommandFile(x);
        if (cmd == null) return;
        lock (Gate)
        {
            if (x.Top == null || cmd.Seq <= x.CommandSeqSeen) return;
            x.CommandSeqSeen = cmd.Seq;
        }
        HandleCommand(cmd, x);
    }

    /// <summary>The mouse used the menu (or the app finished an edit). A slice runs and the menu STAYS open (so the 8
    /// commands can be used one after another); the X and Esc close it; a drag moves it; the pencil, the circular arrow and
    /// the app's answer are handled as for the pen. Nothing happens when no menu is open.</summary>
    public static void HandleCommand(RadialMenuCommand cmd) => HandleCommand(cmd, MenuSurface);

    /// <summary>V29 #154: a command for the colour palette (the app's palette window writes its own command file).</summary>
    public static void HandlePaletteCommand(RadialMenuCommand cmd) => HandleCommand(cmd, PaletteSurface);

    private static void HandleCommand(RadialMenuCommand cmd, Surface surface)
    {
        RadialSlice? run = null;
        RadialMenuBinding? owner;
        lock (Gate)
        {
            _s = surface;
            if (_top == null) return;
            owner = _owner;
            Touch();
            switch (cmd.Op)
            {
                case RadialMenuCommand.Centre:
                case RadialMenuCommand.Close:
                    CloseLocked("mouse " + cmd.Op);
                    return;
                case RadialMenuCommand.Edit:
                    _parked = true;
                    CentreQuarterLocked(RadialMenuGeometry.Edit);
                    return;
                case RadialMenuCommand.Restore:
                    _parked = true;
                    CentreQuarterLocked(RadialMenuGeometry.Restore);
                    return;
                case RadialMenuCommand.MenuChanged:
                    // The app edited (or restored, or cancelled): use this menu from now on and let the pen back in.
                    _busy = false;
                    if (!string.IsNullOrWhiteSpace(cmd.Menu))
                    {
                        // V29 #154: the menu and the palette share one menu (the palette's colours and size live in it).
                        var changed = RadialMenuJson.Parse(cmd.Menu);
                        var other = ReferenceEquals(surface, MenuSurface) ? PaletteSurface : MenuSurface;
                        if (other.Top != null && ReferenceEquals(other.Owner, surface.Owner))
                        {
                            _s = other;
                            _top = RadialMenuJson.Parse(cmd.Menu);
                            if (RadialMenuJson.Level(_top, _path) == null) _path.Clear();
                            WriteStateLocked();
                            _s = surface;
                        }
                        _top = changed;
                        if (RadialMenuJson.Level(_top, _path) == null) _path.Clear();
                    }
                    WriteStateLocked();
                    return;
                case RadialMenuCommand.Move:
                    // The app moved its window itself; keep the pen's idea of the centre the same. No state write:
                    // the app would move the window back to an older position.
                    _cx = cmd.X; _cy = cmd.Y;
                    _parked = true;
                    return;
                case RadialMenuCommand.MoveEnd:
                    // The mouse let go of the window: its last position; on top now, and beside the other one if it covers it.
                    _cx = cmd.X; _cy = cmd.Y;
                    _parked = true;
                    if (AfterDragLocked()) WriteStateLocked();
                    return;
                case RadialMenuCommand.Tap:
                    _parked = true;
                    if (_palette)
                    {
                        // V29 #142: the Mac mouse clicked a swatch of the colour palette (or Yes / No while that question shows).
                        if (_confirm) { if (cmd.Slice >= 0) PaletteAnswerLocked(cmd.Slice); return; }
                        PaletteTapLocked(cmd.Slice);
                        return;
                    }
                    if (cmd.Slice < 0 || cmd.Slice >= RadialMenuDef.SliceCount) return;
                    run = SliceTappedLocked(cmd.Slice, fromMouse: true);
                    break;
                default:
                    return;
            }
        }
        if (run != null && owner != null) RunAndReport(owner, run, surface);
    }

    /// <summary>V29 #131: run a Click slice once the app has let clicks through the menu, then make the menu take clicks again.</summary>
    private static void ClickSteppingAside(RadialMenuBinding owner, RadialSlice run, Surface surface)
    {
        void Back()
        {
            lock (Gate)
            {
                _s = surface;
                _stepAside = false;
                if (_top != null) WriteStateLocked();
            }
        }
        if (StepAsideMs <= 0) { RunAndReport(owner, run, surface); Back(); return; }
        System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                await System.Threading.Tasks.Task.Delay(StepAsideMs);
                RunAndReport(owner, run, surface);
                await System.Threading.Tasks.Task.Delay(AfterClickMs);
            }
            finally { Back(); }
        });
    }

    /// <summary>Run a slice, then tell the app which one ran and whether it worked (TASKS #65: its short confirmation text, or the
    /// failure). Written with the next state, which is the closed state when this slice closed the menu.</summary>
    private static void RunAndReport(RadialMenuBinding owner, RadialSlice s, Surface surface)
    {
        bool ok = Run(owner, s);
        lock (Gate)
        {
            _s = surface;
            _used = (++_usedSeq, s.Label ?? "", s.Keys ?? "", s.Action ?? "", ok);
            WriteStateLocked();
        }
    }

    /// <summary>True when the action was carried out; false (and logged) when it could not be.</summary>
    private static bool Run(RadialMenuBinding owner, RadialSlice s)
    {
        if (ActionRunner != null) { ActionRunner(owner, s); return true; }
        try
        {
            switch (s.Action)
            {
                case RadialActions.Keys:
                    var keys = RingModeBindingsKeys.Parse(s.Keys, owner.Keyboard);
                    if (keys.Length > 0 && owner.Keyboard != null) { Pulse.Press(owner.Keyboard, keys); return true; }
                    Log.Write("Radial menu", $"Keys not supported: '{s.Keys}'", LogLevel.Warning);
                    return false;
                case RadialActions.Click:
                    return Click(owner.Pointer, s.Click);
                case RadialActions.Open:
                    return OpenApp(s.App);
                case RadialActions.Keyboard:
                    // V29 #141: the app shows or hides the keyboard when it sees this slice ran; the keys come back here.
                    FloatingKeyboardKeys.Remember(owner.Keyboard);
                    FloatingKeyboardKeys.Start();
                    return true;
            }
            return true;
        }
        catch (Exception e)
        {
            Log.Write("Radial menu", $"Action failed: {e.Message}", LogLevel.Warning);
            return false;
        }
    }

    private static bool Click(IMouseButtonHandler? pointer, string? which)
    {
        if (pointer == null || string.IsNullOrWhiteSpace(which))
        {
            Log.Write("Radial menu", "Click not done: no pointer or no button chosen.", LogLevel.Warning);
            return false;
        }
        bool dbl = string.Equals(which, "DoubleClick", StringComparison.OrdinalIgnoreCase);
        if (!Enum.TryParse<MouseButton>(dbl ? "Left" : which, true, out var b))
        {
            Log.Write("Radial menu", $"Click not done: unknown button '{which}'.", LogLevel.Warning);
            return false;
        }
        for (int i = 0; i < (dbl ? 2 : 1); i++)
        {
            // 20 Nov (found with Zak's "No right click" report): the Mac pointer posts a button only when its state differs at Flush,
            // so a down and an up before one flush cancel out. Each one is flushed on its own.
            pointer.MouseDown(b);
            if (pointer is ISynchronousPointer s1) s1.Flush();
            pointer.MouseUp(b);
            if (pointer is ISynchronousPointer s2) s2.Flush();
        }
        return true;
    }

    private static bool OpenApp(string? app)
    {
        if (string.IsNullOrWhiteSpace(app) || !OperatingSystem.IsMacOS())
        {
            Log.Write("Radial menu", "Open app not done: no app chosen (or not macOS).", LogLevel.Warning);
            return false;
        }
        var psi = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
        if (app.EndsWith(".app", StringComparison.OrdinalIgnoreCase) || app.Contains('/'))
        {
            psi.ArgumentList.Add("-a");
            psi.ArgumentList.Add(app);
        }
        else
        {
            psi.ArgumentList.Add("-b");
            psi.ArgumentList.Add(app);
        }
        Process.Start(psi)?.Dispose();
        return true;
    }

    private static object? UsedPayloadLocked() =>
        _used is { } u ? new { seq = u.Seq, label = u.Label, keys = u.Keys, action = u.Action, ok = u.Ok } : null;

    /// <summary>What the app's overlay reads: open or closed, where, how big, and the current ring's slices.</summary>
    private static void WriteStateLocked()
    {
        try
        {
            var seq = ++_seq;
            object payload;
            if (_top == null)
            {
                payload = new { state = "closed", seq, used = UsedPayloadLocked() };
            }
            else
            {
                var level = RadialMenuJson.Level(_top, _path) ?? _top;
                var slices = new List<object>();
                for (int i = 0; i < level.Slices.Count; i++)
                {
                    var s = level.Slices[i];
                    // While "Restore the 8 default slices?" is showing, only Yes (right) and No (left) are on the ring.
                    if (_confirm) slices.Add(new { label = "", action = RadialActions.None, set = i == 2 || i == 6 });
                    else slices.Add(new { label = s.Label, action = s.Action, set = s.IsSet });
                }
                object? request = _request is { } r ? new { seq = r.Seq, kind = r.Kind, slice = r.Slice, path = r.Path } : null;
                payload = new
                {
                    state = "open", seq, x = _cx, y = _cy, size = _palette ? _top.PaletteSize : _top.Size,
                    transparency = _palette ? 0 : _top.Transparency, depth = _path.Count, slices,
                    editing = _editing, confirm = _confirm, busy = _busy, stepAside = _stepAside, kind = _palette ? "palette" : "menu",
                    moving = _penDrag && _penDragMoved,   // V29 #174 (Grok): the app doesn't make the window again mid pen drag
                    menu = RadialMenuJson.Serialize(_top), request, used = UsedPayloadLocked(),
                };
            }
            var json = JsonSerializer.Serialize(payload);
            var file = _s.StateFile;
            var dir = Path.GetDirectoryName(file)!;
            Directory.CreateDirectory(dir);
            var tmp = file + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, file, overwrite: true);
        }
        catch (Exception e)
        {
            Log.Write("Radial menu", $"Couldn't write the menu state: {e.Message}", LogLevel.Warning);
        }
    }

    /// <summary>Tests: forget everything.</summary>
    public static void ResetForTests()
    {
        lock (Gate)
        {
            foreach (var x in new[] { MenuSurface, PaletteSurface })
            {
                _s = x;
                _top = null; _owner = null; _path.Clear(); _held = false; _parked = false; _stepAside = false;
                _editing = false; _confirm = false; _busy = false; _penDrag = false; _request = null; _used = null;
                x.OpenedAt = 0;
                StopCommandPollingLocked();
            }
            _s = MenuSurface; _tipSurface = null; _seq = 0; _usedSeq = 0; _openedSeq = 0;
            _lastPressAt = long.MinValue / 2; _menuBeforePress = null;
            Interlocked.Exchange(ref _lastActivity, Environment.TickCount64);
        }
    }

    /// <summary>Tests: let go of the button as if by hand.</summary>
    public static bool Held { get { lock (Gate) return MenuSurface.Held || PaletteSurface.Held; } }
}

/// <summary>Same key parsing as the touch ring (only keys this Mac's keyboard supports).</summary>
internal static class RingModeBindingsKeys
{
    public static string[] Parse(string? keys, IVirtualKeyboard? keyboard) => RingModeTurnBinding.ParseKeys(keys, keyboard);
}

/// <summary>V29 #154: the screens' bounds in macOS global points (top-left origin); empty elsewhere.</summary>
internal static class MacScreens
{
    private const string CG = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    [StructLayout(LayoutKind.Sequential)] private struct CGRect { public double X, Y, W, H; }
    [DllImport(CG)] private static extern int CGGetActiveDisplayList(uint max, [In, Out] uint[] displays, out uint count);
    [DllImport(CG)] private static extern CGRect CGDisplayBounds(uint display);

    public static IReadOnlyList<(double X, double Y, double W, double H)> All()
    {
        var list = new List<(double, double, double, double)>();
        if (!OperatingSystem.IsMacOS()) return list;
        try
        {
            var ids = new uint[16];
            if (CGGetActiveDisplayList((uint)ids.Length, ids, out var n) != 0) return list;
            for (int i = 0; i < n; i++) { var b = CGDisplayBounds(ids[i]); list.Add((b.X, b.Y, b.W, b.H)); }
        }
        catch (Exception) { }
        return list;
    }
}

/// <summary>The pointer position in macOS global points (top-left origin), or null elsewhere.</summary>
internal static class MacCursor
{
    private const string CG = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CF = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    [StructLayout(LayoutKind.Sequential)] private struct CGPoint { public double X, Y; }
    [DllImport(CG)] private static extern IntPtr CGEventCreate(IntPtr source);
    [DllImport(CG)] private static extern CGPoint CGEventGetLocation(IntPtr evt);
    [DllImport(CF)] private static extern void CFRelease(IntPtr cf);

    public static (double X, double Y)? Location()
    {
        if (!OperatingSystem.IsMacOS()) return null;
        try
        {
            var e = CGEventCreate(IntPtr.Zero);
            if (e == IntPtr.Zero) return null;
            try { var p = CGEventGetLocation(e); return (p.X, p.Y); }
            finally { CFRelease(e); }
        }
        catch (Exception) { return null; }
    }
}
