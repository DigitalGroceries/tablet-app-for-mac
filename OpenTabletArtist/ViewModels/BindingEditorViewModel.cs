using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// View model for the modal binding editor (<c>BindingEditorDialog</c>): one tab per binding type
/// (Keyboard / Mouse / Scroll). Edits a copy of the binding and only produces a <see cref="Result"/>
/// on Save/Clear — nothing is applied until then, which is why the old inline apply-on-change hazards
/// (transient-null re-apply loops) can't occur here.
/// </summary>
public partial class BindingEditorViewModel : ObservableObject
{
    public const int KeyboardTab = 0, MouseTab = 1, ScrollTab = 2, MediaTab = 3, RadialTab = 4;

    /// <summary>Media / consumer keys, surfaced on their own MEDIA tab. They're keyboard keys under the
    /// hood, so a media binding is still an <see cref="AuxKind.Keyboard"/> binding (no modifiers).</summary>
    private static readonly HashSet<string> MediaKeyValues = new(StringComparer.Ordinal)
    {
        "Mute", "VolumeDown", "VolumeUp", "PlayPause", "PreviousSong", "NextSong", "StopSong",
    };

    public string Title { get; }

    /// <summary>The chosen binding on Save, <see cref="AuxBinding.Unbound"/> on Clear, or null on Cancel.</summary>
    public AuxBinding? Result { get; private set; }

    public event Action? CloseRequested;

    public IReadOnlyList<KeyOption> KeyOptions => AuxKeyBinding.Options;
    public IReadOnlyList<KeyOption> MouseButtonOptions => AuxKeyBinding.MouseButtonOptions;
    public IReadOnlyList<KeyOption> ScrollOptions => AuxKeyBinding.ScrollOptions;

    // Localized labels for pen gesture scroll options (TASKS #24).
    public string PenScrollLabel => Localizer.Te("binding.scroll.pen", "Move pen to scroll");
    public string PenScrollTip => Localizer.Te("binding.scroll.pen_tip",
        "Hold the button and move the pen to scroll, like a mouse wheel. The cursor stays still.");
    public string PenZoomLabel => Localizer.Te("binding.scroll.pen_zoom", "Move pen to zoom");
    public string PenZoomTip => Localizer.Te("binding.scroll.pen_zoom_tip",
        "Hold the button and move the pen up or down to zoom. Uses Option+scroll, like the touch ring Zoom mode.");
    public string PenPanZoomLabel => Localizer.Te("binding.scroll.pen_pan_zoom", "Move pen to pan and zoom");
    public string PenPanZoomTip => Localizer.Te("binding.scroll.pen_pan_zoom_tip",
        "Hold the button and move the pen: sideways to pan, up or down to zoom.");
    public string SpeedLabel => Localizer.Te("binding.scroll.speed", "Speed");
    public string SpeedSlowLabel => Localizer.Te("binding.scroll.speed_slow", "Slow");
    public string SpeedFastLabel => Localizer.Te("binding.scroll.speed_fast", "Fast");
    public string SpeedTip => Localizer.Te("binding.scroll.speed_tip",
        "1–100. Higher is faster. 50 is the middle, same as Pen scroll.");


    [ObservableProperty] private int _selectedTabIndex;
    [ObservableProperty] private bool _ctrl;
    [ObservableProperty] private bool _shift;
    [ObservableProperty] private bool _alt;
    /// <summary>The Cmd modifier (OTD's Application key, sent as Command on macOS). Offered on macOS only.</summary>
    [ObservableProperty] private bool _cmd;

    /// <summary>The hint under the on-screen keyboard, naming this platform's modifiers.</summary>
    public string ModifierHint => OperatingSystem.IsMacOS()
        ? "Cmd, Ctrl, Shift and Option are held modifiers. Tap to toggle, then pick a key."
        : "Ctrl, Shift and Alt are held modifiers. Tap to toggle, then pick a key.";
    [ObservableProperty] private KeyOption? _selectedKeyOption;
    [ObservableProperty] private string? _selectedMouseButton;
    [ObservableProperty] private string? _selectedScroll;
    [ObservableProperty] private string? _selectedMediaKey;
    /// <summary>Slow–Fast (1–100) for Move pen to scroll / zoom / pan+zoom. Same scale as Pen scroll.</summary>
    [ObservableProperty] private int _gestureSpeed = PenScrollMath.DefaultSpeed;

