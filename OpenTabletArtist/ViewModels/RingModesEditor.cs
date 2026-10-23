using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// Wacom-style Touch Ring modes for one wheel: a switch to turn modes on, and up to four modes, each with
/// a name and a key combo per ring direction. While modes are on, the ring button steps through the modes
/// that are on (the daemon plugin does the switching) and the plain clockwise / counter-clockwise rows are
/// hidden. Every edit is handed to the host, which saves the list and rewrites the wheel's bindings.
/// </summary>
public partial class RingModesEditor : ObservableObject
{
    private readonly Func<bool, IReadOnlyList<RingMode>, Task<bool>>? _setOn;
    private readonly Func<IReadOnlyList<RingMode>, Task>? _changed;
    private bool _suppress;

    public RingModesEditor(int wheelIndex, IReadOnlyList<RingMode> modes, bool isOn, bool hasRingButton,
        bool canEdit, Func<AuxBinding, string, Task<AuxBinding?>>? editBinding,
        Func<bool, IReadOnlyList<RingMode>, Task<bool>>? setOn, Func<IReadOnlyList<RingMode>, Task>? changed)
    {
        WheelIndex = wheelIndex;
        HasRingButton = hasRingButton;
        CanEdit = canEdit && hasRingButton;
        _setOn = setOn;
        _changed = changed;
        _suppress = true;
        IsOn = isOn;
        _suppress = false;
        Rows = modes.Take(RingModeBinding.MaxModes)
            .Select((m, i) => new RingModeRow(i + 1, m, CanEdit, editBinding, OnRowChanged))
            .ToList();
        SizeNameBoxes();
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

    /// <summary>Modes on: the ring button switches modes and the rows below drive the ring.</summary>
    [ObservableProperty] private bool _isOn;

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

    /// <summary>"3 modes · ring button switches mode", or why nothing would happen.</summary>
    public string Summary
    {
        get
        {
            int n = Modes.Count(m => m.Enabled && m.IsMapped);
            if (!HasRingButton) return "This tablet's ring has no button to switch modes.";
            if (n == 0) return "No mode has a key set yet.";
            return n == 1 ? "1 mode. The centre button has nothing to switch." : $"{n} modes. Press the ring's centre button to switch.";
        }
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

    partial void OnIsOnChanged(bool value)
    {
        OnPropertyChanged(nameof(Summary));
        if (_suppress) return;
        _ = ToggleAsync(value);
    }

    /// <summary>Apply the switch; if the host couldn't apply it (nothing to write, wheel off, or an error),
    /// flip the switch back so it never shows "on" while the ring still does its old thing. (Build F.)</summary>
    private async Task ToggleAsync(bool value)
    {
        if (_setOn == null) return;
        bool ok;
        try { ok = await _setOn(value, Modes); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ring modes switch failed: {ex.Message}");
            ok = false;
        }
        if (ok || IsOn != value) return;
        _suppress = true;
        try { IsOn = !value; }
        finally { _suppress = false; }
    }

    private Task OnRowChanged()
    {
        SizeNameBoxes();
        OnPropertyChanged(nameof(Summary));
        return _changed?.Invoke(Modes) ?? Task.CompletedTask;
    }
}

/// <summary>Build K: the one thing a ring mode does.</summary>
public enum RingAction { Keys, SmoothRotate, SmoothZoom, SmoothSize, SmoothHardness }

/// <summary>One mode row: number, editable name, on/off, and the two direction bindings (keys only —
/// the ring plugin sends key combos).</summary>
public partial class RingModeRow : ObservableObject
{
    private readonly Func<Task> _changed;
    private bool _suppress;

    /// <summary>R18: this is the driver's current mode (its badge is lit).</summary>
    [ObservableProperty] private bool _isActive;

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
        if (_rotate != 0 || _size != 0 || _hard != 0) _scroll = 0;   // one action per mode
        _action = _rotate != 0 ? RingAction.SmoothRotate : _size != 0 ? RingAction.SmoothSize
                : _hard != 0 ? RingAction.SmoothHardness : _scroll != 0 ? RingAction.SmoothZoom : RingAction.Keys;
        RotateSensitivity = _rotate != 0 ? Math.Abs(_rotate) : RingMode.DefaultRotateSensitivity;
        SizeSensitivity = _size != 0 ? Math.Abs(_size) : RingMode.DefaultBrushSensitivity;
        HardnessSensitivity = _hard != 0 ? Math.Abs(_hard) : RingMode.DefaultBrushSensitivity;
        ScrollSensitivity = Math.Abs(_scroll) > 0 ? RingMode.SpeedOfScroll(_scroll) : RingMode.DefaultSpeed;   // build K: a Speed
        RefreshSpeed();
        _suppress = false;
        Clockwise = MakeDirection("Clockwise", mode.Clockwise, editBinding);
        CounterClockwise = MakeDirection("Anticlockwise", mode.CounterClockwise, editBinding);
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

    private int _size, _hard;
    private RingAction _action;

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
            _scroll = value == RingAction.SmoothZoom ? RingMode.ScrollOfSpeed((int)Math.Round(ScrollSensitivity)) : 0;
            _size = value == RingAction.SmoothSize ? Sens(SizeSensitivity, RingMode.MinBrushSensitivity, RingMode.MaxBrushSensitivity) : 0;
            _hard = value == RingAction.SmoothHardness ? Sens(HardnessSensitivity, RingMode.MinBrushSensitivity, RingMode.MaxBrushSensitivity) : 0;
            foreach (var n in new[] { nameof(Action), nameof(ActionIndex), nameof(ActionShort), nameof(UseKeys), nameof(UseRotate), nameof(UseScroll), nameof(UseSize),
                         nameof(UseHardness), nameof(UsesKeys), nameof(ShowScrollOption) })
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
        RingAction.SmoothZoom => RingMode.SpeedOfScroll(_scroll),
        _ => RingMode.SpeedOfDegrees(_step),
    };

    public string SpeedHint => Action switch
    {
        RingAction.SmoothHardness => "Control + Option + vertical drag. Needs Preferences › Tools › \"Vary Round Brush Hardness based on HUD vertical movement\"; without it the drag changes opacity. Round brushes only.",
        RingAction.SmoothSize => "Control + Option + horizontal drag. Needs a brush tool and Use Graphics Processor.",
        RingAction.SmoothRotate => "Rotate View: holds R and drags.",
        RingAction.SmoothZoom => "Option + scroll wheel. If Photoshop pans instead, use Keys with ⌘= / ⌘−.",
        RingAction.Keys => $"Sends the keys below once every {FormatDegrees(_step < 5 ? 5 : _step)}° of turn.",
        _ => "",
    };

    private void RefreshSpeed()
    {
        var v = Speed;
        SetQuietly(() => { SpeedText = v.ToString(System.Globalization.CultureInfo.InvariantCulture); SpeedSlider = v; });
        OnPropertyChanged(nameof(Speed));
        OnPropertyChanged(nameof(SpeedHint));
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
                var px = RingMode.ScrollOfSpeed(speed); changed = _scroll != px; _scroll = px; SetQuietly(() => ScrollSensitivity = speed); break;
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

    /// <summary>Build L: the "Ring does" dropdown (index into <see cref="ActionNames"/>, same order as RingAction).</summary>
    public static IReadOnlyList<string> ActionNames { get; } = new[]
    {
        "Keystroke…",
        // Build Q: the hold-and-drag actions are labelled Experimental; none is on by default (Defaults use keys).
        "Smooth rotate",
        "Smooth zoom",
        "Smooth brush size",
        "Smooth hardness",
    };
    public IReadOnlyList<string> ActionChoices => ActionNames;

    /// <summary>Build N: short function name for the ring diagram ("Keystroke…", "Rotate", …).</summary>
    public string ActionShort => !Enabled ? "Skip" : Action switch
    {
        RingAction.SmoothRotate => "Smooth rotate",
        RingAction.SmoothZoom => "Smooth zoom",
        RingAction.SmoothSize => "Smooth brush size",
        RingAction.SmoothHardness => "Smooth hardness",
        _ => "Keystroke…",
    };

    public int ActionIndex
    {
        get => (int)Action;
        set { if (value >= 0 && value < ActionNames.Count) Action = (RingAction)value; }
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

    /// <summary>Scroll pixels per ring position (5°) — higher zooms faster. 1–100 on the slider.</summary>
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

    public bool UsesKeys => Action == RingAction.Keys;

    /// <summary>The sensitivity slider was released: commit it (scroll mode only).</summary>
    public void CommitScrollSensitivity()
    {
        if (!UseScroll) return;
        var v = RingMode.ScrollOfSpeed((int)Math.Round(ScrollSensitivity));
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
        Action == RingAction.SmoothSize ? _size : 0, Action == RingAction.SmoothHardness ? _hard : 0);

    partial void OnNameChanged(string value)
    {
        if (!_suppress) _ = _changed();
    }

    partial void OnEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(ActionShort));
        if (!_suppress) _ = _changed();
    }

    private ButtonBinding MakeDirection(string label, AuxCombo combo,
        Func<AuxBinding, string, Task<AuxBinding?>>? editBinding)
    {
        bool clockwise = label == "Clockwise";
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
                return _changed();
            },
            label: label,
            editBinding: editBinding == null ? null : (current, _) => editBinding(current, $"{Title} · {label}"));
    }
}
