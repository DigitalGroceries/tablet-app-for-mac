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

    private string _spotAppliedJson = "";
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
        if (e.PropertyName is not (nameof(RadialMenuEditorViewModel.Size) or nameof(RadialMenuEditorViewModel.Transparency))) return;
        RadialPictureSize = PictureSizeFor(vm);
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
            // The picture: this menu, or the starting menu (dimmed by the page) when nothing opens one. A slider change that is still on its
            // way to the driver is never thrown away by a refresh: the sliders keep what the user set.
            if (_spotDirty && RadialPreview != null && openers.Count > 0) return;
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
