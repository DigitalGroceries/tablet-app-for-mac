using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OtdInterop;

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
    private bool _busy;
    private bool _disposed;

    /// <summary>Last frontmost bundle id observed (for diagnostics / UI).</summary>
    public string? LastFrontmostBundleId { get; private set; }

    /// <summary>Bundle id whose bindings are currently on the daemon, or null for All.</summary>
    public string? AppliedBundleId => _appliedBundleId;

    public event Action? AppliedChanged;

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
        if (_appliedBundleId == null && _pendingBundleId == null) return;
        await ApplyTargetAsync(null).ConfigureAwait(true);
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

        if (string.Equals(target, _appliedBundleId, StringComparison.OrdinalIgnoreCase)
            && _pendingBundleId == null)
            return;

        // Defer while the tip is down so we don't SetSettings mid-stroke.
        if (_tipDown())
        {
            _pendingBundleId = target ?? "";
            return;
        }

        _pendingBundleId = null;
        await ApplyTargetAsync(target).ConfigureAwait(true);
    }

    private async Task ApplyTargetAsync(string? bundleId)
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

            profile.BindingSettings = bindings;
            var outcome = await _settings.ApplySettingsAsync(clone).ConfigureAwait(true);
            if (!outcome.IsLive) return;
            _appliedBundleId = bundleId;
            AppliedChanged?.Invoke();
        }
        catch (Exception ex)
        {
            AppLog.Warn("Per-app binding apply failed.", ex);
        }
        finally { _busy = false; }
    }

    public static string AllBindingsKey(string tablet) => $"PerAppAllBindings:{tablet}";

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
