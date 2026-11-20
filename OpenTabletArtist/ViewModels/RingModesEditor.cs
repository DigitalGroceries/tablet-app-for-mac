using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// Wacom-style Touch Ring modes for one wheel: a switch to turn modes on, and up to four modes, each with
/// a name and a key combo per ring direction. While modes are on, the ring button steps through the modes
/// that are on (the daemon plugin does the switching) and the plain clockwise / counter-clockwise rows are
/// hidden. Every edit is handed to the host, which saves the list and rewrites the wheel's bindings.
/// </summary>
public partial class RingModesEditor : ObservableObject
{
    private readonly Func<IReadOnlyList<RingMode>, Task>? _changed;
    private readonly Func<RingCentre, IReadOnlyList<RingMode>, Task<bool>>? _setCentre;
    private RingCentre _centre;
    private bool _centreWantsKeys;

    /// <param name="isOn">V29 #158: the ring is shown as its modes (always, except a ring whose plain rows were set up in
    /// OpenTabletDriver in a way modes can't show). There is no switch any more.</param>
    /// <param name="setOn">Unused since V29 #158 (the Touch Ring modes switch is gone); kept so older callers compile.</param>
    /// <param name="centre">What the centre button does now (null: Next mode).</param>
    /// <param name="setCentre">Writes a new centre-button choice with the current modes; false when it couldn't.</param>
    public RingModesEditor(int wheelIndex, IReadOnlyList<RingMode> modes, bool isOn, bool hasRingButton,
        bool canEdit, Func<AuxBinding, string, Task<AuxBinding?>>? editBinding,
        Func<bool, IReadOnlyList<RingMode>, Task<bool>>? setOn, Func<IReadOnlyList<RingMode>, Task>? changed,
        RingCentre? centre = null, Func<RingCentre, IReadOnlyList<RingMode>, Task<bool>>? setCentre = null)
    {
        _ = setOn;
        WheelIndex = wheelIndex;
        HasRingButton = hasRingButton;
        CanEdit = canEdit && hasRingButton;
        _changed = changed;
        _setCentre = setCentre;
        _centre = centre ?? RingCentre.Next;
        IsOn = isOn;
        Rows = modes.Take(RingModeBinding.MaxModes)
            .Select((m, i) => new RingModeRow(i + 1, m, CanEdit, editBinding, OnRowChanged))
            .ToList();
        SizeNameBoxes();
        UpdateReach();
    }

    // ---- Version 1: no text box wider than the longest function name in it --------------------------
    /// <summary>Width that fits <paramref name="text"/> in a box at <paramref name="fontSize"/>, plus
    /// <paramref name="chrome"/> (padding, border, caret or drop-down arrow).</summary>
    public static double FitWidth(IEnumerable<string> texts, double fontSize, double chrome)
    {
        double w = 0;
        foreach (var t in texts)
        {
            if (string.IsNullOrEmpty(t)) continue;
            try
            {
                var ft = new Avalonia.Media.FormattedText(t, System.Globalization.CultureInfo.InvariantCulture,
                    Avalonia.Media.FlowDirection.LeftToRight, Helpers.AppFonts.UiTypeface(), fontSize, null);
                w = Math.Max(w, ft.WidthIncludingTrailingWhitespace);
            }
            catch (Exception) { w = Math.Max(w, t.Length * fontSize * 0.6); }   // no font system (plain unit tests)
        }
        return Math.Ceiling(w + chrome);
    }

    /// <summary>Every mode's name box gets one width: the longest name among the modes.</summary>
    private void SizeNameBoxes()
    {
        var w = FitWidth(Rows.Select(r => string.IsNullOrWhiteSpace(r.Name) ? r.Title : r.Name), 12, 20);
        foreach (var r in Rows) r.NameBoxWidth = w;
    }

    public int WheelIndex { get; }

    /// <summary>Modes need a ring button to switch them; without one the section explains why it's off.</summary>
    public bool HasRingButton { get; }
    public bool NoRingButton => !HasRingButton;
    public bool CanEdit { get; }

    public IReadOnlyList<RingModeRow> Rows { get; }

    /// <summary>The ring is shown as its modes (V29 #158: no switch; see the constructor).</summary>
    public bool IsOn { get; }

    /// <summary>Show the new mode's name on screen for a moment, like Wacom (app-wide setting).</summary>
    public bool ShowOnScreen
    {
        get => Services.RingModeHud.Enabled;
        set
        {
            if (Services.RingModeHud.Enabled == value) return;
            Services.RingModeHud.Enabled = value;
            OnPropertyChanged();
        }
    }

