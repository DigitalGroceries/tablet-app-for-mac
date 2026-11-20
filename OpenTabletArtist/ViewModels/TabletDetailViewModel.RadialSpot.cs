using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

public partial class TabletDetailViewModel
{
    // ── Tablet › Pen: "Radial Menu Settings" (TASKS #63, Zak 20:53) ─────────────────────────────────────
    // The radial menu's picture sits in the middle of the Pen page (between the pen drawing and the Button cards) and opens the
    // radial menu editor for the selected table (All, or one application, or the selected pen) when it is clicked. The editor is the
    // one of Button › Change… › Radial menu (RadialMenuEditorDialog); the new menu goes in through ReplaceRadialMenuAsync, the same
    // path as the radial menu's own pencil. With no pen button on the radial menu the picture is dimmed and offers "Use pen button 2".

    [ObservableProperty] private RadialMenuEditorViewModel? _radialPreview;
    [ObservableProperty] private string _radialSpotHeading = "";
    [ObservableProperty] private string _radialSpotLine = "";
    [ObservableProperty] private string _radialUseButtonLabel = "";
    [ObservableProperty] private bool _hasRadialOpener;
    [ObservableProperty] private bool _canUseRadialButton;

    /// <summary>1 normally; the picture is dimmed while no pen button opens the radial menu.</summary>
    public double RadialSpotOpacity => HasRadialOpener ? 1.0 : 0.35;

    /// <summary>True when no pen button of this table opens the radial menu.</summary>
    public bool NoRadialOpener => !HasRadialOpener;
    /// <summary>The picture opens the editor only when the page can write settings (a driver connection).</summary>
    public bool CanEditRadial => HasRadialOpener && _applyAction != null;

    partial void OnHasRadialOpenerChanged(bool value) { OnPropertyChanged(nameof(NoRadialOpener)); OnPropertyChanged(nameof(RadialSpotOpacity)); OnPropertyChanged(nameof(CanEditRadial)); }

    private string? _radialOpenerJson;

    // ── Menu transparency and Menu size under the picture (TASKS #63, Zak 20:59) ─────────────────────────
    // The sliders are the editor's own view model (RadialPreview: Transparency, TransparencyInput, Size), so the number box, clamping, "40%",
    // comma / dot and Up / Down behave exactly as in the editor. A change shows in the picture at once and is applied to the table a moment
    // later (one apply per drag, not one per tick) through ReplaceRadialMenuAsync; the footer's Save keeps it like the rest of the page.

