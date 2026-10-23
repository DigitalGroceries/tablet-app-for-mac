using System;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// View model for the Startup page (Advanced → Startup): the "launch with Windows" toggle (#360),
/// moved off Home so the dashboard stays focused on tablet + health status.
/// </summary>
public partial class StartupViewModel : ObservableObject
{
    private bool _suppressRegistryWrite;

    private readonly AutoStartCoordinator _auto;
    private bool _suppressLoginWrite;

    public StartupViewModel() : this(AutoStartCoordinator.Default) { }

    public StartupViewModel(AutoStartCoordinator auto)
    {
        _auto = auto;
        RefreshFromRegistry();
        RefreshLoginItem();
    }

    // ---- Build P: macOS start at login (SMAppService.mainApp) ----

    public bool LoginItemSupported => _auto.IsSupported;

    /// <summary>Build Q: on a Mac where SMAppService doesn't answer, say so instead of hiding the choice silently.</summary>
    public bool LoginItemUnavailableOnMac => OperatingSystem.IsMacOS() && !_auto.IsSupported;
    public const string LoginUnavailableText =
        "Start at login isn't available on this Mac. Open the app yourself before using your tablet, or add it in System Settings › General › Login Items.";
    public string LoginUnavailableMessage => LoginUnavailableText;

    /// <summary>"Start automatically when I log in" — mirrors the real login item, not just the saved choice.</summary>
    [ObservableProperty] private bool _startAtLogin;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoginMessage))]
    private string _loginMessage = "";
    public bool HasLoginMessage => !string.IsNullOrEmpty(LoginMessage);

    public const string LoginExplanation =
        "Your tablet only works while Tablet App for Mac is running. If this is off, open Tablet App for Mac each time before using your tablet.";

    partial void OnStartAtLoginChanged(bool value)
    {
        if (_suppressLoginWrite) return;
        var message = _auto.Choose(value);
        RefreshLoginItem();
        if (message != null) LoginMessage = message;
    }

    /// <summary>Re-check the real login-item state (call when the page opens; the user can remove the item in
    /// System Settings at any time).</summary>
    public void RefreshLoginItem()
    {
        if (!_auto.IsSupported) return;
        _suppressLoginWrite = true;
        StartAtLogin = _auto.Sync();
        _suppressLoginWrite = false;
        LoginMessage = _auto.RequiresApproval
            ? "macOS is waiting for your approval: turn Tablet App for Mac on in System Settings › General › Login Items."
            : "";
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void OpenLoginItems() => _auto.Service.OpenLoginItemsSettings();

    /// <summary>Whether the run-at-startup toggle is available (Windows only) — hides the card elsewhere.</summary>
    public bool StartupSupported => StartupService.IsSupported;

    /// <summary>Launch OpenTabletArtist when Windows starts (per-user Run key). (#360)</summary>
    [ObservableProperty] private bool _startWithWindows;

    /// <summary>Shown when the registry write fails or the stored path is stale (#382).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRegistryMessage))]
    private string _registryMessage = "";

    public bool HasRegistryMessage => !string.IsNullOrEmpty(RegistryMessage);

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_suppressRegistryWrite) return;
        if (StartupService.SetEnabled(value))
        {
            RegistryMessage = "";
            return;
        }

        _suppressRegistryWrite = true;
        StartWithWindows = StartupService.IsEnabled;
        _suppressRegistryWrite = false;
        RegistryMessage = "Couldn't update startup settings. The registry may be locked by policy.";
    }

    private void RefreshFromRegistry()
    {
        _suppressRegistryWrite = true;
        StartWithWindows = StartupService.IsEnabled;
        _suppressRegistryWrite = false;
        RegistryMessage = StartupService.GetState() switch
        {
            StartupRegistryState.StalePath =>
                "Startup is enabled but points at an old install path. Toggle off and on to repair.",
            _ => "",
        };
    }
}