    /// <summary>"3 modes. Press the ring's centre button to switch.", or what the ring does instead.</summary>
    public string Summary
    {
        get
        {
            if (!HasRingButton) return Localizer.Te("tablet.ring.no_button", "This tablet's ring has no button to switch modes.");
            if (!IsOn) return Localizer.Te("tablet.ring.plain_kept",
                "This ring was set up in OpenTabletDriver in a way the modes can't show, so it keeps these rows.");
            if (!_centre.NextMode) return Localizer.Te("tablet.ring.mode1_only",
                "The centre button doesn't switch modes, so the ring always does what mode 1 is set to.");
            int n = Modes.Count(m => m.Enabled && m.IsMapped);
            if (n == 0) return Localizer.Te("tablet.ring.no_mode", "No mode does anything, so the ring does nothing.");
            return n == 1 ? Localizer.Te("tablet.ring.one_mode", "1 mode. The centre button has nothing to switch.")
                : Localizer.Te("tablet.ring.n_modes", "{count} modes. Press the ring's centre button to switch.")
                    .Replace("{count}", n.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }
    }

    // ---- V29 #158 (Zak 20 Nov): the centre button's own menu, "Next mode" first (the factory choice) ----------------------
    /// <summary>One choice of the centre button's menu; <see cref="Centre"/> null = "Keyboard shortcut…" (typed in the box).</summary>
    public sealed record CentreItem(Func<string> Label, RingCentre? Centre);

    private static CentreItem Mouse(string key, string english, string button) =>
        new(() => Localizer.Te(key, english), new RingCentre(false, new AuxBinding(AuxKind.Mouse, AuxCombo.Unbound, button, AuxKeyBinding.None)));

    private static CentreItem MediaItem(string key) =>
        new(() => AuxKeyBinding.MediaKeyName(key) ?? key,
            new RingCentre(false, new AuxBinding(AuxKind.Keyboard, new AuxCombo(false, false, false, key), AuxKeyBinding.None, AuxKeyBinding.None)));

    public const int CentreNextIndex = 0, CentreNothingIndex = 1, CentreKeysIndex = 2;

    public static IReadOnlyList<CentreItem> CentreItems { get; } = new[]
    {
        new CentreItem(() => Localizer.Te("tablet.ring.centre.next", "Next mode"), RingCentre.Next),
        new CentreItem(() => Localizer.Te("tablet.ring.fn.nothing", "Do nothing"), RingCentre.Nothing),
        new CentreItem(() => Localizer.Te("tablet.ring.fn.keys", "Keyboard shortcut…"), null),
        Mouse("binding.mouse.left", "Left click", "Left"),
        Mouse("binding.mouse.right", "Right click", "Right"),
        Mouse("binding.mouse.middle", "Middle click", "Middle"),
        Mouse("binding.mouse.double_click", "Double Click", AuxKeyBinding.DoubleClickValue),
        MediaItem("Mute"), MediaItem("VolumeUp"), MediaItem("VolumeDown"), MediaItem("PlayPause"),
        MediaItem("PreviousSong"), MediaItem("NextSong"), MediaItem("BrightnessUp"), MediaItem("BrightnessDown"),
    };

    public IReadOnlyList<string> CentreChoices => CentreItems.Select(i => i.Label()).ToList();

    public string CentreLabel => Localizer.Te("tablet.ring.centre", "Centre button");
    public string KeysHint => Localizer.Te("tablet.ring.keys_hint", "Click, then press the keys");

    /// <summary>What the centre button does now.</summary>
    public RingCentre Centre => _centre;

    /// <summary>The centre button switches modes (otherwise only mode 1 is used).</summary>
    public bool SwitchesModes => _centre.NextMode;

    /// <summary>The menu's selection; -1 for a centre binding the menu doesn't list (set up elsewhere), shown in
    /// <see cref="CentreOther"/>.</summary>
    public int CentreIndex
    {
        get
        {
            if (_centreWantsKeys) return CentreKeysIndex;
            if (_centre.NextMode) return CentreNextIndex;
            if (_centre.Other != null) return -1;
            if (!_centre.Binding.IsBound) return CentreNothingIndex;
            for (int i = 0; i < CentreItems.Count; i++)
                if (CentreItems[i].Centre is { } c && c.Binding == _centre.Binding) return i;
            return _centre.Binding.Kind == AuxKind.Keyboard ? CentreKeysIndex : -1;
        }
        set
        {
            if (value < 0 || value >= CentreItems.Count || value == CentreIndex) return;
            if (CentreItems[value].Centre is not { } centre)
            {
                // "Keyboard shortcut…": the box appears; nothing changes until keys are typed into it.
                _centreWantsKeys = true;
                NotifyCentre();
                return;
            }
            _centreWantsKeys = false;
            _ = SetCentreAsync(centre);
        }
    }

    /// <summary>The shortcut box under the menu is shown.</summary>
    public bool ShowCentreKeys => CentreIndex == CentreKeysIndex;

    /// <summary>The centre button's shortcut, for its box (empty when none is set yet).</summary>
    public string CentreKeysText => _centre is { NextMode: false, Other: null, Binding: { Kind: AuxKind.Keyboard } b } && b.Combo.IsBound
        && CentreItems.All(i => i.Centre?.Binding != b) ? AuxKeyBinding.Describe(b) : "";

    /// <summary>A centre binding set up elsewhere (not in the menu), by name.</summary>
    public string CentreOther => CentreIndex >= 0 ? ""
        : Localizer.Te("tablet.ring.centre.other", "Now: {action}").Replace("{action}",
            _centre.Other != null ? _centre.Other.Path ?? "" : AuxKeyBinding.Describe(_centre.Binding), StringComparison.Ordinal);
    public bool HasCentreOther => CentreOther.Length > 0;

    /// <summary>A shortcut typed into the centre button's box; empty = Do nothing (the box stays for new keys).</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private Task SetCentreKeys(AuxCombo? combo)
    {
        _centreWantsKeys = true;
        return SetCentreAsync(combo is { IsBound: true }
            ? new RingCentre(false, new AuxBinding(AuxKind.Keyboard, combo, AuxKeyBinding.None, AuxKeyBinding.None))
            : RingCentre.Nothing);
    }

    private async Task SetCentreAsync(RingCentre centre)
    {
        var before = _centre;
        _centre = centre;
        NotifyCentre();
        if (_setCentre == null) return;
        bool ok;
        try { ok = await _setCentre(centre, Modes); }
        catch (Exception ex)
        {
            AppLog.Warn("Touch Ring: changing what the centre button does failed.", ex);
            ok = false;
        }
        if (ok) return;
        _centre = before;
        NotifyCentre();
    }

    private void NotifyCentre()
    {
        foreach (var n in new[] { nameof(CentreIndex), nameof(ShowCentreKeys), nameof(CentreKeysText), nameof(CentreOther),
                     nameof(HasCentreOther), nameof(SwitchesModes), nameof(Centre), nameof(Summary) })
            OnPropertyChanged(n);
        UpdateReach();
    }

    /// <summary>Modes 2-4 are dimmed while the centre button doesn't switch modes (only mode 1 is used then).</summary>
    private void UpdateReach()
    {
        foreach (var r in Rows) r.IsReachable = _centre.NextMode || r.Number == 1;
    }

    public IReadOnlyList<RingMode> Modes => Rows.Select(r => r.ToMode()).ToList();

    // ---- R18: which mode is active (badge lit in the accent colour) ---------------------------------------
    // The ring plugin in the daemon counts only the enabled, mapped modes (RingModeBinding.ActiveModes) and
    // publishes "mode count" to RingModeHud.StateFilePath on every centre-button press. ShowPublishedMode
    // maps that back to a row. The on-screen centre button (StepActive) only moves the highlight here: the
    // driver keeps its own mode, which the app has no channel to change.

    /// <summary>Rows the driver steps through, in order.</summary>
    private List<RingModeRow> SteppableRows() => Rows.Where(r => { var m = r.ToMode(); return m.Enabled && m.IsMapped; }).ToList();

    /// <summary>Light the row for the driver's published mode (1-based among the steppable modes).</summary>
    public void ShowPublishedMode(int mode)
    {
        var rows = SteppableRows();
        var active = mode >= 1 && mode <= rows.Count ? rows[mode - 1] : null;
        foreach (var r in Rows) r.IsActive = ReferenceEquals(r, active);
        OnPropertyChanged(nameof(ActiveMode));
        OnPropertyChanged(nameof(HasActiveMode));
    }

    /// <summary>Version 1: a mode is lit, so the toggle button shows its number.</summary>
    public bool HasActiveMode => ActiveMode > 0;

    /// <summary>The highlighted mode, 1-based among the steppable modes; 0 when none is lit.</summary>
    public int ActiveMode => SteppableRows().FindIndex(r => r.IsActive) + 1;

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void StepActive()
    {
        var n = SteppableRows().Count;
        if (n == 0) return;
        ShowPublishedMode(ActiveMode % n + 1);
    }

    /// <summary>Read the driver's published mode file, if any, and light that row.</summary>
    public void RefreshFromStateFile(string? path = null)
    {
        try
        {
            var text = System.IO.File.ReadAllText(path ?? Services.RingModeHud.StateFilePath);
            if (Services.RingModeHud.TryParse(text, out var mode, out _, out _)) ShowPublishedMode(mode);
        }
        catch (Exception) { /* no file yet (no press since the daemon started): leave as is */ }
    }

    private Task OnRowChanged()
    {
        SizeNameBoxes();
        OnPropertyChanged(nameof(Summary));
        return _changed?.Invoke(Modes) ?? Task.CompletedTask;
    }
}

/// <summary>Build K: the one thing a ring mode does.
/// Version 26 (#35): Scroll (a plain mouse wheel, every app) is added last, so saved drop-down indices keep their meaning.</summary>
public enum RingAction { Keys, SmoothRotate, SmoothZoom, SmoothSize, SmoothHardness, Scroll }

/// <summary>One mode row: number, editable name, on/off, and the two direction bindings (keys only —
/// the ring plugin sends key combos).</summary>
public partial class RingModeRow : ObservableObject
{
    private readonly Func<Task> _changed;
    private bool _suppress;

