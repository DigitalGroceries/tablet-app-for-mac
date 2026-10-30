using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletDriver.Desktop.Profiles;

namespace OpenTabletArtist.ViewModels;

/// <summary>Application-row (Wacom-style per-app binding profiles). Partial of
/// <see cref="TabletDetailViewModel"/>.</summary>
public partial class TabletDetailViewModel
{
    /// <summary>Null = All (or All other). Otherwise the selected app's bundle id.</summary>
    [ObservableProperty] private string? _selectedAppBundleId;

    /// <summary>Listed app tiles (not including All).</summary>
    public ObservableCollection<AppProfileTile> AppProfiles { get; } = new();

    /// <summary>True when at least one app profile exists — All's label becomes "All other".</summary>
    public bool HasAppProfiles => AppProfiles.Count > 0;

    /// <summary>All tile label: "All" or "All other".</summary>
    public string AllAppLabel => HasAppProfiles
        ? Localizer.T("tablet.app.all_other")
        : Localizer.T("tablet.app.all");

    /// <summary>Remove is enabled when a specific app tile is selected.</summary>
    public bool CanRemoveApp => !string.IsNullOrEmpty(SelectedAppBundleId);

    /// <summary>v1: Mapping / pressure stay on All; show a short note when editing an app.</summary>
    public bool ShowPerAppBindingsOnlyNote => !string.IsNullOrEmpty(SelectedAppBundleId);

    partial void OnSelectedAppBundleIdChanged(string? value)
    {
        OnPropertyChanged(nameof(CanRemoveApp));
        OnPropertyChanged(nameof(ShowPerAppBindingsOnlyNote));
        // Rebuild ExpressKeys / pen / wheel rows from the selected table.
        RefreshBindingEditorsFromSelection();
    }

    /// <summary>Bindings the ExpressKeys / pen / ring editors should show and write.</summary>
    internal BindingSettings WorkingBindings()
    {
        // Version 14: an extra pen's own settings come first (its tile is only selectable on the Pen tool).
        if (!string.IsNullOrEmpty(SelectedPenKey)
            && PerPenStore.Find(_profile.Tablet ?? "", SelectedPenKey) is { } pen)
            return pen.Bindings;
        if (!string.IsNullOrEmpty(SelectedAppBundleId)
            && PerAppBindingStore.Find(_profile.Tablet ?? "", SelectedAppBundleId!) is { } app)
            return app.Bindings;
        return _profile.BindingSettings;
    }

    internal void LoadAppProfiles()
    {
        AppProfiles.Clear();
        foreach (var p in PerAppBindingStore.List(_profile.Tablet ?? ""))
            AppProfiles.Add(AppProfileTile.From(p));
        OnPropertyChanged(nameof(HasAppProfiles));
        OnPropertyChanged(nameof(AllAppLabel));
        MigrateLegacyAppRingModes();
        // Keep All bindings stash fresh on first open.
        PerAppBindingRuntime.SyncAllBindings(_profile.Tablet ?? "", _profile.BindingSettings);
    }

    /// <summary>Version 16 BETA 2: an app's table with ring modes written by Version 16 BETA 1 or earlier runs
    /// them backwards (see RingModeBinding.IsLegacy). Rewrite them from the app's mode list (or All's, which
    /// the app's table was copied from) so the app is right even before its tile is opened. Idempotent: a
    /// rewritten table is no longer legacy.</summary>
    internal void MigrateLegacyAppRingModes()
    {
        var tablet = _profile.Tablet ?? "";
        if (RingDowngrade.IsPrepared(tablet)) return;
        foreach (var app in PerAppBindingStore.List(tablet))
        {
            var copy = PerAppBindingStore.CloneBindings(app.Bindings);
            bool changed = false;
            for (int wi = 0; wi < copy.WheelBindings.Count; wi++)
            {
                var ws = copy.WheelBindings[wi];
                if (!RingModeBinding.IsLegacy(ws)) continue;
                var saved = AppSettings.Get($"RingModes:{tablet}:{wi}:app:{app.BundleId}");
                if (string.IsNullOrEmpty(saved)) saved = AppSettings.Get($"RingModes:{tablet}:{wi}");
                var modes = RingModeBinding.Load(ws, saved, null, null);
                changed |= RingModeBinding.Apply(ws, modes);
            }
            if (!changed) continue;
            PerAppBindingStore.SaveBindings(tablet, app.BundleId, copy);
            AppLog.Info($"Ring modes: {app.BundleId}'s table rewritten for Version 16 BETA 2 (ring directions).");
        }
    }

    private void RefreshBindingEditorsFromSelection()
    {
        // Reuse the existing refresh paths; they call WorkingBindings via the patched Effective* helpers.
        RefreshPenSwitchRows();
        // ExpressKeys + wheels are rebuilt inside RefreshFromProfile's binding section — call the
        // narrow pieces we can reach without resetting mapping/dynamics.
        RebuildAuxButtonsFromWorking();
        RefreshWheels();
    }

