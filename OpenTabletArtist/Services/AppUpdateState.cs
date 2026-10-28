using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace OpenTabletArtist.Services;

/// <summary>Shared pending-update state for Home/About and the pen-menu tray.
/// One check path (<see cref="Updater.CheckChoicesAsync"/>) so both surfaces stay in sync.
/// Version 14: the check offers a choice — a newer stable version, a newer BETA (not tested yet), and for
/// someone running a BETA, going back to the stable version.</summary>
public sealed class AppUpdateState : INotifyPropertyChanged
{
    public static AppUpdateState Shared { get; } = new();

    Updater.UpdateChoices _choices = Updater.UpdateChoices.None;
    string _status = "";
    string _statusKey = "";
    (string Name, object? Value)[] _statusArgs = Array.Empty<(string, object?)>();
    string? _statusRaw;
    bool _busy;

    public AppUpdateState()
    {
        Localizer.LanguageChanged += RefreshStatusLanguage;
    }

    public Updater.UpdateChoices Choices => _choices;
    /// <summary>A newer stable version.</summary>
    public Updater.Release? StableOffer => _choices.StableUpgrade;
    /// <summary>A newer BETA (not tested yet).</summary>
    public Updater.Release? BetaOffer => _choices.Beta;
    /// <summary>For a BETA build: the stable version to go back to.</summary>
    public Updater.Release? GoBackOffer => _choices.GoBack;
    /// <summary>Version 15: the earlier stable versions anyone can go back to (newest first, at most two).</summary>
    public System.Collections.Generic.IReadOnlyList<Updater.Release> EarlierOffers => _choices.EarlierVersions;

    /// <summary>What the weekly check offers by itself: a newer stable version; for someone already running a
    /// BETA, also a newer BETA. Never a BETA for someone on a stable version (they choose it on Home).</summary>
    public Updater.Release? Pending => _choices.StableUpgrade ?? (Updater.CurrentBeta > 0 ? _choices.Beta : null);

    /// <summary>True when there is anything to choose (the pen menu shows "Update available").</summary>
    public bool UpdateReady => _choices.Any;
    public bool HasStableOffer => StableOffer != null;
    public bool HasBetaOffer => BetaOffer != null;
    public bool HasGoBackOffer => GoBackOffer != null;
    public string Status => _status;
    public bool IsBusy => _busy;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Checks GitHub; updates the offers and <see cref="Status"/>.</summary>
    public async Task CheckAsync()
    {
        if (_busy) return;
        _busy = true;
        Notify(nameof(IsBusy));
        SetLocalizedStatus("update.checking");
        try
        {
            _choices = await Updater.CheckChoicesAsync();
            NotifyChoices();
            SetChoiceStatus();
        }
        finally
        {
            _busy = false;
            Notify(nameof(IsBusy));
        }
    }

    /// <summary>Version 15: say that no earlier version could be found (offline, or GitHub didn't answer).</summary>
    public void ReportNoEarlier() => SetLocalizedStatus("update.earlier_none");

    /// <summary>Status line for the current choices. Pure apart from the localiser.</summary>
    void SetChoiceStatus()
    {
        var s = StableOffer; var b = BetaOffer; var back = GoBackOffer;
        if (s != null && b != null)
            SetLocalizedStatus("update.available_both", ("version", s.Label), ("beta", b.Label));
        else if (s != null)
            SetLocalizedStatus("update.available_ask", ("version", s.Label));
        else if (back != null && b != null)
            SetLocalizedStatus("update.on_beta_newer", ("version", back.Label), ("beta", b.Label));
        else if (back != null)
            SetLocalizedStatus("update.on_beta", ("version", back.Label));
        else if (b != null)
            SetLocalizedStatus("update.beta_available", ("beta", b.Label));
        else
            SetLocalizedStatus("update.latest");
    }

    /// <summary>Download and verify the pending release without installing. Returns an error, or null.</summary>
    public async Task<string?> DownloadPendingAsync()
    {
        var r = Pending;
        if (r == null || _busy) return "No update is available.";
        _busy = true;
        Notify(nameof(IsBusy));
        SetLocalizedStatus("update.downloading");
        try
        {
            var err = await Updater.DownloadAsync(r);
            if (err != null) { SetRawStatus(err); return err; }
            SetLocalizedStatus("update.ready_status", ("version", r.Label));
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
    public Task InstallPendingAsync() => Pending is { } r ? InstallAsync(r) : Task.CompletedTask;

    /// <summary>Version 14: install the chosen release (stable, BETA, or back to stable): download, check, apply.</summary>
    /// <summary>Version 16 BETA 3: run after a version older than 16 is downloaded and before it is installed, to
    /// put ring modes in the layout it reads (see <see cref="RingDowngrade"/>). Set by the main window.</summary>
    public System.Func<Task>? PrepareForOlderVersion { get; set; }

    public async Task InstallAsync(Updater.Release r)
    {
        if (_busy) return;
        _busy = true;
        Notify(nameof(IsBusy));
        SetLocalizedStatus("update.downloading");
        try
        {
            AppLog.Info($"Update: installing Version {r.Label}.");
            var err = await Updater.DownloadAsync(r);
            if (err == null && RingDowngrade.IsNeededFor(r.Version) && PrepareForOlderVersion is { } prepare)
            {
                try { await prepare(); }
                catch (System.Exception ex) { AppLog.Warn("Ring modes: converting them for an earlier version failed.", ex); }
            }
            err ??= Updater.ApplyPrepared();
            if (err != null) SetRawStatus(err);
            else SetLocalizedStatus("update.installing");
        }
        finally
        {
            _busy = false;
            Notify(nameof(IsBusy));
        }
    }

    void NotifyChoices()
    {
        Notify(nameof(Choices));
        Notify(nameof(Pending));
        Notify(nameof(UpdateReady));
        Notify(nameof(StableOffer));
        Notify(nameof(BetaOffer));
        Notify(nameof(GoBackOffer));
        Notify(nameof(HasStableOffer));
        Notify(nameof(HasBetaOffer));
        Notify(nameof(HasGoBackOffer));
        Notify(nameof(EarlierOffers));
    }

    void SetLocalizedStatus(string key, params (string Name, object? Value)[] args)
    {
        _statusKey = key;
        _statusArgs = args;
        _statusRaw = null;
        SetStatus(Localizer.Tf(key, args));
    }

    void SetRawStatus(string value)
    {
        _statusKey = "";
        _statusArgs = Array.Empty<(string, object?)>();
        _statusRaw = value;
        SetStatus(value);
    }

    void RefreshStatusLanguage()
    {
        if (!string.IsNullOrEmpty(_statusRaw)) return;
        if (string.IsNullOrEmpty(_statusKey)) return;
        SetStatus(Localizer.Tf(_statusKey, _statusArgs));
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