    /// <summary>R18: this is the driver's current mode (its badge is lit).</summary>
    [ObservableProperty] private bool _isActive;

    /// <summary>V29 #158: false for modes 2-4 while the centre button doesn't switch modes (the row is dimmed).</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(RowOpacity))] private bool _isReachable = true;
    public double RowOpacity => IsReachable ? 1.0 : 0.45;

    /// <summary>Version 1: the name box fits the longest mode name (set by the editor).</summary>
    [ObservableProperty] private double _nameBoxWidth = 120;

    /// <summary>Version 1: the Function drop-down fits its longest choice.</summary>
    private static double? _actionBoxWidth;
    public static double ActionBoxWidth => _actionBoxWidth ??= RingModesEditor.FitWidth(ActionNames, 14, 48);
    public double ActionWidth => ActionBoxWidth;

    public RingModeRow(int number, RingMode mode, bool canEdit,
        Func<AuxBinding, string, Task<AuxBinding?>>? editBinding, Func<Task> changed)
    {
        Number = number;
        _changed = changed;
        CanEdit = canEdit;
        _suppress = true;
        Name = mode.Name;
        Enabled = mode.Enabled;
        _step = Math.Clamp(mode.StepDegrees, 0f, RingMode.MaxStepDegrees);
        _scroll = Math.Clamp(mode.ScrollAmount, -RingMode.MaxScrollAmount, RingMode.MaxScrollAmount);
        StepText = FormatDegrees(_step);
        SliderDegrees = Math.Max(MinSlider, _step);
        _rotate = mode.RotateSensitivity;
        _size = _rotate != 0 ? 0 : mode.SizeSensitivity;
        _hard = _rotate != 0 || _size != 0 ? 0 : mode.HardnessSensitivity;
        _wheel = _rotate != 0 || _size != 0 || _hard != 0 ? 0 : mode.WheelScroll;
        if (_rotate != 0 || _size != 0 || _hard != 0 || _wheel != 0) _scroll = 0;   // one action per mode
        _action = _rotate != 0 ? RingAction.SmoothRotate : _size != 0 ? RingAction.SmoothSize
                : _hard != 0 ? RingAction.SmoothHardness : _wheel != 0 ? RingAction.Scroll
                : _scroll != 0 ? RingAction.SmoothZoom : RingAction.Keys;
        WheelSensitivity = _wheel != 0 ? Math.Abs(_wheel) : RingMode.DefaultWheelSpeed;
        RotateSensitivity = _rotate != 0 ? Math.Abs(_rotate) : RingMode.DefaultRotateSensitivity;
        SizeSensitivity = _size != 0 ? Math.Abs(_size) : RingMode.DefaultBrushSensitivity;
        HardnessSensitivity = _hard != 0 ? Math.Abs(_hard) : RingMode.DefaultBrushSensitivity;
        ScrollSensitivity = Math.Abs(_scroll) > 0 ? Math.Abs(_scroll) : RingMode.DefaultSpeed;   // Version 16 BETA 2: the Speed itself
        RefreshSpeed();
        _suppress = false;
        Clockwise = MakeDirection(true, mode.Clockwise, editBinding);
        CounterClockwise = MakeDirection(false, mode.CounterClockwise, editBinding);
    }