    // ── Radial menu (TASKS #42) ─────────────────────────────────────────────────
    /// <summary>The Radial menu tab is offered for pen buttons and ExpressKeys, not where only keys can be kept
    /// (a Touch Ring mode's keystroke): see <see cref="BindingEditorContext"/>.</summary>
    public bool ShowRadialTab { get; } = !BindingEditorContext.KeysOnly;
    /// <summary>The menu being set (JSON). Starts as the button's own menu, or the default menu.</summary>
    [ObservableProperty] private string _radialMenuText = Domain.RadialMenuJson.Serialize(RadialMenuDefaults.Create(Localizer.Te));
    /// <summary>Opens the menu editor (set by the dialog, which owns the window): current JSON in, new JSON or null out.</summary>
    public Func<string, System.Threading.Tasks.Task<string?>>? EditRadialMenu { get; set; }
    public string RadialTabLabel => Localizer.Te("radial.title", "Radial menu");
    public string RadialIntro => Localizer.Te("radial.intro",
        "Press this button to show a round menu with 8 slices around the pen. Tap a slice with the pen tip to use it. Tap the centre, an empty slice or outside the menu, or press the button again, to close it. Hold the button while you tap to keep the menu open.");
    public string RadialEditLabel => Localizer.Te("radial.edit", "Edit menu…");
    public string RadialSummary
    {
        get
        {
            var m = Domain.RadialMenuJson.Parse(RadialMenuText);
            var names = m.Slices.Select((sl, i) => sl.IsSet && sl.Label.Length > 0 ? sl.Label : null).Where(n => n != null).ToList();
            return names.Count == 0 ? Localizer.Te("radial.empty", "No slices set yet.") : string.Join(" · ", names);
        }
    }
    partial void OnRadialMenuTextChanged(string value) => OnPropertyChanged(nameof(RadialSummary));

    [RelayCommand]
    private async System.Threading.Tasks.Task EditRadial()
    {
        if (EditRadialMenu == null) return;
        var result = await EditRadialMenu(RadialMenuText);
        if (result != null) RadialMenuText = result;
    }


    public BindingEditorViewModel(AuxBinding initial, string title)
    {
        Title = title;
        BuildKeyboard();
        switch (initial.Kind)
        {
            case AuxKind.Keyboard when initial.Combo.IsBound && MediaKeyValues.Contains(initial.Combo.Key):
                // A media key is stored as a keyboard binding but lives on the MEDIA tab.
                SelectedTabIndex = MediaTab;
                SelectedMediaKey = initial.Combo.Key;
                break;
            case AuxKind.Keyboard:
                SelectedTabIndex = KeyboardTab;
                Ctrl = initial.Combo.Ctrl;
                Shift = initial.Combo.Shift;
                Alt = initial.Combo.Alt;
                Cmd = initial.Combo.Cmd;
                SelectKey(initial.Combo.IsBound ? initial.Combo.Key : null);
                break;
            case AuxKind.Mouse:
                SelectedTabIndex = MouseTab;
                SelectedMouseButton = initial.MouseButton == AuxKeyBinding.None ? null : initial.MouseButton;
                break;
            case AuxKind.RadialMenu when ShowRadialTab:
                SelectedTabIndex = RadialTab;
                RadialMenuText = Domain.RadialMenuJson.Serialize(Domain.RadialMenuJson.Parse(initial.Menu));
                break;
            case AuxKind.Scroll:
                SelectedTabIndex = ScrollTab;
                SelectedScroll = initial.Scroll == AuxKeyBinding.None ? null : initial.Scroll;
                GestureSpeed = initial.GestureSpeed ?? PenScrollMath.DefaultSpeed;
                break;
            default:
                SelectedTabIndex = KeyboardTab;   // an unbound button opens on Keyboard
                break;
        }
        UpdateCombo();
        _logReady = true;
        AppLog.Info($"Binding editor opened: '{title}', current binding: {Describe(initial)}, opened on the {TabName(SelectedTabIndex)} tab"
            + (ShowRadialTab ? "" : " (Radial menu tab not offered here)")
            + $"; Radial menu switch (Settings) is {(RadialMenuOverlay.Enabled ? "on" : "OFF")}.");
    }

