using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// View model for the Startup page (Advanced → Startup): the "launch with Windows" toggle (#360),
/// moved off Home so the dashboard stays focused on tablet + health status.
/// Version 8: also hosts the Language picker (Settings › System).
/// </summary>
public partial class StartupViewModel : ObservableObject
{
    private bool _suppressRegistryWrite;

    private readonly AutoStartCoordinator _auto;
    private bool _suppressLoginWrite;
    private bool _suppressLanguageWrite;

    public StartupViewModel() : this(AutoStartCoordinator.Default) { }

    public StartupViewModel(AutoStartCoordinator auto)
    {
        _auto = auto;
        Localizer.EnsureLoaded();
        RebuildLanguageOptions();
        RefreshFromRegistry();
        RefreshLoginItem();
        Localizer.LanguageChanged += RefreshLocalized;
    }

    // ---- Build P: macOS start at login (SMAppService.mainApp) ----

    public bool LoginItemSupported => _auto.IsSupported;

    /// <summary>Build Q: on a Mac where SMAppService doesn't answer, say so instead of hiding the choice silently.</summary>
    public bool LoginItemUnavailableOnMac => OperatingSystem.IsMacOS() && !_auto.IsSupported;
    public const string LoginUnavailableText =
        "Start at login isn't available on this Mac. Open the app yourself before using your tablet, or add it in System Settings › General › Login Items.";

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
        // Version 7 test 6: check what macOS really did, not just whether the call returned.
        if (message == null && value && !_auto.IsEnabled)
            message = string.Format(AutoStartCoordinator.FallbackMessage, $"status {_auto.Service.Status}");
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
            ? Localizer.T("startup.approval_needed")
            : !StartAtLogin && MacLoginItemService.LastError is { } e
                ? string.Format(AutoStartCoordinator.FallbackMessage, e)
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

    // ---- Version 8: language (Settings › System) ----
    // LanguageOption is a class (not a record): rebuilding the ComboBox ItemsSource with value-equal
    // records left SelectedItem pointing at a stale instance Avalonia could not find in the new list,
    // which cleared the selection and fell through to "Match system" → English on an English Mac.

    public sealed class LanguageOption : System.ComponentModel.INotifyPropertyChanged
    {
        string _nativeName;
        public LanguageOption(string code, string nativeName)
        {
            Code = code;
            _nativeName = nativeName;
        }
        public string Code { get; }
        public string NativeName
        {
            get => _nativeName;
            set
            {
                if (_nativeName == value) return;
                _nativeName = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(NativeName)));
            }
        }
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        public override string ToString() => NativeName;
    }

    public IReadOnlyList<LanguageOption> LanguageOptions { get; private set; } = Array.Empty<LanguageOption>();

    [ObservableProperty] private LanguageOption? _selectedLanguage;

    partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        // Ignore null clears from the ComboBox when ItemsSource is replaced; never treat that as "Match system".
        if (_suppressLanguageWrite || value == null) return;
        if (value.Code.Equals(Localizer.Preference, StringComparison.OrdinalIgnoreCase)) return;
        Localizer.SetPreference(value.Code);
    }

    public string LanguageSectionTitle => Localizer.T("app.language.section");
    public string LanguageNote => Localizer.T("app.language.note");
    public string StartAtLoginSectionTitle => Localizer.T("startup.section");
    public string StartAtLoginToggleLabel => Localizer.T("startup.toggle");
    public string StartAtLoginDescription => Localizer.T("startup.description");
    public string OpenLoginItemsLabel => Localizer.T("startup.open_login_items");
    public string OpenLoginItemsTip => Localizer.T("startup.open_login_items.tip");
    public string LoginUnavailableMessage => Localizer.T("startup.unavailable");

    void RebuildLanguageOptions()
    {
        LanguageOptions = Localizer.Languages.Select(l => new LanguageOption(
            l.Code,
            l.Code == Localizer.SystemCode ? Localizer.T("app.language.match_system") : l.NativeName)).ToArray();
        _suppressLanguageWrite = true;
        SelectedLanguage = LanguageOptions.FirstOrDefault(o =>
            o.Code.Equals(Localizer.Preference, StringComparison.OrdinalIgnoreCase)) ?? LanguageOptions[0];
        _suppressLanguageWrite = false;
    }

    /// <summary>Refresh labels after a language change without replacing LanguageOptions (keeps ComboBox
    /// selection stable). Only the "Match system" display name needs updating.</summary>
    public void RefreshLocalized()
    {
        _suppressLanguageWrite = true;
        try
        {
            var match = LanguageOptions.FirstOrDefault(o => o.Code == Localizer.SystemCode);
            if (match != null)
                match.NativeName = Localizer.T("app.language.match_system");
            var want = LanguageOptions.FirstOrDefault(o =>
                o.Code.Equals(Localizer.Preference, StringComparison.OrdinalIgnoreCase));
            if (want != null && !ReferenceEquals(SelectedLanguage, want))
                SelectedLanguage = want;
        }
        finally
        {
            _suppressLanguageWrite = false;
        }
        OnPropertyChanged(nameof(LanguageSectionTitle));
        OnPropertyChanged(nameof(LanguageNote));
        OnPropertyChanged(nameof(StartAtLoginSectionTitle));
        OnPropertyChanged(nameof(StartAtLoginToggleLabel));
        OnPropertyChanged(nameof(StartAtLoginDescription));
        OnPropertyChanged(nameof(OpenLoginItemsLabel));
        OnPropertyChanged(nameof(OpenLoginItemsTip));
        OnPropertyChanged(nameof(LoginUnavailableMessage));
        RefreshLoginItem();
    }
}
