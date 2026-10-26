using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OpenTabletArtist.Domain;
using OtdInterop;

namespace OpenTabletArtist.Services;

/// <summary>
/// Lightweight tip/pen-busy sample for <see cref="PerAppBindingRuntime"/> mid-stroke deferral.
/// Owns a long-lived <c>SetTabletDebug</c> lease and updates from the daemon DeviceReport stream
/// (same paths as <see cref="DaemonPenInputSource"/> / RingGestureGate.PenBusy).
/// </summary>
public sealed class PenBusySampler : IDisposable
{
    private readonly IDaemonDebugSession _daemon;
    private readonly object _gate = new();
    private volatile bool _tipDown;
    private volatile bool _penButtonDown;
    private volatile bool _subscribed;
    private volatile bool _wantRunning;
    private bool _starting;
    private bool _disposed;
    private long _lastReportTicks; // Stopwatch ticks; 0 = never

    public PenBusySampler(IDaemonDebugSession daemon) => _daemon = daemon;

    /// <summary>True while tip pressure &gt; 0 (contact). Used to defer SetSettings mid-stroke.</summary>
    public bool TipDown => _tipDown;

    /// <summary>Tip down or any pen barrel button held — matches RingGestureGate.PenBusy semantics.</summary>
    public bool PenBusy => _tipDown || _penButtonDown;

    /// <summary>True when a DeviceReport has been seen recently (stream is alive).</summary>
    public bool HasFreshSample
    {
        get
        {
            var t = Interlocked.Read(ref _lastReportTicks);
            if (t == 0) return false;
            var ageMs = (System.Diagnostics.Stopwatch.GetTimestamp() - t) * 1000.0
                        / System.Diagnostics.Stopwatch.Frequency;
            return ageMs < 500;
        }
    }

    public void Start()
    {
        if (_disposed) return;
        _wantRunning = true;
        _ = EnsureStartedAsync();
    }

    public void Stop()
    {
        _wantRunning = false;
        Unsubscribe();
        try { _ = _daemon.SetTabletDebugAsync(false); } catch { /* best-effort */ }
        _tipDown = false;
        _penButtonDown = false;
    }

    /// <summary>Re-enable the debug stream if another consumer turned it off (e.g. leaving Pen tab).</summary>
    public void Kick()
    {
        if (_disposed || !_wantRunning) return;
        if (!HasFreshSample) _ = EnsureStartedAsync();
    }

    private async Task EnsureStartedAsync()
    {
        lock (_gate)
        {
            if (_disposed || !_wantRunning || _starting) return;
            _starting = true;
        }
        try
        {
            try { await _daemon.SetTabletDebugAsync(true).ConfigureAwait(false); }
            catch { return; }
            if (!_wantRunning || _disposed)
            {
                try { await _daemon.SetTabletDebugAsync(false).ConfigureAwait(false); } catch { }
                return;
            }
            if (!_subscribed)
            {
                _daemon.DeviceReport -= OnDeviceReport;
                _daemon.DeviceReport += OnDeviceReport;
                _subscribed = true;
            }
        }
        finally
        {
            lock (_gate) { _starting = false; }
        }
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        _daemon.DeviceReport -= OnDeviceReport;
        _subscribed = false;
    }

    private void OnDeviceReport(JObject data)
    {
        Interlocked.Exchange(ref _lastReportTicks, System.Diagnostics.Stopwatch.GetTimestamp());
        UpdateFromReport(data, out var tip, out var buttons);
        // Tip: only pen-position reports carry pressure; leave tip unchanged on aux/wheel-only packets.
        if (tip is { } t) _tipDown = t;
        if (buttons is { } b) _penButtonDown = b;
    }

    /// <summary>Pure update helper — unit-tested. tip/buttons are null when the report lacks that field.</summary>
    internal static void UpdateFromReport(JObject data, out bool? tipDown, out bool? penButtonDown)
    {
        tipDown = null;
        penButtonDown = null;
        if (DeviceReportSample.TryParse(data, out var sample))
            tipDown = sample.IsDown;
        if (DeviceReportSample.TryParsePenButtons(data, out var buttons))
            penButtonDown = buttons.Any(b => b);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