    private bool _logReady;

    private static string TabName(int tab) => tab switch
    {
        KeyboardTab => "Keyboard", MouseTab => "Mouse", ScrollTab => "Scroll", MediaTab => "Media", RadialTab => "Radial menu", _ => $"tab {tab}",
    };

    private static string Describe(AuxBinding b) =>
        b.Kind == AuxKind.RadialMenu ? "Radial menu" : $"{b.Kind}: {AuxKeyBinding.Describe(b)}";

    /// <summary>Save is enabled only when the active tab has a complete selection.</summary>
    public bool CanSave => SelectedTabIndex switch
    {
        KeyboardTab => SelectedKeyOption != null,
        MouseTab => !string.IsNullOrEmpty(SelectedMouseButton),
        ScrollTab => !string.IsNullOrEmpty(SelectedScroll),
        MediaTab => !string.IsNullOrEmpty(SelectedMediaKey),
        RadialTab => ShowRadialTab,
        _ => false,
    };

    // Tab selection as booleans so the tab strip's RadioButtons bind two-way (checking one switches
    // the tab; changing the tab re-checks the right one).
    public bool IsKeyboardTab { get => SelectedTabIndex == KeyboardTab; set { if (value) SelectedTabIndex = KeyboardTab; } }
    public bool IsMouseTab { get => SelectedTabIndex == MouseTab; set { if (value) SelectedTabIndex = MouseTab; } }
    public bool IsScrollTab { get => SelectedTabIndex == ScrollTab; set { if (value) SelectedTabIndex = ScrollTab; } }
    public bool IsMediaTab { get => SelectedTabIndex == MediaTab; set { if (value) SelectedTabIndex = MediaTab; } }
    public bool IsRadialTab { get => SelectedTabIndex == RadialTab; set { if (value) SelectedTabIndex = RadialTab; } }