    public int Number { get; }
    public string Title => $"Mode {Number}";
    public bool CanEdit { get; }

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private bool _enabled;

    // ---- Step size (build H, typeable in build I) -------------------------------------------------------
    // Build I root cause: the box used to be a NumericUpDown whose Value updates on every keystroke; each update
    // saved the profile, which rebuilds these rows — the box was replaced after the first digit ("5" stuck,
    // "50" impossible). Now the text commits only on Enter or when focus leaves (the view binds StepText with
    // UpdateSourceTrigger=LostFocus and pushes it on Enter), and the slider commits when it's released.

    private float _step;
    private int _scroll;
    private int _rotate;

    /// <summary>Canvas degrees per 100° of ring (100 = 1:1), 10–300; committed on slider release.</summary>
    [ObservableProperty] private double _rotateSensitivity = RingMode.DefaultRotateSensitivity;
    public double MinRotateSensitivity => RingMode.MinRotateSensitivity;
    public double MaxRotateSensitivity => RingMode.MaxRotateSensitivity;

    private int _size, _hard, _wheel;
    private RingAction _action;

    /// <summary>Version 26 (#35): the Scroll action's Speed while another action is picked (kept for switching back).</summary>
    [ObservableProperty] private double _wheelSensitivity = RingMode.DefaultWheelSpeed;

    /// <summary>Build K: what this mode does — exactly one of keys / smooth rotate / smooth zoom / smooth brush
    /// size / smooth brush hardness (radio buttons), so a smooth option can't be ticked alongside another.</summary>
    public RingAction Action
    {
        get => _action;
        set
        {
            if (_action == value) return;
            _action = value;
            _rotate = value == RingAction.SmoothRotate ? Sens(RotateSensitivity, RingMode.MinRotateSensitivity, RingMode.MaxRotateSensitivity) : 0;
            _scroll = value == RingAction.SmoothZoom ? Sens(ScrollSensitivity, RingMode.MinSpeed, RingMode.MaxSpeed) : 0;
            _size = value == RingAction.SmoothSize ? Sens(SizeSensitivity, RingMode.MinBrushSensitivity, RingMode.MaxBrushSensitivity) : 0;
            _hard = value == RingAction.SmoothHardness ? Sens(HardnessSensitivity, RingMode.MinBrushSensitivity, RingMode.MaxBrushSensitivity) : 0;
            _wheel = value == RingAction.Scroll ? Sens(WheelSensitivity, RingMode.MinSpeed, RingMode.MaxSpeed) : 0;
            foreach (var n in new[] { nameof(Action), nameof(ActionIndex), nameof(ActionShort), nameof(UseKeys), nameof(UseRotate), nameof(UseScroll), nameof(UseSize),
                         nameof(UseHardness), nameof(UseWheel), nameof(UsesKeys), nameof(ShowScrollOption) })
                OnPropertyChanged(n);
            RefreshSpeed();
            if (!_suppress) _ = _changed();
        }
    }

    // ---- Build K: one Speed per mode (1–100, higher = faster), for keys and smooth actions alike ----------

    /// <summary>The Speed as typed; committed on Enter / focus loss (same pattern as build I's step box).</summary>
    [ObservableProperty] private string _speedText = "";
    /// <summary>The slider's live position; committed on release.</summary>
    [ObservableProperty] private double _speedSlider = RingMode.DefaultSpeed;
    public double MinSpeed => RingMode.MinSpeed;
    public double MaxSpeed => RingMode.MaxSpeed;

    /// <summary>The committed Speed of the current action.</summary>
    public int Speed => Action switch
    {
        RingAction.SmoothRotate => Math.Abs(_rotate),
        RingAction.SmoothSize => Math.Abs(_size),
        RingAction.SmoothHardness => Math.Abs(_hard),
        RingAction.SmoothZoom => Math.Abs(_scroll),
        RingAction.Scroll => Math.Abs(_wheel),
        _ => RingMode.SpeedOfDegrees(_step),
    };

