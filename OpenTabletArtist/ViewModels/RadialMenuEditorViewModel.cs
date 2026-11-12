using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// The radial menu editor (TASKS #42): a picture of the 8 slices; click one to set its name and what it does
/// (a keystroke, a mouse click, a submenu or opening an app). Submenus are edited the same way, one ring at a
/// time, up to <see cref="RadialMenuDef.MaxDepth"/> levels. Works on a copy; Save returns the new menu (JSON),
/// Cancel returns null. Every choice is optional and the whole menu can be reset (Zak: "Art is the ultimate
/// form of customisation").
/// </summary>
public partial class RadialMenuEditorViewModel : ObservableObject
{
    public const int ActNothing = 0, ActKeys = 1, ActClick = 2, ActSubmenu = 3, ActOpen = 4;
    private static readonly string[] ActionCodes =
        { RadialActions.None, RadialActions.Keys, RadialActions.Click, RadialActions.Submenu, RadialActions.Open };

    public static readonly string[] ClickValues = { "Left", "Right", "Middle", "DoubleClick" };

    private RadialMenuDef _top;
    private readonly List<int> _path = new();
    private bool _loading;

    public string Title { get; }
    public string? Result { get; private set; }
    public event Action? CloseRequested;

    /// <summary>Opens the keystroke picker (the binding editor, keyboard only); set by the dialog.</summary>
    public Func<AuxBinding, Task<AuxBinding?>>? PickKeystroke { get; set; }
    /// <summary>Opens a file picker for an app; set by the dialog. Returns the .app path or null.</summary>
    public Func<Task<string?>>? PickApp { get; set; }

    public RadialMenuEditorViewModel(string? menuJson, string title)
    {
        _top = string.IsNullOrWhiteSpace(menuJson) ? RadialMenuDefaults.Create(Localizer.Te) : RadialMenuJson.Parse(menuJson);
        Title = title;
        _size = _top.Size;
        _transparency = _top.Transparency;
        _transparencyInput = _transparency.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _selectedSlice = 0;
        LoadSlice();
        Refresh();
    }

    // ── Texts (8 languages) ───────────────────────────────────────────────────
    public string Heading => Localizer.Te("radial.title", "Radial menu");
    public string HelpText => Localizer.Te("radial.editor_help",
        "Click a slice to choose what it does. Slices you leave empty close the menu when tapped.");
    public string SliceCaption => Localizer.Tf("radial.slice", ("n", SelectedSlice + 1));
    public string LabelCaption => Localizer.Te("radial.label", "Name");
    public string ActionCaption => Localizer.Te("radial.action", "What it does");
    public IReadOnlyList<string> ActionNames { get; } = new[]
    {
        Localizer.Te("radial.action.none", "Nothing (closes the menu)"),
        Localizer.Te("radial.action.keys", "Keystroke"),
        Localizer.Te("radial.action.click", "Mouse click"),
        Localizer.Te("radial.action.submenu", "Submenu (8 more slices)"),
        Localizer.Te("radial.action.open", "Open an app"),
    };
    public IReadOnlyList<string> ClickNames { get; } = new[]
    {
        Localizer.Te("binding.mouse.left", "Left click"),
        Localizer.Te("binding.mouse.right", "Right click"),
        Localizer.Te("binding.mouse.middle", "Middle click"),
        Localizer.Te("binding.mouse.double_click", "Double Click"),
    };
    public string SetKeysLabel => Localizer.Te("radial.set_keys", "Set keystroke…");
    public string ChooseAppLabel => Localizer.Te("radial.choose_app", "Choose app…");
    public string EditSubmenuLabel => Localizer.Te("radial.edit_submenu", "Edit submenu");
    public string BackLabel => Localizer.Te("radial.back", "Back to the menu above");
    public string ClearSliceLabel => Localizer.Te("radial.clear_slice", "Clear slice");
    public string ResetLabel => Localizer.Te("radial.reset", "Reset menu");
    public string SizeCaption => Localizer.Te("radial.size", "Menu size");
    public string SizeSmall => Localizer.Te("radial.size_small", "Small");
    public string SizeLarge => Localizer.Te("radial.size_large", "Large");
    public string TransparencyCaption => Localizer.Te("radial.transparency", "Menu transparency");
    public string TransparencySolid => Localizer.Te("radial.transparency_solid", "Solid");
    public string TransparencyClear => Localizer.Te("radial.transparency_clear", "Clear");
    public string SaveLabel => Localizer.Te("save.button", "Save");
    public string CancelLabel => Localizer.Te("app.cancel", "Cancel");

