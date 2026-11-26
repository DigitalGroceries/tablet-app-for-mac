using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// Landing page for the Tablets group (shown when its header is clicked / no tablet is selected).
/// Lists every known tablet — detected and remembered — with its detection status and last-seen time,
/// each navigable to its settings page. The row list and <see cref="HasTablets"/> are kept in sync by
/// the shell as tablets come and go (#307). (The supported-tablets catalog link now lives in Home's
/// About → RESOURCES card.)
/// </summary>
public partial class TabletsOverviewViewModel : ObservableObject
{
    [ObservableProperty] private bool _hasTablets;

    /// <summary>One row per known tablet, rebuilt by the shell on each data load.</summary>
    [ObservableProperty] private List<TabletOverviewItemViewModel> _tablets = [];
}

/// <summary>One tablet on the overview: detection status + last-seen, navigable to its settings page.
/// Navigation is a callback supplied by the shell so the row stays UI-only (#307).</summary>
public partial class TabletOverviewItemViewModel : ObservableObject
{
    private readonly Action _navigate;
    private readonly Func<Task> _forget;

    public TabletOverviewItemViewModel(string name, bool isDetected, string statusText,
        string? statusDetail, Action navigate, Func<Task> forget, int keysNotSetUp = 0, Action? openExpressKeys = null)
    {
        Name = name;
        IsDetected = isDetected;
        StatusText = statusText;
        StatusDetail = statusDetail;
        _navigate = navigate;
        _forget = forget;
        KeysNotSetUp = keysNotSetUp;
        _openExpressKeys = openExpressKeys ?? navigate;
    }

    private readonly Action _openExpressKeys;

    /// <summary>V30 #200 B2 (Digital Groceries 23 Nov 2026; team vote: do this first): how many ExpressKeys do nothing yet (not counting
    /// keys the user turned Off). Home's card says so, with a link to the ExpressKeys page.</summary>
    public int KeysNotSetUp { get; }
    public bool HasKeysNotSetUp => KeysNotSetUp > 0;
    /// <summary>V30 build 3 (Digital Groceries 24 Nov, his words): "... not set up: Choose what they do or restore to factory settings".
    /// Build 4 (Digital Groceries 24 Nov, on build 3): "change the text to include "Click this button to choose what they do..."" - "I dont
    /// want to take any (IQ) chances".</summary>
    public string KeysNotSetUpText => KeysNotSetUp == 1
        ? Services.Localizer.Te("home.keys_not_set_up.one", "1 ExpressKey not set up: Click this button to choose what it does or restore to factory settings")
        : Services.Localizer.Te("home.keys_not_set_up.many", "{n} ExpressKeys not set up: Click this button to choose what they do or restore to factory settings").Replace("{n}", KeysNotSetUp.ToString());

    /// <summary>V30 build 3 (task list L6.2): the card's own red Forget button (was in the ••• menu).</summary>
    public string ForgetText => Services.Localizer.Te("home.forget", "Forget this tablet");

    [RelayCommand]
    private void OpenExpressKeys() => _openExpressKeys();

    public string Name { get; }
    public bool IsDetected { get; }
    public string StatusText { get; }
    /// <summary>The card's third line: CONNECTED, or when the tablet was last seen.</summary>
    public string? StatusDetail { get; }
    public bool HasStatusDetail => !string.IsNullOrEmpty(StatusDetail);

    /// <summary>#27 (Wacom comparison point 6, Mike Milo #30): a tablet that isn't found says where to look next,
    /// instead of only "Not detected".</summary>
    public string NotFoundHint => Services.Localizer.Te("home.not_found_hint", "Not found. Double-click to see what to try.");

    [RelayCommand]
    private void Open() => _navigate();

    /// <summary>Forget this tablet — remove its saved profile from the settings.</summary>
    [RelayCommand]
    private Task Forget() => _forget();
}
