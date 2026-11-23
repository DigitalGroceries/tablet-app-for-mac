using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

// Moved out of TabletDetailViewModel (#751). Nothing changed but the file it lives in: these
// are top-level types that never touched the editor's fields, so the move is mechanical and
// the editor is that much smaller to read.
//
// ButtonBinding, one of the tablet editor's row/card types.

public partial class ButtonBinding : ObservableObject
{
    private readonly Func<int, AuxBinding, Task>? _applyBinding;
    private readonly Func<AuxBinding, string, Task<AuxBinding?>>? _editBinding;
    private AuxBinding _applied;
    private readonly Func<int, bool, Task>? _applyKeyOn;
    private bool _keyOn;

    public ButtonBinding(int index, AuxBinding binding, bool isOtherBinding, string otherLabel,
        bool canEdit, Func<int, AuxBinding, Task>? applyBinding, string? label = null,
        Func<AuxBinding, string, Task<AuxBinding?>>? editBinding = null,
        bool? keyOn = null, bool allButtonsOn = true, Func<int, bool, Task>? applyKeyOn = null)
    {
        _keyOn = keyOn ?? true;
        ShowKeyToggle = keyOn != null;
        AllButtonsOn = allButtonsOn;
        _applyKeyOn = applyKeyOn;
        Index = index;
        _label = label;
        IsOtherBinding = isOtherBinding;
        OtherLabel = otherLabel;
        CanEdit = canEdit;
        _applyBinding = applyBinding;
        _editBinding = editBinding;
        _applied = binding;
        Localizer.LanguageChanged += () =>
        {
            OnPropertyChanged(nameof(Label));
            OnPropertyChanged(nameof(KeyToggleHeader));
            OnPropertyChanged(nameof(KeyToggleTip));
            OnPropertyChanged(nameof(NotSetUpText));
            OnPropertyChanged(nameof(ChooseText));
        };
    }

    // ── TASKS #28: this key's own On/Off (ExpressKeys only; wheel rows pass no keyOn and show no item) ──

    /// <summary>The "•••" menu has the On/Off item (ExpressKeys cards only).</summary>
    public bool ShowKeyToggle { get; }

    /// <summary>The "All buttons" switch is On. While it is Off the per-key item is greyed.</summary>
    public bool AllButtonsOn { get; }

    /// <summary>This key is On. An Off key keeps its shortcut but does nothing.</summary>
    public bool IsKeyOn => _keyOn;

    /// <summary>An Off key is drawn greyed.</summary>
    public double CardOpacity => _keyOn ? 1.0 : 0.45;

    /// <summary>The menu item turns the key the other way: "Turn off" on an On key, "Turn on" on an Off key.</summary>
    public string KeyToggleHeader => Localizer.Te(_keyOn ? "tablet.expresskeys.key_turn_off" : "tablet.expresskeys.key_turn_on",
        _keyOn ? "Turn off" : "Turn on");

    /// <summary>Tooltip of the item while All buttons is Off (the item is disabled then).</summary>
    public string KeyToggleTip => AllButtonsOn ? "" : Localizer.Te("tablet.expresskeys.key_needs_all", "Turn on All buttons first");

    private bool CanToggleKey => AllButtonsOn && ShowKeyToggle && CanEdit;

    [RelayCommand(CanExecute = nameof(CanToggleKey))]
    private async Task ToggleKey()
    {
        if (!CanToggleKey) return;
        _keyOn = !_keyOn;
        OnPropertyChanged(nameof(IsKeyOn));
        OnPropertyChanged(nameof(CardOpacity));
        OnPropertyChanged(nameof(KeyToggleHeader));
        if (_applyKeyOn != null) await _applyKeyOn(Index, _keyOn);
    }

    public int Index { get; }
    private readonly string? _label;
    /// <summary>Row title. Defaults to localised "Button N"; wheel rows pass a custom label (the direction).</summary>
    public string Label => _label ?? ButtonLabel(Index);

