using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>One pen button or ExpressKey of the selected table that opens a radial menu (TASKS #63).</summary>
public sealed record RadialOpener(bool IsPenButton, int Index, string Label, string Json)
{
    public override string ToString() => Label;
}

public partial class TabletDetailViewModel
{
    // ── Tablet › Pen › Radial menu (TASKS #63, Zak 20:31) ───────────────────────────────────────────────
    // The radial menu editor inside the page, for the selected application (or All / the selected pen). It edits the menu of
    // a pen button or ExpressKey that opens a radial menu; each change is applied to that table the way the Pen tab's other
    // controls are (so the footer's Save keeps it, like every other tab). Same editor control as Button › Change… › Radial menu.

    /// <summary>How long after the last edit the menu is sent to the driver (so typing a name is not one apply per letter).</summary>
    internal static TimeSpan RadialApplyDelay { get; set; } = TimeSpan.FromMilliseconds(350);

    public ObservableCollection<RadialOpener> RadialOpeners { get; } = new();

    [ObservableProperty] private RadialOpener? _selectedRadialOpener;
    [ObservableProperty] private RadialMenuEditorViewModel? _radialEditor;
    [ObservableProperty] private string _radialTopLine = "";
    [ObservableProperty] private string _radialChooseLabel = "";
    [ObservableProperty] private string _radialUseButtonLabel = "";
    [ObservableProperty] private bool _hasRadialOpener;
    [ObservableProperty] private bool _showRadialChooser;
    [ObservableProperty] private bool _canUseRadialButton;

    /// <summary>True when no pen button or ExpressKey of this table opens the radial menu (the page then offers one).</summary>
    public bool NoRadialOpener => !HasRadialOpener;
    /// <summary>The page can write settings (a live driver connection); false makes the editor read-only.</summary>
    public bool CanEditRadial => _applyAction != null;

    partial void OnHasRadialOpenerChanged(bool value) => OnPropertyChanged(nameof(NoRadialOpener));

    private string _radialAppliedJson = "";
    private bool _radialDirty;
    private bool _radialSelecting;
    private CancellationTokenSource? _radialDelay;
    private Task _radialApply = Task.CompletedTask;

    private static string RadialCanonical(string? json) => RadialMenuJson.Serialize(RadialMenuJson.Parse(json));

    private static string FormatNumbered(string key, string english, int n) =>
        Localizer.Te(key, english).Replace("{number}", n.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);

    private static string RadialPenButtonLabel(int n) => FormatNumbered("radial_tab.pen_button", "Pen button {number}", n);
    private static string RadialExpressKeyLabel(int n) => FormatNumbered("radial_tab.express_key", "ExpressKey {number}", n);

    partial void OnSelectedRadialOpenerChanged(RadialOpener? value)
    {
        if (_radialSelecting) return;       // set by RefreshRadialTab itself
        _ = FlushRadialAsync();             // never lose an edit when the chooser moves on
        RefreshRadialTab(rebuildEditor: true);
    }

    /// <summary>Rebuild the tab's state from the selected table: which buttons open a radial menu, the top line, and the editor.
    /// A pending (not yet applied) edit is never thrown away by a refresh.</summary>
    internal void RefreshRadialTab(bool rebuildEditor = false)
    {
        try
        {
            var bindings = WorkingBindings();
            var found = new List<RadialOpener>();
            for (int i = 0; i < (bindings.PenButtons?.Count ?? 0); i++)
                if (RadialMenuBinding.Is(bindings.PenButtons![i]))
                    found.Add(new RadialOpener(true, i + 1, RadialPenButtonLabel(i + 1), RadialMenuBinding.ReadMenuJson(bindings.PenButtons[i]) ?? ""));
            for (int i = 0; i < (bindings.AuxButtons?.Count ?? 0); i++)
                if (RadialMenuBinding.Is(bindings.AuxButtons![i]))
                    found.Add(new RadialOpener(false, i + 1, RadialExpressKeyLabel(i + 1), RadialMenuBinding.ReadMenuJson(bindings.AuxButtons[i]) ?? ""));

            var previous = SelectedRadialOpener;
            var pick = found.FirstOrDefault(o => previous != null && o.IsPenButton == previous.IsPenButton && o.Index == previous.Index)
                       ?? found.FirstOrDefault();

            _radialSelecting = true;
            try
            {
                RadialOpeners.Clear();
                foreach (var o in found) RadialOpeners.Add(o);
                SelectedRadialOpener = pick;
            }
            finally { _radialSelecting = false; }

            HasRadialOpener = found.Count > 0;
            ShowRadialChooser = found.Count > 1;
            RadialChooseLabel = Localizer.Te("radial_tab.choose", "Menu to edit:");
            RadialTopLine = found.Count > 0
                ? Localizer.Te("radial_tab.opens_with", "Opens with: {buttons}").Replace("{buttons}", string.Join(", ", found.Select(o => o.Label)), StringComparison.Ordinal)
                : Localizer.Te("radial_tab.none", "No pen button opens the radial menu yet");

            // "Use pen button 2": the second button when the pen has one, else the first.
            int count = bindings.PenButtons?.Count ?? 0;
            int use = count >= 2 ? 2 : count;
            CanUseRadialButton = found.Count == 0 && use > 0 && _applyAction != null;
            RadialUseButtonLabel = use > 0 ? FormatNumbered("radial_tab.use_button", "Use pen button {number}", use) : "";
            OnPropertyChanged(nameof(CanEditRadial));

            if (pick == null) { DropRadialEditor(); return; }
            bool same = RadialEditor != null && _radialEditorFor is { } f && f.IsPenButton == pick.IsPenButton && f.Index == pick.Index
                        && RadialCanonical(_radialAppliedJson) == RadialCanonical(pick.Json);
            if (_radialDirty && RadialEditor != null && _radialEditorFor is { } g && g.IsPenButton == pick.IsPenButton && g.Index == pick.Index)
                return;                                   // an edit is on its way to the driver: keep what the user typed
            if (same && !rebuildEditor) return;
            BuildRadialEditor(pick);
        }
        catch (Exception ex)
        {
            AppLog.Warn("Radial menu tab: could not refresh from the selected table.", ex);
        }
    }

    private RadialOpener? _radialEditorFor;

    private void BuildRadialEditor(RadialOpener pick)
    {
        DropRadialEditor();
        var editor = new RadialMenuEditorViewModel(pick.Json, Localizer.Te("radial.title", "Radial menu"));
        _radialAppliedJson = editor.CurrentJson();
        _radialEditorFor = pick;
        _radialDirty = false;
        editor.Changed += OnRadialEdited;
        RadialEditor = editor;
    }

    private void DropRadialEditor()
    {
        if (RadialEditor != null) RadialEditor.Changed -= OnRadialEdited;
        RadialEditor = null;
        _radialEditorFor = null;
        _radialDirty = false;
        _radialAppliedJson = "";
        try { _radialDelay?.Cancel(); } catch (ObjectDisposedException) { }
    }

    private void OnRadialEdited()
    {
        _radialDirty = true;
        try { _radialDelay?.Cancel(); } catch (ObjectDisposedException) { }
        var cts = _radialDelay = new CancellationTokenSource();
        _ = DelayedRadialApplyAsync(cts.Token);
    }

    private async Task DelayedRadialApplyAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(RadialApplyDelay, token);
            await ApplyRadialNowAsync();
        }
        catch (OperationCanceledException) { /* a newer edit restarted the wait */ }
        catch (Exception ex) { AppLog.Warn("Radial menu tab: applying the menu failed.", ex); }
    }

    /// <summary>Send any pending edit now (leaving the tab or the chooser; tests).</summary>
    internal Task FlushRadialAsync()
    {
        try { _radialDelay?.Cancel(); } catch (ObjectDisposedException) { }
        if (!_radialDirty) return _radialApply;
        _radialApply = FlushCoreAsync();
        return _radialApply;
    }

    private async Task FlushCoreAsync()
    {
        try { await ApplyRadialNowAsync(); }
        catch (Exception ex) { AppLog.Warn("Radial menu tab: applying the menu failed.", ex); }
    }

    private async Task ApplyRadialNowAsync()
    {
        var editor = RadialEditor;
        var opener = _radialEditorFor;
        if (editor == null || opener == null) { _radialDirty = false; return; }
        var newJson = editor.CurrentJson();
        var oldJson = _radialAppliedJson;
        if (RadialCanonical(newJson) == RadialCanonical(oldJson)) { _radialDirty = false; return; }

        bool ok = false;
        try { ok = await ReplaceRadialMenuAsync(oldJson, newJson); }
        catch (Exception ex) { AppLog.Warn($"Radial menu tab: sending the menu of {opener.Label} failed.", ex); }

        bool current = ReferenceEquals(editor, RadialEditor);   // the chooser may have moved to another button meanwhile
        if (ok)
        {
            AppLog.Info($"Radial menu tab: the menu of {opener.Label} was changed and applied to the selected table.");
            if (!current) return;
            _radialAppliedJson = newJson;
            // Edited again while it was on its way: send that too.
            if (RadialCanonical(editor.CurrentJson()) != RadialCanonical(newJson))
            { await ApplyRadialNowAsync(); return; }
            _radialDirty = false;
        }
        else
        {
            AppLog.Warn($"Radial menu tab: the change to the menu of {opener.Label} was NOT applied (see the preceding log line for why: page read-only, no button of this table holds that menu any more, a newer edit took over, or the driver refused). The tab shows what is stored again.");
            if (!current) return;
            _radialDirty = false;
            RefreshRadialTab(rebuildEditor: true);
        }
    }

    /// <summary>"Use pen button 2": make that pen button open the radial menu (the default menu), like Change… › Radial menu.</summary>
    [RelayCommand]
    private async Task UseRadialButtonAsync()
    {
        try
        {
            int count = WorkingBindings().PenButtons?.Count ?? 0;
            int use = count >= 2 ? 2 : count;
            if (use <= 0) { AppLog.Warn("Radial menu tab: this pen has no buttons to open the radial menu."); return; }
            AppLog.Info($"Radial menu tab: pen button {use} set to open the radial menu.");
            await ApplyPenSwitchBindingAsync(PenSwitchKind.PenButton, use, RadialMenuBinding.Make());
            RefreshRadialTab(rebuildEditor: true);
        }
        catch (Exception ex)
        {
            AppLog.Warn("Radial menu tab: could not set a pen button to open the radial menu.", ex);
        }
    }
}