    // ── The ring on screen ────────────────────────────────────────────────────
    [ObservableProperty] private IReadOnlyList<string> _labels = Array.Empty<string>();
    [ObservableProperty] private IReadOnlyList<bool> _setFlags = Array.Empty<bool>();
    [ObservableProperty] private IReadOnlyList<bool> _submenuFlags = Array.Empty<bool>();
    [ObservableProperty] private string _where = "";
    [ObservableProperty] private bool _inSubmenu;

    // ── The selected slice ────────────────────────────────────────────────────
    [ObservableProperty] private int _selectedSlice;
    [ObservableProperty] private string _sliceLabel = "";
    [ObservableProperty] private int _actionIndex;
    [ObservableProperty] private int _clickIndex;
    [ObservableProperty] private string _keysText = "";
    [ObservableProperty] private string _appText = "";
    [ObservableProperty] private int _size;
    [ObservableProperty] private int _transparency;

    public bool IsKeys => ActionIndex == ActKeys;
    public bool IsClick => ActionIndex == ActClick;
    public bool IsSubmenu => ActionIndex == ActSubmenu;
    public bool IsOpen => ActionIndex == ActOpen;
    /// <summary>Submenus stop at <see cref="RadialMenuDef.MaxDepth"/> levels.</summary>
    public bool CanAddSubmenu => _path.Count + 1 < RadialMenuDef.MaxDepth;

    private RadialMenuDef Level => RadialMenuJson.Level(_top, _path) ?? _top;
    private RadialSlice Slice => Level.Slices[Math.Clamp(SelectedSlice, 0, RadialMenuDef.SliceCount - 1)];

    /// <summary>The menu as it stands now (tests).</summary>
    public RadialMenuDef Menu => RadialMenuJson.Normalize(_top);

    /// <summary>Open on a given slice of a given ring (the radial menu's pencil: edit the slice just tapped).</summary>
    public void GoTo(IReadOnlyList<int> path, int slice)
    {
        foreach (var i in path)
        {
            SelectSlice(i);
            if (Slice.Action != RadialActions.Submenu) break;
            EditSubmenu();
        }
        SelectSlice(slice);
    }

    public void SelectSlice(int i)
    {
        if (i < 0 || i >= RadialMenuDef.SliceCount) return;
        SelectedSlice = i;
    }

    partial void OnSelectedSliceChanged(int value) => LoadSlice();

    private void LoadSlice()
    {
        _loading = true;
        var s = Slice;
        SliceLabel = s.Label;
        ActionIndex = Math.Max(0, Array.IndexOf(ActionCodes, s.Action));
        ClickIndex = Math.Max(0, Array.IndexOf(ClickValues, s.Click ?? "Left"));
        KeysText = DescribeKeys(s.Keys);
        AppText = s.App ?? "";
        _loading = false;
        OnPropertyChanged(nameof(SliceCaption));
        NotifyActionFlags();
    }

    partial void OnSliceLabelChanged(string value)
    {
        if (_loading) return;
        Slice.Label = value ?? "";
        Refresh();
    }

    partial void OnActionIndexChanged(int value)
    {
        NotifyActionFlags();
        if (_loading) return;
        var s = Slice;
        var action = ActionCodes[Math.Clamp(value, 0, ActionCodes.Length - 1)];
        if (action == RadialActions.Submenu && !CanAddSubmenu) { ActionIndex = Array.IndexOf(ActionCodes, s.Action); return; }
        s.Action = action;
        if (action == RadialActions.Click && string.IsNullOrEmpty(s.Click)) s.Click = ClickValues[Math.Max(0, ClickIndex)];
        if (action == RadialActions.Submenu && s.Submenu == null) s.Submenu = new RadialMenuDef();
        Refresh();
    }

    partial void OnClickIndexChanged(int value)
    {
        if (_loading || value < 0) return;
        Slice.Click = ClickValues[Math.Clamp(value, 0, ClickValues.Length - 1)];
        if (string.IsNullOrWhiteSpace(Slice.Label)) { Slice.Label = ClickNames[value]; SliceLabel = Slice.Label; }
        Refresh();
    }

