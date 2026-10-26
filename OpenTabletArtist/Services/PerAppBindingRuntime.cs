using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OtdInterop;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Services;

/// <summary>
/// Focus-apply path for Application-row bindings (v1 spike).
/// Polls the frontmost macOS bundle id; when it matches a listed app, live-applies that app's
/// BindingSettings onto the daemon. Otherwise restores All. Never writes OTD settings.json itself
/// (Save still persists All). Avoids mid-stroke SetSettings by skipping while the tip is down when
/// a pen sample source is available; otherwise documents residual risk.
/// </summary>
public sealed class PerAppBindingRuntime : IDisposable
{
    /// <summary>The process-wide runtime started by the shell (may be null off macOS or before start).</summary>
    public static PerAppBindingRuntime? Shared { get; set; }

    private readonly ISettingsCoordinator _settings;
    private readonly Func<string?> _activeTablet;
    private readonly Func<bool> _tipDown;
    private readonly DispatcherTimer _timer;
    private string? _appliedBundleId; // null = All is on the daemon
    private string? _pendingBundleId;
    // Version 14: per-pen overlay. Null = the main pen (no overlay).
    private string? _appliedPenKey;
    private string? _pendingPenKey;
    private bool _penPending;
    private DateTime _penFileStamp;
    private bool _busy;
    private bool _disposed;

    /// <summary>Last frontmost bundle id observed (for diagnostics / UI).</summary>
    public string? LastFrontmostBundleId { get; private set; }

    /// <summary>Bundle id whose bindings are currently on the daemon, or null for All.</summary>
    public string? AppliedBundleId => _appliedBundleId;

    /// <summary>Version 14: the extra pen whose settings are on the daemon, or null for the main pen.</summary>
    public string? AppliedPenKey => _appliedPenKey;

    /// <summary>Version 14: the last pen that came near the tablet (from the pen-dynamics plugin).</summary>
    public PenToolSignal? LastPen { get; private set; }

    public event Action? AppliedChanged;

    /// <summary>Version 14: a pen came near (raised on the UI thread with the tablet name). The Tablet page
    /// uses it to add a tile for a new pen.</summary>
    public event Action<string, PenToolSignal>? PenSeen;

    public PerAppBindingRuntime(ISettingsCoordinator settings, Func<string?> activeTablet, Func<bool> tipDown)
    {
        _settings = settings;
        _activeTablet = activeTablet;
        _tipDown = tipDown;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => _ = TickAsync();
    }

    public void Start()
    {
        if (_disposed || !OperatingSystem.IsMacOS()) return;
        if (!_timer.IsEnabled) _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
    }

    /// <summary>Call before Save / Restore so the on-disk All profile is what the daemon holds.</summary>
    public async Task EnsureAllOnDaemonAsync()
    {
        if (_appliedBundleId == null && _pendingBundleId == null && _appliedPenKey == null && !_penPending) return;
        await ApplyTargetAsync(null, null).ConfigureAwait(true);
    }

    /// <summary>Re-evaluate now (e.g. after the user edits a listed app's bindings).</summary>
    public void Kick() => _ = TickAsync();

    private async Task TickAsync()
    {
        if (_busy || _disposed || !OperatingSystem.IsMacOS()) return;
        var front = MacFrontmostApp.FrontmostBundleId();
        LastFrontmostBundleId = front;
        var tablet = _activeTablet();
        if (string.IsNullOrEmpty(tablet)) return;

        string? target = null;
        if (!string.IsNullOrEmpty(front) && PerAppBindingStore.Find(tablet, front!) != null)
            target = front;

        // Version 14: which pen is near, and does it have its own settings?
        ReadPen(tablet);
        string? penTarget = null;
        if (LastPen is { } pen && PerPenStore.Find(tablet, pen.Key) != null)
            penTarget = pen.Key;

        if (string.Equals(target, _appliedBundleId, StringComparison.OrdinalIgnoreCase)
            && _pendingBundleId == null
            && penTarget == _appliedPenKey && !_penPending)
            return;

        // Defer while the tip is down so we don't SetSettings mid-stroke.
        if (_tipDown())
        {
            _pendingBundleId = target ?? "";
            _pendingPenKey = penTarget;
            _penPending = true;
            return;
        }

        _pendingBundleId = null;
        _pendingPenKey = null;
        _penPending = false;
        await ApplyTargetAsync(target, penTarget).ConfigureAwait(true);
    }