    partial void OnSelectedTabIndexChanged(int value)
    {
        if (_logReady) AppLog.Info($"Binding editor '{Title}': {TabName(value)} tab chosen (Save is {(CanSave ? "enabled" : "disabled until a choice is made on this tab")}).");
        SaveCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsKeyboardTab));
        OnPropertyChanged(nameof(IsMouseTab));
        OnPropertyChanged(nameof(IsScrollTab));
        OnPropertyChanged(nameof(IsMediaTab));
        OnPropertyChanged(nameof(IsRadialTab));
    }

    partial void OnSelectedKeyOptionChanged(KeyOption? value) { SaveCommand.NotifyCanExecuteChanged(); UpdateCombo(); }
    partial void OnSelectedMouseButtonChanged(string? value) => SaveCommand.NotifyCanExecuteChanged();
    partial void OnSelectedScrollChanged(string? value)
    {
        SaveCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ShowGestureSpeed));
    }

    /// <summary>Speed slider visible for pen scroll / zoom / pan+zoom.</summary>
    public bool ShowGestureSpeed => AuxKeyBinding.IsPenGestureScroll(SelectedScroll);

    partial void OnSelectedMediaKeyChanged(string? value) => SaveCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        Result = SelectedTabIndex switch
        {
            KeyboardTab => new AuxBinding(AuxKind.Keyboard, new AuxCombo(Ctrl, Shift, Alt, SelectedKeyOption!.Value, Cmd),
                                          AuxKeyBinding.None, AuxKeyBinding.None),
            MouseTab => new AuxBinding(AuxKind.Mouse, AuxCombo.Unbound, SelectedMouseButton!, AuxKeyBinding.None),
            ScrollTab => new AuxBinding(AuxKind.Scroll, AuxCombo.Unbound, AuxKeyBinding.None, SelectedScroll!,
                AuxKeyBinding.IsPenGestureScroll(SelectedScroll) ? GestureSpeed : null),
            // A media key is a keyboard binding with no modifiers.
            MediaTab => new AuxBinding(AuxKind.Keyboard, new AuxCombo(false, false, false, SelectedMediaKey!),
                                       AuxKeyBinding.None, AuxKeyBinding.None),
            RadialTab => AuxBinding.RadialMenu(RadialMenuText),
            _ => AuxBinding.Unbound,
        };
        AppLog.Info($"Binding editor '{Title}': Save clicked on the {TabName(SelectedTabIndex)} tab, new binding: {Describe(Result)}.");
        if (SelectedTabIndex == RadialTab && !RadialMenuOverlay.Enabled)
            AppLog.Warn($"Binding editor '{Title}': Radial menu chosen, but the 'Show the radial menu' switch (Settings › System) is OFF, so the menu will not show until it is switched on.");
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Clear()
    {
        Result = AuxBinding.Unbound;
        AppLog.Info($"Binding editor '{Title}': Clear clicked (the button will do nothing).");
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void CancelDialog()
    {
        Result = null;
        AppLog.Info($"Binding editor '{Title}': Cancel (nothing changed).");
        CloseRequested?.Invoke();
    }

    // ── On-screen keyboard (Keyboard tab) ───────────────────────────────────────
    // A full-size physical layout in three aligned sections (main block · nav cluster · numpad). Every
    // cap maps to an OTD key name and picks the single main key; the Ctrl/Shift/Alt keys are toggles
    // (they highlight while active and combine with the picked key). Valid keys not on the layout —
    // media, F13-F24, misc — go in MoreKeys so nothing is unreachable.
    private readonly Dictionary<string, KeyCap> _keyCaps = new();   // pickable keys, by value
    private readonly List<KeyCap> _modifierCaps = new();            // Ctrl/Shift/Alt caps (may repeat)

    public IReadOnlyList<IReadOnlyList<KeyCap>> MainRows { get; private set; } = Array.Empty<IReadOnlyList<KeyCap>>();
    public IReadOnlyList<IReadOnlyList<KeyCap>> NavRows { get; private set; } = Array.Empty<IReadOnlyList<KeyCap>>();
    public IReadOnlyList<IReadOnlyList<KeyCap>> NumpadRows { get; private set; } = Array.Empty<IReadOnlyList<KeyCap>>();
    public IReadOnlyList<KeyCap> MoreKeys { get; private set; } = Array.Empty<KeyCap>();

    [RelayCommand]
    private void PickKey(KeyCap? cap)
    {
        if (cap == null) return;
        switch (cap.Kind)
        {
            case KeyCapKind.Key:
                // Clicking the already-selected key clears it (toggle).
                SelectKey(cap.Value == SelectedKeyOption?.Value ? null : cap.Value);
                break;
            case KeyCapKind.Modifier:
                if (cap.Value == "Ctrl") Ctrl = !Ctrl;
                else if (cap.Value == "Shift") Shift = !Shift;
                else if (cap.Value == "Alt") Alt = !Alt;
                else if (cap.Value == "Cmd") Cmd = !Cmd;
                break;
        }
    }

    private void SelectKey(string? value)
    {
        SelectedKeyOption = value == null ? null : KeyOptions.FirstOrDefault(o => o.Value == value);
        foreach (var cap in _keyCaps.Values) cap.IsSelected = cap.Value == value;
    }

    partial void OnCtrlChanged(bool value) { SyncModifierCaps(); UpdateCombo(); }
    partial void OnShiftChanged(bool value) { SyncModifierCaps(); UpdateCombo(); }
    partial void OnAltChanged(bool value) { SyncModifierCaps(); UpdateCombo(); }
    partial void OnCmdChanged(bool value) { SyncModifierCaps(); UpdateCombo(); }

    private void SyncModifierCaps()
    {
        foreach (var c in _modifierCaps)
            c.IsSelected = c.Value switch { "Ctrl" => Ctrl, "Shift" => Shift, "Alt" => Alt, "Cmd" => Cmd, _ => false };
    }

    // ── Combo preview (the "CTRL + ALT + E" strip in the footer) ────────────────
    [ObservableProperty] private IReadOnlyList<ComboPart> _comboParts = Array.Empty<ComboPart>();
    [ObservableProperty] private bool _comboEmpty = true;

    private void UpdateCombo()
    {
        var parts = new List<ComboPart>();
        void Add(ComboPart p) { if (parts.Count > 0) parts.Add(ComboPart.Sep()); parts.Add(p); }

        if (Cmd) Add(ComboPart.Chip(AuxKeyBinding.CmdLabel));
        if (Ctrl) Add(ComboPart.Chip("Ctrl"));
        if (Shift) Add(ComboPart.Chip("Shift"));
        if (Alt) Add(ComboPart.Chip(AuxKeyBinding.AltLabel));
        if (SelectedKeyOption is { } k) Add(ComboPart.Chip(k.Display));
        else if (parts.Count > 0) Add(ComboPart.Chip("?", placeholder: true)); // modifiers set, no key yet

        ComboParts = parts;
        ComboEmpty = parts.Count == 0;
    }

    private void BuildKeyboard()
    {
        KeyCap Key(string display, string value, double units = 1)
        {
            var cap = new KeyCap(display, value, units, KeyCapKind.Key, PickKeyCommand);
            if (!string.IsNullOrEmpty(value)) _keyCaps[value] = cap;
            return cap;
        }
        KeyCap Mod(string display, string modId, double units)
        {
            var cap = new KeyCap(display, modId, units, KeyCapKind.Modifier, PickKeyCommand);
            _modifierCaps.Add(cap);
            return cap;
        }
        KeyCap Sp(double units) => new("", "", units, KeyCapKind.Spacer, PickKeyCommand);

        MainRows = new List<IReadOnlyList<KeyCap>>
        {
            new[]{ Key("Esc","Escape"), Sp(1), Key("F1","F1"),Key("F2","F2"),Key("F3","F3"),Key("F4","F4"),
                   Sp(0.5), Key("F5","F5"),Key("F6","F6"),Key("F7","F7"),Key("F8","F8"),
                   Sp(0.5), Key("F9","F9"),Key("F10","F10"),Key("F11","F11"),Key("F12","F12") },
            new[]{ Key("`","Grave"),Key("1","D1"),Key("2","D2"),Key("3","D3"),Key("4","D4"),Key("5","D5"),
                   Key("6","D6"),Key("7","D7"),Key("8","D8"),Key("9","D9"),Key("0","D0"),
                   Key("-","Minus"),Key("=","Equal"),Key("Back","Backspace",2) },
            new[]{ Key("Tab","Tab",1.5), Key("Q","Q"),Key("W","W"),Key("E","E"),Key("R","R"),Key("T","T"),
                   Key("Y","Y"),Key("U","U"),Key("I","I"),Key("O","O"),Key("P","P"),
                   Key("[","LeftBracket"),Key("]","RightBracket"),Key("\\","Backslash",1.5) },
            new[]{ Key("Caps","CapsLock",1.75), Key("A","A"),Key("S","S"),Key("D","D"),Key("F","F"),Key("G","G"),
                   Key("H","H"),Key("J","J"),Key("K","K"),Key("L","L"),
                   Key(";","Semicolon"),Key("'","Quote"),Key("Enter","Enter",2.25) },
            new[]{ Mod("Shift","Shift",2.25), Key("Z","Z"),Key("X","X"),Key("C","C"),Key("V","V"),Key("B","B"),
                   Key("N","N"),Key("M","M"),Key(",","Comma"),Key(".","Period"),Key("/","Slash"),
                   Mod("Shift","Shift",2.75) },
            OperatingSystem.IsMacOS()
                // A Mac keyboard's bottom row: Cmd is a held modifier here (sent as OTD's Application key).
                ? new[]{ Mod("Ctrl","Ctrl",1.5), Mod("Option","Alt",1.5), Mod("Cmd","Cmd",1.75), Key("Space","Space",6.5),
                         Mod("Cmd","Cmd",1.75), Mod("Option","Alt",2) }
                : new[]{ Mod("Ctrl","Ctrl",1.5), Mod("Alt","Alt",1.5), Key("Space","Space",7),
                         Mod("Alt Gr","Alt",1.5), Key("Menu","Application",1.5), Mod("Ctrl","Ctrl",2) },
        };

        NavRows = new List<IReadOnlyList<KeyCap>>
        {
            new[]{ Key("PrtSc","PrintScreen"), Key("ScrLk","ScrollLock"), Key("Pause","Pause") },
            new[]{ Key("Ins","Insert"), Key("Home","Home"), Key("PgUp","PageUp") },
            new[]{ Key("Del","Delete"), Key("End","End"), Key("PgDn","PageDown") },
            new[]{ Sp(3) },
            new[]{ Sp(1), Key("↑","Up"), Sp(1) },
            new[]{ Key("←","Left"), Key("↓","Down"), Key("→","Right") },
        };

        NumpadRows = new List<IReadOnlyList<KeyCap>>
        {
            new[]{ Sp(4) },
            new[]{ Key("Num","NumberLock"), Key("/","Divide"), Key("*","Multiply"), Key("-","Subtract") },
            new[]{ Key("7","Keypad7"), Key("8","Keypad8"), Key("9","Keypad9"), Key("+","Add") },
            new[]{ Key("4","Keypad4"), Key("5","Keypad5"), Key("6","Keypad6"), Sp(1) },
            new[]{ Key("1","Keypad1"), Key("2","Keypad2"), Key("3","Keypad3"), Sp(1) },
            new[]{ Key("0","Keypad0",2), Key(".","Decimal"), Key("=","KeypadEqual") },
        };

        var modifiers = new HashSet<string>
        {
            "Shift","Control","Alt","LeftShift","RightShift","LeftControl","RightControl",
            "LeftAlt","RightAlt","LeftApplication","RightApplication","Application",
        };
        MoreKeys = KeyOptions
            .Where(o => !_keyCaps.ContainsKey(o.Value) && !modifiers.Contains(o.Value)
                        && !MediaKeyValues.Contains(o.Value)) // media keys live on the MEDIA tab now
            .Select(o => Key(o.Display, o.Value))
            .ToList();

        SyncModifierCaps();
    }
}