    static string ButtonLabel(int n)
    {
        var t = Localizer.T("tablet.pen.button_n");
        if (t == "tablet.pen.button_n") return $"Button {n}";
        return t.Replace("{number}", n.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Read-only summary shown on the card: the friendly name of a binding this editor can't
    /// model, else "Ctrl + Z" / "Left click" / "Scroll up" / "Do nothing".</summary>
    public string Summary => IsOtherBinding && !_applied.IsBound ? OtherLabel : AuxKeyBinding.Describe(_applied);

    /// <summary>False disables the Edit button (read-only host, or button mapping suspended).</summary>
    public bool CanEdit { get; }

    /// <summary>V30 #200 B2 (Zak 23 Nov 2026: users "wouldnt know their tablets buttons, wheels and strips wouldnt do anything
    /// until they are setup"; team vote: do this first): nothing is on this control, so its card says so and offers to
    /// choose, instead of a quiet "Do nothing".</summary>
    public bool IsNotSetUp => CanEdit && _keyOn && !IsOtherBinding && !_applied.IsBound;
    public bool ShowSummary => !IsNotSetUp;
    public string NotSetUpText => Localizer.Te("tablet.not_set_up", "Not set up");
    public string ChooseText => Localizer.Te("tablet.not_set_up.choose", "Choose what it does…");

    /// <summary>True when this button already holds a binding this editor can't model (Windows Ink, an
    /// adaptive binding, or a multi-key macro) — the summary shows its friendly name until it's replaced.
    /// Settable only by <see cref="Clear"/>, which is the one thing that can remove such a binding: the
    /// modal editor cannot, because it hands back Unbound and this row already holds Unbound, so its
    /// "no change" check swallows it.</summary>
    public bool IsOtherBinding { get; private set; }
    public string OtherLabel { get; private set; }

    /// <summary>True while the physical button is held down — highlights the card live.</summary>
    [ObservableProperty] private bool _isPressed;

    /// <summary>Open the modal editor and apply the result — a binding, or <see cref="AuxBinding.Unbound"/>
    /// from Clear. Cancel (null) leaves the binding untouched. Nothing is applied until the dialog
    /// returns, so there's no inline apply-on-change to loop.</summary>
    // CanExecute, not just the guard below: the action is a named menu item now rather than a button the
    // view could disable itself, so a read-only host has to grey it out from here.
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task Edit()
    {
        if (_editBinding == null || !CanEdit) return;
        var result = await _editBinding(_applied, Label);
        if (result is not { } binding) return; // cancelled
        if (binding == _applied) return;        // no change
        var before = _applied;
        _applied = binding;
        OnPropertyChanged(nameof(Summary)); OnPropertyChanged(nameof(IsNotSetUp)); OnPropertyChanged(nameof(ShowSummary));
        OnPropertyChanged(nameof(KeysText));
        ClearCommand.NotifyCanExecuteChanged();
        if (_applyBinding != null) await _applyBinding(Index, binding);
        Services.RadialMenuOverlay.ReplaceLive(before, binding);   // V29 #161: an ExpressKey's open menu follows too
    }

    /// <summary>V29 #158: the shortcut box's text while it is empty.</summary>
    public string KeysHint => Localizer.Te("tablet.ring.keys_hint", "Click, then press the keys");
    public string ClearKeysTip => Localizer.Te("tablet.ring.keys_clear", "Clear the shortcut");

    /// <summary>V29 #158: the keyboard shortcut shown in a ring mode's shortcut box (empty when none is set).</summary>
    public string KeysText => _applied.Kind == AuxKind.Keyboard && _applied.Combo.IsBound ? AuxKeyBinding.Describe(_applied) : "";

    /// <summary>V29 #158 (Zak: shortcuts set in place, no extra window): a shortcut typed into the row's box
    /// (<see cref="Controls.KeyCaptureBox"/>); <see cref="AuxCombo.Unbound"/> empties the row.</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task SetKeys(AuxCombo? combo)
    {
        if (!CanEdit) return;
        var binding = combo is { IsBound: true }
            ? new AuxBinding(AuxKind.Keyboard, combo, AuxKeyBinding.None, AuxKeyBinding.None) : AuxBinding.Unbound;
        if (binding == _applied && !IsOtherBinding) return;
        _applied = binding;
        IsOtherBinding = false;
        OnPropertyChanged(nameof(Summary)); OnPropertyChanged(nameof(IsNotSetUp)); OnPropertyChanged(nameof(ShowSummary));
        OnPropertyChanged(nameof(KeysText));
        ClearCommand.NotifyCanExecuteChanged();
        if (_applyBinding != null) await _applyBinding(Index, binding);
    }

    /// <summary>V29 #153: show <paramref name="binding"/> (set by its owner, e.g. a ring mode's Function menu) without
    /// applying it again; the owner saves it itself.</summary>
    public void ShowQuietly(AuxBinding binding)
    {
        _applied = binding;
        IsOtherBinding = false;
        OnPropertyChanged(nameof(Summary)); OnPropertyChanged(nameof(IsNotSetUp)); OnPropertyChanged(nameof(ShowSummary));
        OnPropertyChanged(nameof(KeysText));
        ClearCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Something is mapped here, so there is something to clear. An unmodellable binding counts:
    /// it is exactly the case the modal editor cannot undo.</summary>
    private bool CanClear => CanEdit && (_applied.IsBound || IsOtherBinding);

    /// <summary>Unmap this row without opening the editor (#wheel-row-actions). Applies the same Unbound
    /// the dialog's Clear does, in one step rather than two.</summary>
    [RelayCommand(CanExecute = nameof(CanClear))]
    private async Task Clear()
    {
        _applied = AuxBinding.Unbound;
        IsOtherBinding = false;
        OtherLabel = "";
        OnPropertyChanged(nameof(Summary)); OnPropertyChanged(nameof(IsNotSetUp)); OnPropertyChanged(nameof(ShowSummary));
        OnPropertyChanged(nameof(KeysText));
        ClearCommand.NotifyCanExecuteChanged();
        if (_applyBinding != null) await _applyBinding(Index, _applied);
    }
}