    partial void OnAppTextChanged(string value)
    {
        if (_loading) return;
        Slice.App = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        Refresh();
    }

    partial void OnSizeChanged(int value)
    {
        _top.Size = Math.Clamp(value, RadialMenuDef.MinSize, RadialMenuDef.MaxSize);
        OnPropertyChanged(nameof(PreviewDiameter));
        OnPropertyChanged(nameof(SizeText));
    }

    partial void OnTransparencyChanged(int value)
    {
        _top.Transparency = Math.Clamp(value, RadialMenuDef.MinTransparency, RadialMenuDef.MaxTransparency);
        OnPropertyChanged(nameof(TransparencyText));
        _transparencyInput = Math.Clamp(value, RadialMenuDef.MinTransparency, RadialMenuDef.MaxTransparency)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        OnPropertyChanged(nameof(TransparencyInput));
    }

    private string _transparencyInput = "";

    /// <summary>The transparency as typed in the number box beside the slider (TASKS #59, Zak 18:21: also editable from the
    /// keyboard). Whatever is committed (Enter or leaving the box) is read as a number, "40", "40%" or "40,5", clamped to
    /// 0-100 and applied, so the slider and the live preview follow; text that is not a number puts the old value back.
    /// The box always ends up showing the real value.</summary>
    public string TransparencyInput
    {
        get => _transparencyInput;
        set
        {
            if (TryParsePercent(value, out var v)) Transparency = v;
            // Show the real value even when nothing changed (clamped, "abc", "40%").
            _transparencyInput = Math.Clamp(Transparency, RadialMenuDef.MinTransparency, RadialMenuDef.MaxTransparency)
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
            OnPropertyChanged(nameof(TransparencyInput));
        }
    }

