using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>Version 14: Tool-row pen tiles (Wacom-style per-pen settings) and the Device / Tool + and −
/// buttons. Partial of <see cref="TabletDetailViewModel"/>.</summary>
public partial class TabletDetailViewModel
{
    /// <summary>Null = the main pen (the ordinary Pen tile). Otherwise the selected extra pen's key.</summary>
    [ObservableProperty] private string? _selectedPenKey;

    /// <summary>True while the Pen tile or an extra pen tile is selected: shows the Pen, Eraser and Mapping
    /// tabs (the view sets it; the tabs bind to it).</summary>
    [ObservableProperty] private bool _penToolActive = true;

    /// <summary>Extra pens (not including the main pen).</summary>
    public ObservableCollection<PenProfileTile> PenProfiles { get; } = new();

    /// <summary>The Tool row's − removes an extra pen's settings; the main pen can't be removed.</summary>
    public bool CanRemovePen => !string.IsNullOrEmpty(SelectedPenKey);

    /// <summary>Shown on the Pen tab while an extra pen is selected: what is per pen and what is shared.</summary>
    public bool ShowPerPenNote => !string.IsNullOrEmpty(SelectedPenKey);

    public string PerPenNote => Localizer.Te("tablet.pen.per_pen_note",
        "This pen has its own tip, eraser and side-button settings. Tip Feel, pressure, tilt and mapping are shared by all pens.");

    partial void OnSelectedPenKeyChanged(string? value)
    {
        foreach (var t in PenProfiles) t.IsSelected = t.Key == value;
        OnPropertyChanged(nameof(CanRemovePen));
        OnPropertyChanged(nameof(ShowPerPenNote));
        RefreshBindingEditorsFromSelection();
    }

    internal void LoadPenProfiles()
    {
        var tablet = _profile.Tablet ?? "";
        var list = PerPenStore.List(tablet);
        if (SelectedPenKey != null && list.All(p => p.Key != SelectedPenKey)) SelectedPenKey = null;
        PenProfiles.Clear();
        foreach (var p in list)
            PenProfiles.Add(new PenProfileTile { Key = p.Key, DisplayName = p.DisplayName, IsSelected = p.Key == SelectedPenKey });
    }

    /// <summary>A pen came near (from <see cref="PerAppBindingRuntime.PenSeen"/>): show a new pen's tile.</summary>
    internal void OnPenSeen(string tablet, PenToolSignal pen)
    {
        if (tablet != _profile.Tablet) return;
        if (PenProfiles.Any(t => t.Key == pen.Key)) return;
        if (PerPenStore.Find(tablet, pen.Key) != null) LoadPenProfiles();
    }

    [RelayCommand]
    private void SelectPenProfile(string? key) => SelectedPenKey = string.IsNullOrEmpty(key) ? null : key;

    [RelayCommand]
    private async Task RemoveSelectedPenAsync()
    {
        if (string.IsNullOrEmpty(SelectedPenKey)) return;
        var tablet = _profile.Tablet ?? "";
        var name = PenProfiles.FirstOrDefault(p => p.Key == SelectedPenKey)?.DisplayName ?? SelectedPenKey;
        if (!await OpenTabletArtist.Helpers.Dialogs.ShowConfirmAsync(
                Localizer.Te("tablet.pen.remove_title", "Remove pen"),
                Localizer.Te("tablet.pen.remove_body",
                    "Remove the settings for \"{name}\"? It goes back to the main pen's settings. If you use it again, it comes back here with a copy of the main pen's settings.")
                    .Replace("{name}", name)))
            return;
        PerPenStore.Remove(tablet, SelectedPenKey!);
        SelectedPenKey = null;
        LoadPenProfiles();
        PerAppBindingRuntime.Shared?.Kick();
    }

    /// <summary>The Device row's −: the same as Forget on the Home tablet card (it asks first).</summary>
    public bool CanForgetTablet => _forgetAction != null;

    /// <summary>The Device row's +: tablets appear by themselves when connected; an unknown model needs a
    /// tablet config (Advanced › Tablet configs).</summary>
    public async Task AddTabletAsync()
    {
        var ok = await OpenTabletArtist.Helpers.Dialogs.ShowConfirmAsync(
            Localizer.Te("tablet.device.add_title", "Add a tablet"),
            Localizer.Te("tablet.device.add_body",
                "Connect the tablet with its cable. It appears here by itself. If it doesn't, this app may not know the model yet: open Tablet configs to check for one."),
            Localizer.Te("tablet.device.add_configs", "Tablet configs"),
            Localizer.Te("app.close", "Close"));
        if (ok) _openConfigsPage?.Invoke();
    }

    /// <summary>The Tool row's +: pens are added by themselves the first time they come near the tablet.</summary>
    public Task AddPenAsync() => OpenTabletArtist.Helpers.Dialogs.ShowMessageAsync(
        Localizer.Te("tablet.pen.add_title", "Add a pen"),
        Localizer.Te("tablet.pen.add_body",
            "Bring the new pen close to the tablet. It's added to the Tool row by itself, with its own tip, eraser and side-button settings (a copy of the main pen's to start with)."));
}

/// <summary>One extra pen tile in the Tool row.</summary>
public sealed partial class PenProfileTile : ObservableObject
{
    public string Key { get; init; } = "";
    public string DisplayName { get; init; } = "";
    [ObservableProperty] private bool _isSelected;
}