    /// <summary>Zak 9 Nov: what turning the ring does in this mode and where it works, in one or two plain
    /// sentences. The app-specific tip is the separate <see cref="ModeSetup"/> line.</summary>
    public string ModeNote => !Enabled ? Localizer.Te("tablet.ring.note.nothing",
            "The ring does nothing in this mode, and the centre button skips it.") : Action switch
    {
        RingAction.SmoothRotate => Localizer.Te("tablet.ring.note.rotate",
            "Turn the ring to rotate the canvas smoothly (experimental). Works in Photoshop and Affinity. Other apps get this mode's keys instead."),
        RingAction.SmoothZoom => Localizer.Te("tablet.ring.note.zoom",
            "Turn the ring to zoom in and out smoothly (experimental). Works in Photoshop and Affinity. Other apps get this mode's keys instead."),
        RingAction.SmoothSize => Localizer.Te("tablet.ring.note.size",
            "Turn the ring to make the brush bigger or smaller smoothly (experimental). Works in Photoshop only. Other apps get this mode's keys instead."),
        RingAction.SmoothHardness => Localizer.Te("tablet.ring.note.hardness",
            "Turn the ring to make the brush harder or softer smoothly (experimental). Works in Photoshop only, with round brushes. Other apps get this mode's keys instead."),
        RingAction.Scroll => Localizer.Te("tablet.ring.note.scroll",
            "Turn the ring to scroll, like a mouse wheel: clockwise scrolls down, anticlockwise scrolls up. Works in every app."),
        RingAction.Keys => KeysNote(),
        _ => "",
    };

    /// <summary>Zak 9 Nov: the app setting a smooth mode needs, on its own "Setup:" line. Empty for keys.</summary>
    public string ModeSetup => !Enabled ? "" : Action switch
    {
        RingAction.SmoothRotate => Localizer.Te("tablet.ring.setup.rotate",
            "Setup: in Affinity, turn on \"Enable canvas rotation with ⌘+scroll wheel\" in Settings › Tools. Photoshop needs no setup."),
        RingAction.SmoothZoom => Localizer.Te("tablet.ring.setup.zoom",
            "Setup: if Photoshop moves the picture up and down instead of zooming, untick \"Zoom with Scroll Wheel\" in Photoshop's Settings › Tools. Or choose Keyboard shortcut… and use ⌘= and ⌘−."),
        RingAction.SmoothSize => Localizer.Te("tablet.ring.setup.size",
            "Setup: in Photoshop, pick a brush tool and turn on \"Use Graphics Processor\" in Settings › Performance."),
        RingAction.SmoothHardness => Localizer.Te("tablet.ring.setup.hardness",
            "Setup: in Photoshop, turn on \"Vary Round Brush Hardness based on HUD vertical movement\" in Settings › Tools. Without it, the ring changes opacity instead."),
        _ => "",
    };

    public bool HasModeSetup => ModeSetup.Length > 0;

    /// <summary>The Function menu's tooltip: how a smooth mode does its job, for anyone who wants the detail.</summary>
    public string ModeHow => Action switch
    {
        RingAction.SmoothRotate => "How it works: in Photoshop the app holds R and drags (Rotate View); in Affinity it sends ⌘ + scroll wheel. " + HeldKeys,
        RingAction.SmoothZoom => "How it works: the app sends Option + scroll wheel. " + HeldKeys,
        RingAction.SmoothSize => "How it works: in Photoshop the app holds Control + Option and drags sideways. " + HeldKeys,
        RingAction.SmoothHardness => "How it works: in Photoshop the app holds Control + Option and drags up and down. " + HeldKeys,
        RingAction.Scroll => "How it works: the app turns the mouse scroll wheel, one notch at a time, where the pointer is. No keys are held.",
        _ => "Smooth actions work in Photoshop (zoom and rotate also in Affinity) while the pointer is over its window. In other apps, a mode sends its keys.",
    };

    private const string HeldKeys = "Any keys it holds are let go 0.3 s after the ring stops (3 s at most).";

    private string KeysNote()
    {
        var degrees = FormatDegrees(_step < 5 ? 5 : _step);
        var t = Localizer.Tf("tablet.ring.note.keys", ("degrees", degrees));
        return t == "tablet.ring.note.keys" ? $"Turning the ring sends the keys below, once every {degrees}°." : t;
    }

    private void RefreshSpeed()
    {
        var v = Speed;
        SetQuietly(() => { SpeedText = v.ToString(System.Globalization.CultureInfo.InvariantCulture); SpeedSlider = v; });
        OnPropertyChanged(nameof(Speed));
        OnPropertyChanged(nameof(ModeNote));
        OnPropertyChanged(nameof(ModeSetup));
        OnPropertyChanged(nameof(HasModeSetup));
        OnPropertyChanged(nameof(ModeHow));
    }

    public static bool TryParseSpeed(string? text, out int speed)
    {
        speed = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim().Replace(',', '.');
        if (!double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)
            || double.IsNaN(v) || double.IsInfinity(v)) return false;
        speed = (int)Math.Round(Math.Clamp(v, RingMode.MinSpeed, RingMode.MaxSpeed));
        return true;
    }

    partial void OnSpeedTextChanged(string value)
    {
        if (_suppress) return;
        if (TryParseSpeed(value, out var v)) CommitSpeed(v);
        else RefreshSpeed();   // not a number: put the old value back
    }