    /// <summary>Reads a typed percent: optional % sign, comma or dot, rounded and clamped to the transparency range.</summary>
    public static bool TryParsePercent(string? text, out int percent)
    {
        percent = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim().TrimEnd('%').Trim().Replace(',', '.');
        if (!double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d)
            || double.IsNaN(d) || double.IsInfinity(d)) return false;
        percent = (int)Math.Round(Math.Clamp(d, RadialMenuDef.MinTransparency, RadialMenuDef.MaxTransparency), MidpointRounding.AwayFromZero);
        return true;
    }

    /// <summary>The transparency shown as a number next to its slider, "70%".</summary>
    public string TransparencyText => Math.Clamp(Transparency, RadialMenuDef.MinTransparency, RadialMenuDef.MaxTransparency)
        .ToString(System.Globalization.CultureInfo.InvariantCulture) + "%";

    /// <summary>The size shown as a number next to the slider (1-50).</summary>
    public string SizeText => Math.Clamp(Size, RadialMenuDef.MinSize, RadialMenuDef.MaxSize).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Width and height in points of the live preview ring: the same size the menu has on screen
    /// (50 gives 320, 1 gives 180, 100 gives 460), so the slider shows what you will get.</summary>
    public double PreviewDiameter => 2 * RadialMenuGeometry.OuterRadius(Size);

    private void NotifyActionFlags()
    {
        OnPropertyChanged(nameof(IsKeys));
        OnPropertyChanged(nameof(IsClick));
        OnPropertyChanged(nameof(IsSubmenu));
        OnPropertyChanged(nameof(IsOpen));
    }

    private void Refresh()
    {
        var level = Level;
        Labels = level.Slices.Select(s => s.Label).ToList();
        SetFlags = level.Slices.Select(s => s.IsSet).ToList();
        SubmenuFlags = level.Slices.Select(s => s.Action == RadialActions.Submenu).ToList();
        InSubmenu = _path.Count > 0;
        var names = new List<string> { Localizer.Te("radial.top", "Top menu") };
        var cur = _top;
        foreach (var i in _path)
        {
            var name = cur.Slices[i].Label;
            names.Add(string.IsNullOrWhiteSpace(name) ? Localizer.Tf("radial.slice", ("n", i + 1)) : name);
            cur = cur.Slices[i].Submenu ?? cur;
        }
        Where = string.Join(" › ", names);
        OnPropertyChanged(nameof(CanAddSubmenu));
    }

    private static string DescribeKeys(string? keys)
    {
        if (string.IsNullOrWhiteSpace(keys)) return Localizer.Te("radial.no_keys", "No keystroke yet");
        var combo = AuxKeyBinding.ParseKeys(keys);
        return combo is { IsBound: true }
            ? AuxKeyBinding.Describe(new AuxBinding(AuxKind.Keyboard, combo, AuxKeyBinding.None, AuxKeyBinding.None))
            : keys;
    }

    [RelayCommand]
    private async Task SetKeys()
    {
        if (PickKeystroke == null) return;
        var s = Slice;
        var combo = AuxKeyBinding.ParseKeys(s.Keys) ?? AuxCombo.Unbound;
        var initial = combo.IsBound
            ? new AuxBinding(AuxKind.Keyboard, combo, AuxKeyBinding.None, AuxKeyBinding.None)
            : AuxBinding.Unbound;
        AuxBinding? result;
        using (BindingEditorContext.KeysOnlyScope()) result = await PickKeystroke(initial);
        if (result is not { } b) return;
        if (b.Kind == AuxKind.Keyboard && b.Combo.IsBound)
        {
            s.Action = RadialActions.Keys;
            s.Keys = AuxKeyBinding.FormatKeys(b.Combo);
            if (string.IsNullOrWhiteSpace(s.Label)) s.Label = AuxKeyBinding.Describe(b);
        }
        else if (b.Kind == AuxKind.Mouse && !string.IsNullOrEmpty(b.MouseButton) && Array.IndexOf(ClickValues, b.MouseButton) >= 0)
        {
            s.Action = RadialActions.Click;
            s.Click = b.MouseButton;
            if (string.IsNullOrWhiteSpace(s.Label)) s.Label = AuxKeyBinding.Describe(b);
        }
        else if (!b.IsBound)
        {
            s.Keys = null;
        }
        LoadSlice();
        Refresh();
    }

    [RelayCommand]
    private async Task ChooseApp()
    {
        if (PickApp == null) return;
        var path = await PickApp();
        if (string.IsNullOrWhiteSpace(path)) return;
        var s = Slice;
        s.Action = RadialActions.Open;
        s.App = path;
        if (string.IsNullOrWhiteSpace(s.Label))
            s.Label = System.IO.Path.GetFileNameWithoutExtension(path.TrimEnd('/'));
        LoadSlice();
        Refresh();
    }

    [RelayCommand]
    private void EditSubmenu()
    {
        var s = Slice;
        if (s.Action != RadialActions.Submenu || !CanAddSubmenu) return;
        s.Submenu ??= new RadialMenuDef();
        s.Submenu = RadialMenuJson.Normalize(s.Submenu);
        _path.Add(SelectedSlice);
        SelectedSlice = 0;
        LoadSlice();
        Refresh();
    }

    [RelayCommand]
    private void Back()
    {
        if (_path.Count == 0) return;
        var from = _path[^1];
        _path.RemoveAt(_path.Count - 1);
        SelectedSlice = from;
        LoadSlice();
        Refresh();
    }

    [RelayCommand]
    private void ClearSlice()
    {
        var level = Level;
        level.Slices[SelectedSlice] = new RadialSlice();
        LoadSlice();
        Refresh();
    }

    [RelayCommand]
    private void Reset()
    {
        _top = RadialMenuDefaults.Create(Localizer.Te);
        _path.Clear();
        Size = _top.Size;
        Transparency = _top.Transparency;
        SelectedSlice = 0;
        LoadSlice();
        Refresh();
    }

    /// <summary>The menu as it is now (JSON), with the size and transparency the sliders show. TASKS #63: the Pen page's sliders use it.</summary>
    public string CurrentJson()
    {
        _top.Size = Math.Clamp(Size, RadialMenuDef.MinSize, RadialMenuDef.MaxSize);
        _top.Transparency = Math.Clamp(Transparency, RadialMenuDef.MinTransparency, RadialMenuDef.MaxTransparency);
        return RadialMenuJson.Serialize(_top);
    }

    [RelayCommand]
    private void Save()
    {
        _top.Size = Size;
        _top.Transparency = Transparency;
        Result = RadialMenuJson.Serialize(_top);
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        CloseRequested?.Invoke();
    }
}
