using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OtdInterop;

namespace OpenTabletArtist.Services;

/// <summary>
/// Intuos4 status LED brightness (PTK-440 and the rest of the Intuos4 family).
/// <para>
/// The tablet itself switches between two brightness levels: one while idle and one while the pen
/// touches (or a button is pressed). The Wacom driver set a dim idle level and a bright touch level;
/// without it the tablet stays at its power-on default. We send the same LED control feature report as
/// Linux <c>wacom_led_control()</c> (drivers/hid/wacom_sys.c): report 0x20, 9 bytes,
/// <c>[0x20, select|0x04, idle, touch, oledLum, 0, 0, 0, 0]</c>, levels 0–127.
/// </para>
/// <para>
/// Because the tablet does the switching, the report goes out once per connection (and when the setting
/// changes), never per stroke — nothing is sent while drawing. Sends are at least
/// <see cref="MinInterval"/> apart. If a send fails, it's logged once and we stop trying until restart.
/// </para>
/// </summary>
public sealed class Intuos4StatusLed : IDisposable
{
    public const string SettingKey = "intuos4.statusLedBrightOnTouch";
    public const int WacomVendorId = 0x056A;
    /// <summary>Intuos4 USB product IDs (PTK-440, 640, 840, 1240, and the wireless model when wired).</summary>
    public static readonly int[] Intuos4ProductIds = { 0xB8, 0xB9, 0xBA, 0xBB, 0xBC };
    public const byte ReportId = 0x20;
    public const byte IdleLevel = 10;      // dim, as the Linux driver's default
    public const byte TouchLevel = 127;    // full brightness while the pen touches
    public const byte SteadyLevel = 40;    // setting off: one steady level, no change on touch
    public static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(500);

    private readonly IDaemonCapabilities _daemon;
    private readonly IConnectionState _connection;
    private readonly Func<DateTime> _now;
    private DateTime _lastSend = DateTime.MinValue;
    private string? _lastSent;     // device + payload last sent, so nothing repeats
    private bool _failed;
    private bool _busy;

    public Intuos4StatusLed(IDaemonCapabilities daemon, IConnectionState connection, Func<DateTime>? now = null)
    {
        _daemon = daemon;
        _connection = connection;
        _now = now ?? (() => DateTime.UtcNow);
        _daemon.TabletsChanged += OnTabletsChanged;
        _connection.PropertyChanged += OnConnectionChanged;
        if (_connection.IsConnected) _ = ApplyAsync();
    }

    /// <summary>Whether the light gets brighter when the pen touches. Default on.</summary>
    public static bool BrightOnTouch
    {
        get => AppSettings.Get(SettingKey) != "false";
        set => AppSettings.Set(SettingKey, value ? "true" : "false");
    }

    public static bool IsIntuos4(int vendorId, int productId) =>
        vendorId == WacomVendorId && Intuos4ProductIds.Contains(productId);

    /// <summary>The 9-byte LED control report (Linux wacom_led_control, Intuos4 branch).</summary>
    public static byte[] BuildReport(bool brightOnTouch) => brightOnTouch
        ? new byte[] { ReportId, 0x04, IdleLevel, TouchLevel, IdleLevel, 0, 0, 0, 0 }
        : new byte[] { ReportId, 0x04, SteadyLevel, SteadyLevel, IdleLevel, 0, 0, 0, 0 };

    private void OnTabletsChanged() { _lastSent = null; _ = ApplyAsync(); }

    private void OnConnectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(IConnectionState.IsConnected)) return;
        _lastSent = null;
        if (_connection.IsConnected) _ = ApplyAsync();
    }

    /// <summary>Re-send after the setting changes.</summary>
    public Task SettingChangedAsync() => ApplyAsync();

    /// <summary>Finds a connected Intuos4 and sends the report if it differs from the last one sent.</summary>
    public async Task ApplyAsync()
    {
        if (_failed || _busy || !_connection.IsConnected) return;
        _busy = true;
        try
        {
            var target = PickInterface(await _daemon.GetDevicesAsync());
            if (target is not { } t) return;
            var report = BuildReport(BrightOnTouch);
            var key = $"{t.Vid}:{t.Pid}:{t.Iface}:{Convert.ToHexString(report)}";
            if (key == _lastSent) return;
            var wait = _lastSend + MinInterval - _now();
            if (wait > TimeSpan.Zero) await Task.Delay(wait);
            _lastSend = _now();
            if (await _daemon.SendFeatureReportAsync(t.Vid, t.Pid, t.Iface, report))
            {
                _lastSent = key;
                AppLog.Info($"Intuos4 status light set ({(BrightOnTouch ? "dim, bright on touch" : "steady")}).");
            }
            else
            {
                _failed = true;
                AppLog.Warn("Intuos4 status light: the tablet didn't accept the brightness setting; not trying again until the app restarts.");
            }
        }
        catch (Exception ex)
        {
            _failed = true;
            AppLog.Warn("Intuos4 status light: sending failed; not trying again until the app restarts.", ex);
        }
        finally { _busy = false; }
    }

    /// <summary>The Intuos4 interface that takes feature reports (the lowest-numbered one that has them).</summary>
    public static (int Vid, int Pid, string Iface)? PickInterface(JArray devices) =>
        devices.OfType<JObject>()
            .Select(d => (Vid: d["VendorID"]?.Value<int>() ?? 0, Pid: d["ProductID"]?.Value<int>() ?? 0,
                          Feature: d["FeatureReportLength"]?.Value<int>() ?? 0,
                          Iface: d["DeviceAttributes"]?["USB_INTERFACE_NUMBER"]?.Value<string>()))
            .Where(d => IsIntuos4(d.Vid, d.Pid) && d.Feature > 0 && d.Iface != null)
            .OrderBy(d => int.TryParse(d.Iface, out var n) ? n : 99)
            .Select(d => ((int, int, string)?)(d.Vid, d.Pid, d.Iface!))
            .FirstOrDefault();

    public void Dispose()
    {
        _daemon.TabletsChanged -= OnTabletsChanged;
        _connection.PropertyChanged -= OnConnectionChanged;
    }
}