    /// <summary>How long after the last slider change the menu is sent to the driver.</summary>
    internal static TimeSpan RadialApplyDelay { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>v27 (Zak 16:35): the picture is the menu at its real size, the same edge as the menu window (2 x outer radius + 4):
    /// 324 points at the largest menu (<see cref="RadialMenuDef.MaxDiameter"/> 320), smaller with the size slider.</summary>
    public const double RadialPictureMax = RadialMenuDef.MaxDiameter + 4;
    /// <summary>The fixed square kept for the picture on the page: the largest picture (the button around it has no padding).</summary>
    public const double RadialSpotAreaSize = RadialPictureMax;
    [ObservableProperty] private double _radialPictureSize = RadialPictureMax;

    /// <summary>The sliders only work when a pen button opens the menu and the page can write settings.</summary>
    public bool CanEditRadialSliders => CanEditRadial;

    /// <summary>V29 #141: the floating keyboard's transparency and (#162) size: app-wide settings, in the Pen page's Floating keyboard
    /// section (#163).</summary>
    public FloatingKeyboardLookViewModel KeyboardLook { get; } = new();

    // ── V29 #163 (Zak 20 Nov ~09:10, screenshot "Put the keyboard settings menu and how it is triggered here"): the Floating keyboard
    // section under Movement / Pen input. Its slice row puts the keyboard on a slice of the radial menu shown above (the menu of the
    // first pen button that opens it), through ReplaceRadialMenuAsync like the sliders, so an open menu shows it at once (#161).
    [ObservableProperty] private IReadOnlyList<string> _keyboardSlots = Array.Empty<string>();
    [ObservableProperty] private int _keyboardSlot = -1;
    /// <summary>"The keyboard is on slice 7." when it is on one, or why the row can't be used.</summary>
    [ObservableProperty] private string _keyboardStatus = "";

    // ── V29 #160 (Zak 20 Nov ~09:18-09:24): what holding pen button 1 does, in the Floating keyboard section. ──
    /// <summary>"Open the keyboard" (0, factory: a quick press right-clicks when let go) or "Drag with the right button" (1: the
    /// button's Default, held down like a mouse button). -1 while pen button 1 does something else (set with Change…).</summary>
    [ObservableProperty] private int _holdChoice = -1;
    /// <summary>Pen button 1 opens the keyboard when held: the line under the pen tip says so.</summary>
    [ObservableProperty] private bool _holdOpensKeyboard;
    [ObservableProperty] private bool _hasPenButton1;
    private bool _holdQuiet;

    public IReadOnlyList<string> HoldChoices => new[]
    {
        Localizer.Te("keyboard.hold_opens", "Open the keyboard"),
        Localizer.Te("keyboard.hold_drags", "Drag with the right button"),
    };
    public string HoldCaption => Localizer.Te("keyboard.hold_caption", "Holding pen button 1");
    public string HoldNote => Localizer.Te("keyboard.hold_note", "A quick press right-clicks. Holding it about half a second opens or closes the keyboard.");
    public string HoldOtherNote => Localizer.Te("keyboard.hold_other", "Pen button 1 does something else now (set with Change…). Pick one here to change it.");
    /// <summary>Under the pen tip's "Default setting" (Zak's words, with long press).</summary>
    public string PenTipHoldLine => Localizer.Te("tablet.pen.hold_keyboard_note", "Long press of Pen button 1 opens the Keyboard");
    public bool HoldIsOther => HasPenButton1 && HoldChoice < 0;
    public bool CanChooseHold => HasPenButton1 && _applyAction != null;

    partial void OnHoldChoiceChanged(int value)
    {
        OnPropertyChanged(nameof(HoldIsOther));
        if (_holdQuiet || value < 0) return;
        _ = ApplyHoldChoiceAsync(value);
    }

    private void RefreshHoldChoice(OpenTabletDriver.Desktop.Profiles.BindingSettings bindings)
    {
        var store = bindings.PenButtons.Count > 0 ? bindings.PenButtons[0] : null;
        HasPenButton1 = bindings.PenButtons.Count > 0;
        HoldOpensKeyboard = HoldKeyboardBinding.Is(store);
        _holdQuiet = true;
        try
        {
            HoldChoice = !HasPenButton1 ? -1
                : HoldOpensKeyboard ? 0
                : PenSwitchBinding.DetectMode(store, PenSwitchKind.PenButton, 1) == PenSwitchBindingMode.Auto ? 1 : -1;
        }
        finally { _holdQuiet = false; }
        OnPropertyChanged(nameof(HoldIsOther));
        OnPropertyChanged(nameof(CanChooseHold));
    }

    private async Task ApplyHoldChoiceAsync(int choice)
    {
        try
        {
            if (_applyAction == null || !HasPenButton1) return;
            var store = choice == 0 ? HoldKeyboardBinding.Make() : PenSwitchBinding.MakeAdaptiveBinding(PenSwitchKind.PenButton, 1);
            AppLog.Info($"Floating keyboard: holding pen button 1 now {(choice == 0 ? "opens the keyboard (a quick press right-clicks)" : "drags with the right button")}.");
            await ApplyPenSwitchBindingAsync(PenSwitchKind.PenButton, 1, store);
            RefreshPenSwitchRows();
        }
        catch (Exception ex)
        {
            AppLog.Warn("Floating keyboard: changing what holding pen button 1 does failed.", ex);
        }
    }

    /// <summary>The slice row works when a pen button opens the radial menu and the page can write settings.</summary>
    public bool CanPutKeyboard => CanEditRadial;

    private void RefreshKeyboardSlots()
    {
        if (_radialOpenerJson == null)
        {
            KeyboardSlots = Array.Empty<string>();
            KeyboardSlot = -1;
            KeyboardStatus = Localizer.Te("keyboard.no_menu", "No pen button opens the radial menu yet, so there is no slice to put the keyboard on.");
        }
        else
        {
            KeyboardSlots = FloatingKeyboardPlacement.Slots(_radialOpenerJson);
            KeyboardSlot = FloatingKeyboardPlacement.SuggestedSlot(_radialOpenerJson);
            KeyboardStatus = FloatingKeyboardPlacement.Status(_radialOpenerJson);
        }
        OnPropertyChanged(nameof(CanPutKeyboard));
    }

    /// <summary>"Put the keyboard there": the keyboard goes on the chosen slice (and off any other) of the menu shown above.</summary>
    [RelayCommand]
    private async Task PutKeyboardAsync()
    {
        try
        {
            await FlushRadialSpotAsync();   // a slider change still waiting to be sent goes first
            var json = _radialOpenerJson;
            if (json == null || _applyAction == null) { AppLog.Warn("Floating keyboard: no pen button opens the radial menu (or the page is read-only), so the keyboard can't be put on a slice."); return; }
            var current = RadialMenuJson.Serialize(RadialMenuJson.Parse(json));
            int slot = KeyboardSlot;
            var result = FloatingKeyboardPlacement.Put(current, slot, out var was);
            if (result == current) { RefreshKeyboardSlots(); return; }
            if (await ReplaceRadialMenuAsync(current, result))
                AppLog.Info($"Floating keyboard: put on slice {Math.Clamp(slot, 0, 7) + 1} of the radial menu" + (was.Length > 0 ? $" (it was '{was}')." : "."));
            else
                AppLog.Warn("Floating keyboard: putting it on a slice was NOT applied (see the preceding log line).");
            RefreshRadialSpot();
        }
        catch (Exception ex)
        {
            AppLog.Warn("Floating keyboard: putting it on a slice failed.", ex);
        }
    }

    private string _spotAppliedJson = "";
    /// <summary>V29 #166: when the user last moved a slider or typed in a box under the picture (Environment.TickCount64).</summary>
    private long _spotLastEdit;
    /// <summary>V29 #166: for this long after the last slider move, a page refresh never puts older stored values back.</summary>
    internal static int SpotEditQuietMs { get; set; } = 1500;
    /// <summary>V29 #166: the table (tablet, application, pen) the picture was built for.</summary>
    private string _spotTable = "";
    private bool _spotDirty;
    private CancellationTokenSource? _spotDelay;
    private Task _spotApply = Task.CompletedTask;

    private static string RadialCanonical(string? json) => RadialMenuJson.Serialize(RadialMenuJson.Parse(json));

    partial void OnRadialPreviewChanging(RadialMenuEditorViewModel? value)
    {
        if (RadialPreview != null) RadialPreview.PropertyChanged -= OnRadialPreviewPropertyChanged;
    }

    partial void OnRadialPreviewChanged(RadialMenuEditorViewModel? value)
    {
        if (value == null) return;
        _spotAppliedJson = value.CurrentJson();
        RadialPictureSize = PictureSizeFor(value);
        value.PropertyChanged += OnRadialPreviewPropertyChanged;
    }

    /// <summary>The picture's edge: exactly the menu window's edge (see RadialMenuOverlay.Show: 2 x outer radius + 4), never above the largest.</summary>
    internal static double PictureSizeFor(RadialMenuEditorViewModel vm) =>
        Math.Min(RadialPictureMax, vm.PreviewDiameter + 4);

    private void OnRadialPreviewPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not RadialMenuEditorViewModel vm || !ReferenceEquals(vm, RadialPreview)) return;
        if (e.PropertyName is not (nameof(RadialMenuEditorViewModel.Size) or nameof(RadialMenuEditorViewModel.Transparency)
            or nameof(RadialMenuEditorViewModel.PaletteSize))) return;   // V29 #142: the palette's size is saved the same way
        RadialPictureSize = PictureSizeFor(vm);
        _spotLastEdit = Environment.TickCount64;   // V29 #166
        if (_applyAction == null || _radialOpenerJson == null) return;   // nothing to write to (the page is read-only or no button opens the menu)
        _spotDirty = true;
        try { _spotDelay?.Cancel(); } catch (ObjectDisposedException) { }
        var cts = _spotDelay = new CancellationTokenSource();
        _ = DelayedSpotApplyAsync(cts.Token);
    }

    private async Task DelayedSpotApplyAsync(CancellationToken token)
    {
        try { await Task.Delay(RadialApplyDelay, token); await ApplySpotNowAsync(); }
        catch (OperationCanceledException) { /* a newer change restarted the wait */ }
        catch (Exception ex) { AppLog.Warn("Radial menu settings: applying the size / transparency failed.", ex); }
    }

    /// <summary>Send a pending slider change now (before the editor opens; tests).</summary>
    internal Task FlushRadialSpotAsync()
    {
        try { _spotDelay?.Cancel(); } catch (ObjectDisposedException) { }
        if (!_spotDirty) return _spotApply;
        return _spotApply = FlushSpotCoreAsync();
    }

    private async Task FlushSpotCoreAsync()
    {
        try { await ApplySpotNowAsync(); }
        catch (Exception ex) { AppLog.Warn("Radial menu settings: applying the size / transparency failed.", ex); }
    }

    private async Task ApplySpotNowAsync()
    {
        var preview = RadialPreview;
        if (preview == null) { _spotDirty = false; return; }
        var newJson = preview.CurrentJson();
        var oldJson = _spotAppliedJson;
        if (RadialCanonical(newJson) == RadialCanonical(oldJson)) { _spotDirty = false; return; }
        bool ok = false;
        try { ok = await ReplaceRadialMenuAsync(oldJson, newJson); }
        catch (Exception ex) { AppLog.Warn("Radial menu settings: sending the changed size / transparency failed.", ex); }
        bool current = ReferenceEquals(preview, RadialPreview);   // a refresh may have built a new picture meanwhile
        if (ok)
        {
            AppLog.Info("Radial menu settings: the menu size / transparency was changed and applied (the footer's Save keeps it).");
            if (!current) return;
            _spotAppliedJson = newJson;
            _radialOpenerJson = newJson;
            RefreshKeyboardSlots();
            if (RadialCanonical(preview.CurrentJson()) != RadialCanonical(newJson)) { await ApplySpotNowAsync(); return; }   // moved again meanwhile
            _spotDirty = false;
        }
        else
        {
            AppLog.Warn("Radial menu settings: the size / transparency change was NOT applied (see the preceding log line for why: the page is read-only, or no button of this table holds that menu any more). The sliders show what is stored again.");
            _spotDirty = false;
            if (current) RefreshRadialSpot();
        }
    }

    private static string FormatNumbered(string key, string english, int n) =>
        Localizer.Te(key, english).Replace("{number}", n.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);

    /// <summary>The pen button "Use pen button N" would take: the second when the pen has two, else the first (0 = no buttons).</summary>
    private int RadialUseButtonNumber() { int c = WorkingBindings().PenButtons?.Count ?? 0; return c >= 2 ? 2 : c; }

    /// <summary>Rebuild the picture, its line and the button from the selected table. Never throws; a failure is logged.</summary>
    internal void RefreshRadialSpot()
    {
        try
        {
            var bindings = WorkingBindings();
            var openers = new List<(int number, string json)>();
            for (int i = 0; i < (bindings.PenButtons?.Count ?? 0); i++)
                if (RadialMenuBinding.Is(bindings.PenButtons![i]))
                    openers.Add((i + 1, RadialMenuBinding.ReadMenuJson(bindings.PenButtons[i]) ?? ""));

            RadialSpotHeading = Localizer.Te("radial_spot.heading", "Radial Menu Settings");
            HasRadialOpener = openers.Count > 0;
            int use = RadialUseButtonNumber();
            CanUseRadialButton = openers.Count == 0 && use > 0 && _applyAction != null;
            RadialUseButtonLabel = use > 0 ? FormatNumbered("radial_spot.use_button", "Use pen button {number}", use) : "";
            OnPropertyChanged(nameof(CanEditRadial));
            if (openers.Count > 0)
            {
                _radialOpenerJson = openers[0].json;
                RadialSpotLine = Localizer.Te("radial_spot.opens_with", "Opens with: {buttons}")
                    .Replace("{buttons}", string.Join(", ", openers.Select(o => FormatNumbered("radial_spot.pen_button", "Pen button {number}", o.number))), StringComparison.Ordinal);
            }
            else
            {
                _radialOpenerJson = null;
                RadialSpotLine = Localizer.Te("radial_spot.none", "No pen button opens the radial menu yet");
            }
            OnPropertyChanged(nameof(CanEditRadialSliders));
            RefreshKeyboardSlots();   // V29 #163
            // The picture: this menu, or the starting menu (dimmed by the page) when nothing opens one. A slider change that is still on its
            // way to the driver is never thrown away by a refresh: the sliders keep what the user set.
            if (_spotDirty && RadialPreview != null && openers.Count > 0) return;
            // V29 #166 (Zak 20 Nov ~09:45: "The transparency and the size is out of synk and jump on different values than what I
            // put them"): a refresh that arrives while the user is still moving a slider (an earlier value was just applied, the
            // next one not yet) rebuilt the sliders from the stored menu, so they jumped back. The picture is kept when it already
            // shows the stored menu, or when a slider moved in the last moment (the pending apply brings the two together).
            var table = $"{_profile.Tablet}|{SelectedAppBundleId}|{SelectedPenKey}";
            if (RadialPreview != null && openers.Count > 0 && table == _spotTable)   // another table always gets its own picture
            {
                bool same = RadialCanonical(RadialPreview.CurrentJson()) == RadialCanonical(_radialOpenerJson);
                bool justMoved = Environment.TickCount64 - _spotLastEdit < SpotEditQuietMs;
                if (same || justMoved) return;
            }
            _spotTable = table;
            RadialPreview = new RadialMenuEditorViewModel(_radialOpenerJson, Localizer.Te("radial.title", "Radial menu"));
        }
        catch (Exception ex)
        {
            AppLog.Warn("Radial menu settings: could not refresh the picture from the selected table.", ex);
        }
    }

    /// <summary>The picture was clicked: open the editor on the menu of the first pen button that opens it. <paramref name="showEditor"/>
    /// gets the current menu (JSON) and returns the new one, or null on Cancel (the page passes RadialMenuEditorDialog.ShowAsync).
    /// Returns true when a changed menu was applied. Every failure is logged.</summary>
    internal async Task<bool> EditRadialMenuAsync(Func<string, Task<string?>> showEditor)
    {
        await FlushRadialSpotAsync();   // a slider change still waiting to be sent goes first
        var json = _radialOpenerJson;
        if (json == null) { AppLog.Warn("Radial menu settings: no pen button opens the radial menu, so there is no menu to edit."); return false; }
        if (_applyAction == null) { AppLog.Warn("Radial menu settings: the page is read-only (no driver connection), so the menu can't be edited."); return false; }
        try
        {
            var current = RadialMenuJson.Serialize(RadialMenuJson.Parse(json));
            var result = await showEditor(current);
            if (result == null || RadialMenuJson.Serialize(RadialMenuJson.Parse(result)) == current) return false;   // cancelled / unchanged
            if (await ReplaceRadialMenuAsync(current, result))
            {
                AppLog.Info("Radial menu settings: the radial menu was changed (the footer's Save keeps it).");
                RefreshRadialSpot();
                return true;
            }
            AppLog.Warn("Radial menu settings: the change was NOT applied (no button of this table holds that menu any more, or the driver refused it; see the preceding log line).");
            RefreshRadialSpot();
            return false;
        }
        catch (Exception ex)
        {
            AppLog.Warn("Radial menu settings: editing the radial menu failed.", ex);
            return false;
        }
    }

    /// <summary>"Use pen button 2": that pen button opens the radial menu (the default menu), like Change… › Radial menu.</summary>
    [RelayCommand]
    private async Task UseRadialButtonAsync()
    {
        try
        {
            int use = RadialUseButtonNumber();
            if (use <= 0) { AppLog.Warn("Radial menu settings: this pen has no buttons to open the radial menu."); return; }
            AppLog.Info($"Radial menu settings: pen button {use} set to open the radial menu.");
            await ApplyPenSwitchBindingAsync(PenSwitchKind.PenButton, use, RadialMenuBinding.Make());
            RefreshRadialSpot();
        }
        catch (Exception ex)
        {
            AppLog.Warn("Radial menu settings: could not set a pen button to open the radial menu.", ex);
        }
    }
}