public enum KeyCapKind { Key, Modifier, Spacer }

/// <summary>TASKS #42: while a <see cref="KeysOnlyScope"/> is open (a Touch Ring mode's keystroke, or a radial
/// menu slice's keystroke) the binding editor doesn't offer the Radial menu tab. Flows across awaits.</summary>
public static class BindingEditorContext
{
    private static readonly System.Threading.AsyncLocal<bool> _keysOnly = new();
    public static bool KeysOnly => _keysOnly.Value;

    public static IDisposable KeysOnlyScope()
    {
        var before = _keysOnly.Value;
        _keysOnly.Value = true;
        return new Restore(before);
    }

    private sealed class Restore(bool before) : IDisposable
    {
        public void Dispose() => _keysOnly.Value = before;
    }
}

/// <summary>One cap on the on-screen keyboard. <see cref="Width"/> is the full footprint in pixels
/// (units × base) — the visual gap is drawn inside it, so every row of the same total units lines up
/// regardless of how many caps it has. Spacers are invisible footprint; modifiers toggle Ctrl/Shift/Alt.</summary>
public partial class KeyCap : ObservableObject
{
    public string Display { get; }
    public string Value { get; }
    public double Width { get; }
    public KeyCapKind Kind { get; }
    public bool IsSpacer => Kind == KeyCapKind.Spacer;
    public bool IsModifier => Kind == KeyCapKind.Modifier;
    public IRelayCommand<KeyCap?> PickCommand { get; }

    [ObservableProperty] private bool _isSelected;

    public KeyCap(string display, string value, double widthUnits, KeyCapKind kind, IRelayCommand<KeyCap?> pickCommand)
    {
        Display = display;
        Value = value;
        Width = widthUnits * 34;
        Kind = kind;
        PickCommand = pickCommand;
    }
}