    /// <summary>Reads the pen file when it changed and tells listeners about the pen.</summary>
    private void ReadPen(string tablet)
    {
        try
        {
            var path = PenToolSignal.FilePath;
            if (!System.IO.File.Exists(path)) return;
            var stamp = System.IO.File.GetLastWriteTimeUtc(path);
            if (stamp == _penFileStamp) return;
            _penFileStamp = stamp;
            if (PenToolSignal.Read(path) is not { } pen) return;
            var changed = LastPen?.Key != pen.Key;
            LastPen = pen;
            if (changed) AppLog.Info($"Pen: near the tablet: {pen.Format()} (key {pen.Key}).");
            if (AllBindings(tablet) is { } all)
            {
                var result = PerPenStore.Seen(tablet, pen, all, out var added);
                if (result == PerPenStore.SeenResult.BecameMainPen) AppLog.Info($"Pen: {pen.Key} is the main pen of {tablet}.");
                if (added != null) AppLog.Info($"Pen: added \"{added.DisplayName}\" ({added.Key}) to {tablet}.");
            }
            PenSeen?.Invoke(tablet, pen);
        }
        catch (Exception ex) { AppLog.Warn("Pen: couldn't read the pen file.", ex); }
    }

    private async Task ApplyTargetAsync(string? bundleId, string? penKey)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var tablet = _activeTablet();
            var current = _settings.CurrentSettings;
            if (string.IsNullOrEmpty(tablet) || current is null) return;

            var clone = SettingsCodec.Clone(current);
            var profile = clone.Profiles.FirstOrDefault(p => p.Tablet == tablet);
            if (profile is null) return;

            // The clone currently may already hold a previous overlay. Rebuild BindingSettings from
            // the store / the All values we keep in sync via the editor (see SyncAllBindings).
            BindingSettings bindings;
            if (bundleId is null)
            {
                var allJson = AppSettings.Get(AllBindingsKey(tablet));
                if (!string.IsNullOrEmpty(allJson))
                {
                    try
                    {
                        bindings = Newtonsoft.Json.JsonConvert.DeserializeObject<BindingSettings>(allJson)
                                   ?? profile.BindingSettings;
                    }
                    catch { bindings = profile.BindingSettings; }
                }
                else bindings = profile.BindingSettings;
            }
            else
            {
                var app = PerAppBindingStore.Find(tablet, bundleId);
                if (app is null) return;
                bindings = PerAppBindingStore.CloneBindings(app.Bindings);
            }

            // Version 14: an extra pen's own tip / eraser / side-button settings win over All and the app.
            if (penKey is not null && PerPenStore.Find(tablet, penKey) is { } pen)
            {
                bindings = PerAppBindingStore.CloneBindings(bindings);
                PenBindings.Overlay(bindings, PerAppBindingStore.CloneBindings(pen.Bindings));
            }
            else penKey = null;

            profile.BindingSettings = bindings;
            var outcome = await _settings.ApplySettingsAsync(clone).ConfigureAwait(true);
            if (!outcome.IsLive) return;
            _appliedBundleId = bundleId;
            _appliedPenKey = penKey;
            AppliedChanged?.Invoke();
        }
        catch (Exception ex)
        {
            AppLog.Warn("Per-app binding apply failed.", ex);
        }
        finally { _busy = false; }
    }

    public static string AllBindingsKey(string tablet) => $"PerAppAllBindings:{tablet}";

    /// <summary>The All binding table (kept in sync by the editor), else the live profile's.</summary>
    private BindingSettings? AllBindings(string tablet)
    {
        var allJson = AppSettings.Get(AllBindingsKey(tablet));
        if (!string.IsNullOrEmpty(allJson))
        {
            try
            {
                if (Newtonsoft.Json.JsonConvert.DeserializeObject<BindingSettings>(allJson) is { } b) return b;
            }
            catch { /* fall back to the profile */ }
        }
        return _settings.CurrentSettings?.Profiles.FirstOrDefault(p => p.Tablet == tablet)?.BindingSettings;
    }

    /// <summary>Persist the All binding table whenever the user edits All (so focus-restore is accurate
    /// even if the workspace draft briefly held an overlay).</summary>
    public static void SyncAllBindings(string tablet, BindingSettings bindings) =>
        AppSettings.Set(AllBindingsKey(tablet),
            Newtonsoft.Json.JsonConvert.SerializeObject(PerAppBindingStore.CloneBindings(bindings)));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