    private void RebuildAuxButtonsFromWorking()
    {
        var auxStores = EffectiveAuxStores();
        AuxButtonCount = auxStores.Count.ToString();
        var canEditAux = _applyAction != null && AuxButtonsEnabled;
        var newAuxButtons = new System.Collections.Generic.List<ButtonBinding>();
        for (int i = 0; i < auxStores.Count; i++)
        {
            var store = auxStores[i];
            var binding = AuxKeyBinding.ReadBinding(store);
            newAuxButtons.Add(new ButtonBinding(
                index: i + 1,
                binding: binding ?? AuxBinding.Unbound,
                isOtherBinding: binding == null,
                otherLabel: binding == null ? GetBindingName(store) : "",
                canEdit: canEditAux,
                applyBinding: ApplyAuxBindingAsync,
                editBinding: _editBinding));
        }
        AuxButtons = newAuxButtons;
        NoAuxButtons = newAuxButtons.Count == 0;
        ShowAuxControls = newAuxButtons.Count > 0 && _applyAction != null;
    }

    /// <summary>Select All (clears the app selection).</summary>
    [RelayCommand]
    private void SelectAllApp()
    {
        SelectedAppBundleId = null;
    }

    [RelayCommand]
    private void SelectAppProfile(string? bundleId)
    {
        if (string.IsNullOrEmpty(bundleId)) { SelectedAppBundleId = null; return; }
        SelectedAppBundleId = bundleId;
    }

    public async Task AddApplicationAsync(MacAppCandidate candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate.BundleId)) return;
        var tablet = _profile.Tablet ?? "";
        PerAppBindingStore.Add(tablet, candidate.BundleId, candidate.DisplayName, candidate.AppPath,
            _profile.BindingSettings);
        LoadAppProfiles();
        SelectedAppBundleId = candidate.BundleId;
        PerAppBindingRuntime.Shared?.Kick();
    }

    [RelayCommand]
    private async Task RemoveSelectedAppAsync()
    {
        if (string.IsNullOrEmpty(SelectedAppBundleId)) return;
        var tablet = _profile.Tablet ?? "";
        var name = AppProfiles.FirstOrDefault(a => a.BundleId == SelectedAppBundleId)?.DisplayName
                   ?? SelectedAppBundleId;
        if (!await OpenTabletArtist.Helpers.Dialogs.ShowConfirmAsync(
                Localizer.T("tablet.app.remove_title"),
                Localizer.T("tablet.app.remove_body").Replace("{name}", name)))
            return;
        PerAppBindingStore.Remove(tablet, SelectedAppBundleId!);
        SelectedAppBundleId = null;
        LoadAppProfiles();
        PerAppBindingRuntime.Shared?.Kick();
    }

    /// <summary>Write a binding change either to All (OTD profile) or to the selected app's store.</summary>
    internal async Task ApplyBindingMutationAsync(Action<BindingSettings> mutate)
    {
        // Version 14: editing an extra pen writes to that pen's own settings.
        if (!string.IsNullOrEmpty(SelectedPenKey))
        {
            var tablet = _profile.Tablet ?? "";
            var pen = PerPenStore.Find(tablet, SelectedPenKey);
            if (pen is null) return;
            var copy = PerAppBindingStore.CloneBindings(pen.Bindings);
            mutate(copy);
            PerPenStore.SaveBindings(tablet, SelectedPenKey!, copy);
            RefreshBindingEditorsFromSelection();
            PerAppBindingRuntime.Shared?.Kick();
            return;
        }
        if (!string.IsNullOrEmpty(SelectedAppBundleId))
        {
            var tablet = _profile.Tablet ?? "";
            var app = PerAppBindingStore.Find(tablet, SelectedAppBundleId!);
            if (app is null) return;
            var copy = PerAppBindingStore.CloneBindings(app.Bindings);
            mutate(copy);
            PerAppBindingStore.SaveBindings(tablet, SelectedAppBundleId!, copy);
            RefreshBindingEditorsFromSelection();
            PerAppBindingRuntime.Shared?.Kick();
            return;
        }
        // All — normal OTD apply, and keep the All stash in sync.
        await ApplySettingsChange(p =>
        {
            mutate(p.BindingSettings);
            PerAppBindingRuntime.SyncAllBindings(p.Tablet ?? "", p.BindingSettings);
        });
    }
}

/// <summary>One Application-row tile (not All).</summary>
public sealed partial class AppProfileTile : ObservableObject
{
    public string BundleId { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string AppPath { get; init; } = "";

    public static AppProfileTile From(PerAppProfile p) => new()
    {
        BundleId = p.BundleId,
        DisplayName = string.IsNullOrWhiteSpace(p.DisplayName) ? p.BundleId : p.DisplayName,
        AppPath = p.AppPath ?? "",
    };
}
