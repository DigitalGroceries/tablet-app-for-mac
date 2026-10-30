using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.ViewModels;

/// <summary>Update notices for the connected tablets, shown above the page until dismissed
/// (<see cref="TabletUpdateNotices"/>). Dismissal is stored in the app settings, so it survives restarts.</summary>
public sealed partial class TabletNoticesViewModel : ObservableObject
{
    private readonly Func<string, string?> _getSetting;
    private readonly Action<string, string> _setSetting;
    private readonly IReadOnlyList<TabletUpdateNotice>? _catalog;
    private List<string> _connected = [];

    public TabletNoticesViewModel(Func<string, string?> getSetting, Action<string, string> setSetting,
        IReadOnlyList<TabletUpdateNotice>? catalog = null)
    {
        _getSetting = getSetting;
        _setSetting = setSetting;
        _catalog = catalog;
    }

    public ObservableCollection<TabletNoticeItem> Items { get; } = [];

    [ObservableProperty] private bool _hasNotices;

    public void Refresh(IEnumerable<string> connectedTablets)
    {
        _connected = connectedTablets.ToList();
        var pending = TabletUpdateNotices.Pending(_connected, IsDismissed, _catalog);
        if (pending.SequenceEqual(Items)) return;
        Items.Clear();
        foreach (var item in pending) Items.Add(item);
        HasNotices = Items.Count > 0;
    }

    [RelayCommand]
    private void Dismiss(TabletNoticeItem? item)
    {
        if (item == null) return;
        _setSetting(TabletUpdateNotices.DismissedKey(item.Id), "true");
        Refresh(_connected);
    }

    private bool IsDismissed(string id) => _getSetting(TabletUpdateNotices.DismissedKey(id)) == "true";
}