    partial void OnSpeedSliderChanged(double value)
    {
        if (_suppress) return;
        SetQuietly(() => SpeedText = ((int)Math.Round(value)).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public void CommitSpeedSlider() => CommitSpeed((int)Math.Round(SpeedSlider));

    public void CommitSpeed(int speed)
    {
        speed = Math.Clamp(speed, RingMode.MinSpeed, RingMode.MaxSpeed);
        bool changed = false;
        switch (Action)
        {
            case RingAction.SmoothRotate: changed = _rotate != speed; _rotate = speed; SetQuietly(() => RotateSensitivity = speed); break;
            case RingAction.SmoothSize: changed = _size != speed; _size = speed; SetQuietly(() => SizeSensitivity = speed); break;
            case RingAction.SmoothHardness: changed = _hard != speed; _hard = speed; SetQuietly(() => HardnessSensitivity = speed); break;
            case RingAction.SmoothZoom:
                changed = Math.Abs(_scroll) != speed; _scroll = (_scroll < 0 ? -1 : 1) * speed; SetQuietly(() => ScrollSensitivity = speed); break;
            case RingAction.Scroll:
                changed = Math.Abs(_wheel) != speed; _wheel = (_wheel < 0 ? -1 : 1) * speed; SetQuietly(() => WheelSensitivity = speed); break;
            default:
                var deg = RingMode.DegreesOfSpeed(speed);
                changed = Math.Abs(deg - _step) > 0.01f; _step = deg;
                SetQuietly(() => { StepText = FormatDegrees(deg); SliderDegrees = Math.Max(MinSlider, deg); });
                OnPropertyChanged(nameof(StepDegrees));
                break;
        }
        RefreshSpeed();
        if (changed) _ = _changed();
    }

    /// <summary>V29 #153 (Zak 20 Nov: "The Brightness and all other functions should be available to choose from the menu
    /// called Keystroke ... so the need of extra app window goes away"): actions with a ready pair of keys, picked straight
    /// from the Function menu. Underneath they are the Keys action with these keys (so the driver needs nothing new).</summary>
    public sealed record KeyPreset(string Key, string English, AuxCombo Clockwise, AuxCombo CounterClockwise)
    {
        public string Name => Localizer.Te(Key, English);
    }

    public static IReadOnlyList<KeyPreset> Presets { get; } = new[]
    {
        new KeyPreset("tablet.ring.fn.volume", "Volume", Media("VolumeUp"), Media("VolumeDown")),
        new KeyPreset("tablet.ring.fn.brightness", "Brightness", Media("BrightnessUp"), Media("BrightnessDown")),
        new KeyPreset("tablet.ring.fn.tracks", "Previous / next track", Media("NextSong"), Media("PreviousSong")),
    };

    private static AuxCombo Media(string key) => new(false, false, false, key);

    /// <summary>Build L: the Function dropdown. The first six follow RingAction's order; V29 #153's presets come after
    /// them. Names in the app's language (V29: they were English in every language).</summary>
    public static IReadOnlyList<string> ActionNames => new[]
    {
        Localizer.Te("tablet.ring.fn.keys", "Keyboard shortcut…"),
        // Build Q: the hold-and-drag actions are labelled Experimental; none is on by default (Defaults use keys).
        Localizer.Te("tablet.ring.fn.smooth_rotate", "Smooth rotate"),
        Localizer.Te("tablet.ring.fn.smooth_zoom", "Smooth zoom"),
        Localizer.Te("tablet.ring.fn.smooth_size", "Smooth brush size"),
        Localizer.Te("tablet.ring.fn.smooth_hardness", "Smooth hardness"),
        // Version 26 (#35, Maxime): a plain mouse-wheel scroll in every app, like Wacom's Touch Ring "Scroll".
        Localizer.Te("tablet.ring.fn.scroll", "Scroll"),
    }.Concat(Presets.Select(p => p.Name))
        // V29 #158 (Zak 20 Nov: "If anyone wants to turn off ... the touch ring just let them choose 'Do nothing'"): last, so the
        // indices above keep their meaning. It is the mode's Off (the old "On" tick box): the ring does nothing, the centre skips it.
        .Append(Localizer.Te("tablet.ring.fn.nothing", "Do nothing")).ToList();

    /// <summary>V29 #158: the Function menu's "Do nothing" (the last item).</summary>
    public static int DoNothingIndex => (int)RingAction.Scroll + 1 + Presets.Count;
    public IReadOnlyList<string> ActionChoices => ActionNames;

    /// <summary>The preset these keys are, or null.</summary>
    public KeyPreset? Preset => Action == RingAction.Keys
        ? Presets.FirstOrDefault(p => p.Clockwise == _cw && p.CounterClockwise == _ccw) : null;

    /// <summary>Build N: short function name for the ring diagram ("Keyboard shortcut…", "Smooth rotate", "Volume", …).</summary>
    public string ActionShort => !Enabled ? Localizer.Te("tablet.ring.fn.nothing", "Do nothing") : Preset is { } preset ? preset.Name : ActionNames[(int)Action];

    public int ActionIndex
    {
        get => !Enabled ? DoNothingIndex
            : Preset is { } preset ? (int)RingAction.Scroll + 1 + Presets.ToList().IndexOf(preset) : (int)Action;
        set
        {
            if (value == ActionIndex) return;
            if (value == DoNothingIndex)
            {
                Enabled = false;   // saves (OnEnabledChanged)
                return;
            }
            if (!Enabled)
            {
                // Back from Do nothing: the mode is on again with the function picked (saved once, below or by the setter).
                SetQuietly(() => Enabled = true);
                NotifyEnabled();
                var before = Action;
                var keysBefore = (_cw, _ccw);
                SetFunction(value);
                if (Action == before && (_cw, _ccw) == keysBefore) _ = _changed();
                return;
            }
            SetFunction(value);
        }
    }

    private void SetFunction(int value)
    {
        {
            int presetIndex = value - ((int)RingAction.Scroll + 1);
            if (presetIndex >= 0 && presetIndex < Presets.Count) { UsePreset(Presets[presetIndex]); return; }
            if (value < 0 || value > (int)RingAction.Scroll) return;
            // Leaving a preset for "Keyboard shortcut…": start from empty rows for the user's own keys (picking the preset
            // again puts its keys back), so the menu doesn't jump back to the preset's name.
            if (value == (int)RingAction.Keys && Preset != null) SetKeys(AuxCombo.Unbound, AuxCombo.Unbound);
            Action = (RingAction)value;
            NotifyFunction();
        }
    }

    private void UsePreset(KeyPreset preset)
    {
        SetQuietly(() => Action = RingAction.Keys);
        SetKeys(preset.Clockwise, preset.CounterClockwise);
        NotifyFunction();
        if (!_suppress) _ = _changed();
    }

    private void SetKeys(AuxCombo cw, AuxCombo ccw)
    {
        _cw = cw; _ccw = ccw;
        Clockwise.ShowQuietly(cw.IsBound ? new AuxBinding(AuxKind.Keyboard, cw, AuxKeyBinding.None, AuxKeyBinding.None) : AuxBinding.Unbound);
        CounterClockwise.ShowQuietly(ccw.IsBound ? new AuxBinding(AuxKind.Keyboard, ccw, AuxKeyBinding.None, AuxKeyBinding.None) : AuxBinding.Unbound);
    }

    private void NotifyFunction()
    {
        OnPropertyChanged(nameof(ActionIndex));
        OnPropertyChanged(nameof(ActionShort));
        OnPropertyChanged(nameof(Preset));
    }

    private static int Sens(double v, int min, int max) => (int)Math.Round(Math.Clamp(v, min, max));

    private void Pick(RingAction a, bool on)
    {
        if (on) Action = a;
        else if (Action == a) Action = RingAction.Keys;
    }

    public bool UseKeys { get => Action == RingAction.Keys; set { if (value) Action = RingAction.Keys; } }
    /// <summary>Build J (EXPERIMENTAL): smooth canvas rotation in Photoshop (hold R + drag).</summary>
    public bool UseRotate { get => Action == RingAction.SmoothRotate; set => Pick(RingAction.SmoothRotate, value); }
    /// <summary>Build I: Option+scroll (Photoshop's smooth zoom).</summary>
    public bool UseScroll { get => Action == RingAction.SmoothZoom; set => Pick(RingAction.SmoothZoom, value); }
    /// <summary>Build K (EXPERIMENTAL): brush size by Control+Option+drag sideways.</summary>
    public bool UseSize { get => Action == RingAction.SmoothSize; set => Pick(RingAction.SmoothSize, value); }
    /// <summary>Build K (EXPERIMENTAL): brush hardness by Control+Option+drag up/down.</summary>
    public bool UseHardness { get => Action == RingAction.SmoothHardness; set => Pick(RingAction.SmoothHardness, value); }
    /// <summary>Version 26 (#35): plain scroll wheel, every app.</summary>
    public bool UseWheel { get => Action == RingAction.Scroll; set => Pick(RingAction.Scroll, value); }

    [ObservableProperty] private double _sizeSensitivity = RingMode.DefaultBrushSensitivity;
    [ObservableProperty] private double _hardnessSensitivity = RingMode.DefaultBrushSensitivity;
    public double MinBrushSensitivity => RingMode.MinBrushSensitivity;
    public double MaxBrushSensitivity => RingMode.MaxBrushSensitivity;

    public void CommitSizeSensitivity()
    {
        if (!UseSize) return;
        var v = Sens(SizeSensitivity, RingMode.MinBrushSensitivity, RingMode.MaxBrushSensitivity);
        if (v == _size) return;
        _size = v;
        _ = _changed();
    }

    public void CommitHardnessSensitivity()
    {
        if (!UseHardness) return;
        var v = Sens(HardnessSensitivity, RingMode.MinBrushSensitivity, RingMode.MaxBrushSensitivity);
        if (v == _hard) return;
        _hard = v;
        _ = _changed();
    }

    /// <summary>The rotate sensitivity slider was released: commit it (rotate mode only).</summary>
    public void CommitRotateSensitivity()
    {
        if (!UseRotate) return;
        var v = (int)Math.Round(Math.Clamp(RotateSensitivity, RingMode.MinRotateSensitivity, RingMode.MaxRotateSensitivity));
        if (v == Math.Abs(_rotate)) return;
        _rotate = v;
        _ = _changed();
    }

    public bool ShowScrollOption => true;   // build K: all actions are radio buttons, always shown

    /// <summary>Committed degrees of ring travel per key press; 0 = every ring step (fastest).</summary>
    public float StepDegrees => _step;
    public int ScrollAmount => _scroll;

    public const double MinSlider = 5;
    public double MaxSlider => RingMode.MaxStepDegrees;

    /// <summary>The step as typed. Setting it is the commit (the view only pushes on Enter / focus loss).</summary>
    [ObservableProperty] private string _stepText = "";

    /// <summary>The slider's live position (5–180°); shown in the box while dragging, committed on release.</summary>
    [ObservableProperty] private double _sliderDegrees = MinSlider;

    /// <summary>Smooth zoom's Speed (1–100): one wheel line per DegreesOfSpeed of ring — higher zooms faster.</summary>
    [ObservableProperty] private double _scrollSensitivity = RingMode.DefaultScrollAmount;
    public const double MaxScrollSensitivity = 100;

    /// <summary>Accepts "50", "50°", " 7,5 ", "7.5"; false for anything else. Clamped to 0–180.</summary>
    public static bool TryParseDegrees(string? text, out float degrees)
    {
        degrees = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim().TrimEnd('°').Trim().Replace(',', '.');
        if (!float.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)
            || float.IsNaN(v) || float.IsInfinity(v)) return false;
        degrees = Math.Clamp(v, 0f, RingMode.MaxStepDegrees);
        return true;
    }

    public static string FormatDegrees(float degrees) =>
        degrees.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

    partial void OnStepTextChanged(string value)
    {
        if (_suppress) return;
        if (TryParseDegrees(value, out var v)) CommitStep(v);
        else SetQuietly(() => StepText = FormatDegrees(_step));   // not a number: put the old value back
    }

    partial void OnSliderDegreesChanged(double value)
    {
        if (_suppress) return;
        // Live preview only — committing here would rebuild the row under the drag.
        SetQuietly(() => StepText = FormatDegrees((float)Math.Round(value)));
    }

    /// <summary>The slider was released (or moved by keyboard): commit its position.</summary>
    public void CommitSlider() => CommitStep((float)Math.Round(SliderDegrees));

    private void CommitStep(float degrees)
    {
        degrees = (float)Math.Round(Math.Clamp(degrees, 0f, RingMode.MaxStepDegrees), 1);
        SetQuietly(() =>
        {
            StepText = FormatDegrees(degrees);
            SliderDegrees = Math.Max(MinSlider, degrees);
        });
        if (Math.Abs(degrees - _step) < 0.01f) return;
        _step = degrees;
        OnPropertyChanged(nameof(StepDegrees));
        _ = _changed();
    }

    public bool UsesKeys => Enabled && Action == RingAction.Keys;

    /// <summary>V29 #158: the Speed is shown for a mode that does something (not for Do nothing).</summary>
    public bool HasSpeed => Enabled;

    /// <summary>The sensitivity slider was released: commit it (scroll mode only).</summary>
    public void CommitScrollSensitivity()
    {
        if (!UseScroll) return;
        var v = Sens(ScrollSensitivity, RingMode.MinSpeed, RingMode.MaxSpeed);
        if (v == Math.Abs(_scroll)) return;
        _scroll = Math.Sign(_scroll == 0 ? 1 : _scroll) * v;
        _ = _changed();
    }

    private void SetQuietly(Action set)
    {
        var was = _suppress;
        _suppress = true;
        try { set(); }
        finally { _suppress = was; }
    }

    public ButtonBinding Clockwise { get; }
    public ButtonBinding CounterClockwise { get; }

    private AuxCombo _cw = AuxCombo.Unbound, _ccw = AuxCombo.Unbound;

    public RingMode ToMode() => new(string.IsNullOrWhiteSpace(Name) ? Title : Name.Trim(), _cw, _ccw, Enabled,
        _step, Action == RingAction.SmoothZoom ? _scroll : 0, Action == RingAction.SmoothRotate ? _rotate : 0,
        Action == RingAction.SmoothSize ? _size : 0, Action == RingAction.SmoothHardness ? _hard : 0,
        Action == RingAction.Scroll ? _wheel : 0);

    partial void OnNameChanged(string value)
    {
        if (!_suppress) _ = _changed();
    }

    partial void OnEnabledChanged(bool value)
    {
        NotifyEnabled();
        if (!_suppress) _ = _changed();
    }

    /// <summary>Everything that shows whether the mode is on (V29 #158: Do nothing in the Function menu is the mode's Off).</summary>
    private void NotifyEnabled()
    {
        foreach (var n in new[] { nameof(ActionShort), nameof(ActionIndex), nameof(Preset), nameof(UsesKeys), nameof(HasSpeed),
                     nameof(ModeNote), nameof(ModeSetup), nameof(HasModeSetup) })
            OnPropertyChanged(n);
    }

    private ButtonBinding MakeDirection(bool clockwise, AuxCombo combo,
        Func<AuxBinding, string, Task<AuxBinding?>>? editBinding)
    {
        // V29 #120: the direction names in the app's language (the side is passed in, not read back from the English label).
        var label = clockwise ? Services.Localizer.Te("tablet.ring.clockwise", "Clockwise")
                              : Services.Localizer.Te("tablet.ring.anticlockwise", "Anticlockwise");
        if (clockwise) _cw = combo; else _ccw = combo;
        var binding = combo.IsBound ? new AuxBinding(AuxKind.Keyboard, combo, AuxKeyBinding.None, AuxKeyBinding.None)
                                    : AuxBinding.Unbound;
        return new ButtonBinding(
            index: Number,
            binding: binding,
            isOtherBinding: false,
            otherLabel: "",
            canEdit: CanEdit,
            applyBinding: (_, b) =>
            {
                // The ring plugin sends keys; a mouse or scroll choice can't be kept for a mode.
                var keys = b.Kind == AuxKind.Keyboard ? b.Combo : AuxCombo.Unbound;
                if (clockwise) _cw = keys; else _ccw = keys;
                NotifyFunction();   // V29 #153: e.g. Volume up + Volume down set by hand shows as "Volume"
                return _changed();
            },
            label: label,
            // A ring mode can only keep keys, so the editor doesn't offer the Radial menu tab here (TASKS #42).
            editBinding: editBinding == null ? null : async (current, _) =>
            {
                using (BindingEditorContext.KeysOnlyScope()) return await editBinding(current, $"{Title} · {label}");
            });
    }
}
