using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace OpenTabletArtist.Services;

/// <summary>Shared pending-update state for Home/About and the pen-menu tray.
/// One check path (<see cref="Updater.CheckAsync"/>) so both surfaces stay in sync.</summary>
public sealed class AppUpdateState : INotifyPropertyChanged
{
    public static AppUpdateState Shared { get; } = new();

    Updater.Release? _pending;
    string _status = "";
    string _statusKey = "";
    string? _statusVersion;
    string? _statusRaw;
    bool _busy;

    public AppUpdateState()
    {
        Localizer.LanguageChanged += RefreshStatusLanguage;
    }

    public Updater.Release? Pending => _pending;
    public bool UpdateReady => _pending != null;
    public string Status => _status;
    public bool IsBusy => _busy;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>GitHub latest check; updates <see cref="Pending"/> and <see cref="Status"/>.</summary>
    public async Task CheckAsync()
    {
        if (_busy) return;
        _busy = true;
        Notify(nameof(IsBusy));
        SetLocalizedStatus("update.checking");
        try
        {
            _pending = await Updater.CheckAsync();
            Notify(nameof(Pending));
            Notify(nameof(UpdateReady));
            if (_pending != null)
                SetLocalizedStatus("update.available_ask", _pending.Version.ToString());
            else
                SetLocalizedStatus("update.latest");
        }
        finally
        {
            _busy = false;
            Notify(nameof(IsBusy));
        }
    }

    /// <summary>Download and verify the pending release without installing. Returns an error, or null.</summary>
    public async Task<string?> DownloadPendingAsync()
    {
        if (_pending == null || _busy) return "No update is available.";
        _busy = true;
        Notify(nameof(IsBusy));
        SetLocalizedStatus("update.downloading");
        try
        {
            var err = await Updater.DownloadAsync(_pending);
            if (err != null) { SetRawStatus(err); return err; }
            SetLocalizedStatus("update.ready_status", _pending.Version.ToString());
            return null;
        }
        finally
        {
            _busy = false;
            Notify(nameof(IsBusy));
        }
    }

    /// <summary>Apply a previously downloaded update (quits and swaps).</summary>
    public async Task ApplyPendingAsync()
    {
        if (_busy) return;
        _busy = true;
        Notify(nameof(IsBusy));
        SetLocalizedStatus("update.installing");
        try
        {
            var err = Updater.ApplyPrepared();
            if (err != null) SetRawStatus(err);
            await Task.CompletedTask;
        }
        finally
        {
            _busy = false;
            Notify(nameof(IsBusy));
        }
    }

    /// <summary>Install the pending release (download then apply — same path as Home's Update now).</summary>
    public async Task InstallPendingAsync()
    {
        if (_pending == null || _busy) return;
        _busy = true;
        Notify(nameof(IsBusy));
        SetLocalizedStatus("update.downloading");
        try
        {
            var err = await Updater.InstallAsync(_pending);
            if (err != null) SetRawStatus(err);
            else SetLocalizedStatus("update.installing");
        }
        finally
        {
            _busy = false;
            Notify(nameof(IsBusy));
        }
    }

    void SetLocalizedStatus(string key, string? version = null)
    {
        _statusKey = key;
        _statusVersion = version;
        _statusRaw = null;
        var text = version == null ? Localizer.T(key) : Localizer.Tf(key, ("version", version));
        SetStatus(text);
    }

    void SetRawStatus(string value)
    {
        _statusKey = "";
        _statusVersion = null;
        _statusRaw = value;
        SetStatus(value);
    }

    void RefreshStatusLanguage()
    {
        if (!string.IsNullOrEmpty(_statusRaw)) return;
        if (string.IsNullOrEmpty(_statusKey)) return;
        var text = _statusVersion == null
            ? Localizer.T(_statusKey)
            : Localizer.Tf(_statusKey, ("version", _statusVersion));
        SetStatus(text);
    }

    void SetStatus(string value)
    {
        if (_status == value) return;
        _status = value;
        Notify(nameof(Status));
    }

    void Notify([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
