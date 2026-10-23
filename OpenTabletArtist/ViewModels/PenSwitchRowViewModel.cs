using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.ViewModels;

// Moved out of TabletDetailViewModel (#751). Nothing changed but the file it lives in: these
// are top-level types that never touched the editor's fields, so the move is mechanical and
// the editor is that much smaller to read.
//
// PenSwitchRowViewModel, one of the tablet editor's row/card types.

public partial class PenSwitchRowViewModel : ObservableObject
{
    private readonly PenSwitchKind _kind;
    private readonly int _penButtonIndex;
    private readonly Func<PenSwitchKind, int, PluginSettingStore?, Task> _applyAsync;
    private readonly Func<PenSwitchRowViewModel, Task>? _chooseAction;
    private PluginSettingStore? _store;

    public PenSwitchKind Kind => _kind;
    public int PenButtonIndex => _penButtonIndex;
    /// <summary>The binding this row currently shows (for the action editor's starting point).</summary>
    public PluginSettingStore? Store => _store;

    public PenSwitchRowViewModel(
        PenSwitchKind kind,
        int penButtonIndex,
        PluginSettingStore? store,
        bool canEdit,
        Func<PenSwitchKind, int, PluginSettingStore?, Task> applyAsync,
        Func<PenSwitchRowViewModel, Task>? chooseAction = null)
    {
        _kind = kind;
        _penButtonIndex = penButtonIndex;
        _applyAsync = applyAsync;
        _chooseAction = chooseAction;
        SectionLabel = kind switch
        {
            PenSwitchKind.Tip => "Pen tip",
            PenSwitchKind.Eraser => "Eraser",
            PenSwitchKind.PenButton => $"Button {penButtonIndex}",
            _ => "SWITCH"
        };
        RefreshFromStore(store, canEdit);
    }

    public string SectionLabel { get; }

    /// <summary>First row in the merged Pen Switches card — its separating divider is hidden.</summary>
    public bool IsFirst { get; set; }

    [ObservableProperty] private string _bindingLabel = "None";
    /// <summary>Plain-language name of the current binding for the simplified card's "Currently: …" line
    /// (e.g. "Mouse button", "Windows Ink (legacy)") — no plugin jargon (#pen-inputs-simplify).</summary>
    [ObservableProperty] private string _currentPlainLabel = "None";
    [ObservableProperty] private PenSwitchBindingMode _mode;
    [ObservableProperty] private bool _canEdit;

    /// <summary>On macOS a pen side switch can do any action (Absolute mode posts it directly; the Windows Ink
    /// limitation of #495 doesn't apply), so its card offers a Wacom-style "Change…" instead of a warning.</summary>
    public bool CanChooseAction => _chooseAction != null && _kind == PenSwitchKind.PenButton && OperatingSystem.IsMacOS();
    public bool ShowChooseAction => CanChooseAction && CanEdit;

    public bool IsRecommended => Mode == PenSwitchBindingMode.Auto && !CanChooseAction;
    public bool IsNotRecommended => Mode != PenSwitchBindingMode.Auto && !CanChooseAction;
    /// <summary>Show the "Use Adaptive" fix only when the switch isn't already on Adaptive and the host
    /// can apply changes.</summary>
    public bool ShowUseAdaptive => CanEdit && Mode != PenSwitchBindingMode.Auto;

    partial void OnModeChanged(PenSwitchBindingMode value)
    {
        OnPropertyChanged(nameof(IsRecommended));
        OnPropertyChanged(nameof(IsNotRecommended));
        OnPropertyChanged(nameof(ShowUseAdaptive));
        SetAutoCommand.NotifyCanExecuteChanged();
    }

    partial void OnCanEditChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowChooseAction));
        OnPropertyChanged(nameof(ShowUseAdaptive));
        SetAutoCommand.NotifyCanExecuteChanged();
    }

    private void RefreshFromStore(PluginSettingStore? store, bool canEdit)
    {
        _store = store;
        CanEdit = canEdit;
        Mode = PenSwitchBinding.DetectMode(store, _kind, _penButtonIndex);
        BindingLabel = PenSwitchBinding.GetDisplayLabel(store, _kind, _penButtonIndex, GetPluginFriendlyName);
        CurrentPlainLabel = PenSwitchBinding.GetPlainCurrentLabel(store, _kind, _penButtonIndex, GetPluginFriendlyName);
        // A keystroke / mouse button / scroll reads as what it does ("Right click", "⌘Z"), not its type.
        if (AuxKeyBinding.ReadBinding(store) is { IsBound: true } action)
            CurrentPlainLabel = AuxKeyBinding.Describe(action);
        else if (Mode == PenSwitchBindingMode.Auto && _kind == PenSwitchKind.PenButton)
            CurrentPlainLabel = _penButtonIndex == 1 ? "Default (right click)" : "Default (middle click)";
        OnPropertyChanged(nameof(ShowChooseAction));
    }

    [RelayCommand]
    private Task ChooseAction() => _chooseAction?.Invoke(this) ?? Task.CompletedTask;

    private static string? GetPluginFriendlyName(string? path) =>
        path == null ? null : AppInfo.PluginManager.GetFriendlyName(path);

    private bool CanSetAuto => CanEdit && Mode != PenSwitchBindingMode.Auto;

    [RelayCommand(CanExecute = nameof(CanSetAuto))]
    private Task SetAuto() =>
        _applyAsync(_kind, _penButtonIndex, PenSwitchBinding.MakeAdaptiveBinding(_kind, _penButtonIndex));
}
