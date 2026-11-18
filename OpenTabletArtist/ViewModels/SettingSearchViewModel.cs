using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>Version 29: the "Setting search" box under Home's tab row. Typing lists the matching places with
/// their paths; choosing one asks the window to open it (<see cref="PlaceChosen"/>).</summary>
public partial class SettingSearchViewModel : ObservableObject
{
    public SettingSearchViewModel() => Localizer.LanguageChanged += OnLanguageChanged;

    [ObservableProperty] private string _query = "";
    [ObservableProperty] private IReadOnlyList<SettingSearchEntry> _results = Array.Empty<SettingSearchEntry>();
    [ObservableProperty] private bool _isOpen;

    /// <summary>Raised with the place to open.</summary>
    public event Action<SettingSearchEntry>? PlaceChosen;

    public string Label => Localizer.Te("search.label", "Setting search");
    public string Hint => Localizer.Te("search.hint", "Find any setting or function");
    public string NothingFoundText => Localizer.Te("search.none", "Nothing found. Try another word.");

    public bool NothingFound => IsOpen && Results.Count == 0;

    partial void OnQueryChanged(string value)
    {
        Results = SettingSearch.Find(value);
        IsOpen = !string.IsNullOrWhiteSpace(value);
    }

    partial void OnResultsChanged(IReadOnlyList<SettingSearchEntry> value) => OnPropertyChanged(nameof(NothingFound));
    partial void OnIsOpenChanged(bool value) => OnPropertyChanged(nameof(NothingFound));

    [RelayCommand]
    private void Open(SettingSearchEntry? entry)
    {
        if (entry is null) return;
        AppLog.Info($"Setting search: '{Query}' -> {entry.Route} ({entry.Text})");
        IsOpen = false;
        Query = "";
        PlaceChosen?.Invoke(entry);
    }

    /// <summary>Enter in the box: open the best match, if there is one.</summary>
    public void OpenFirst()
    {
        if (Results.Count > 0) Open(Results[0]);
    }

    /// <summary>Escape: empty the box and close the list.</summary>
    public void Clear()
    {
        Query = "";
        IsOpen = false;
    }

    private void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(Hint));
        OnPropertyChanged(nameof(NothingFoundText));
        Results = SettingSearch.Find(Query);
    }
}
