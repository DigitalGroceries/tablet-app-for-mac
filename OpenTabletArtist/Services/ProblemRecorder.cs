using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OtdInterop;

namespace OpenTabletArtist.Services;

/// <summary>
/// "Record the problem" in the report window (TASKS #81 a): between Start and Stop it keeps a short,
/// timestamped list of every report the tablet sent (pen, eraser, buttons, touch, ExpressKeys, ring),
/// and marks the start and the end in app.log and in the driver log, so the logs in the report show
/// what the app and the driver did in the same seconds. Bounded: at most <see cref="MaxEvents"/>
/// reports and <see cref="MaxLength"/>; anything after that is counted, not kept.
/// </summary>
public sealed class ProblemRecorder
{
    public const int MaxEvents = 5000;
    public static readonly TimeSpan MaxLength = TimeSpan.FromMinutes(2);
    internal const int MaxDataChars = 400;
    internal const string LogGroup = "Tablet App for Mac";

    private readonly IDaemonDebugSession? _daemon;
    private readonly Func<DateTime> _now;
    private readonly object _lock = new();
    private readonly List<string> _lines = new();
    private readonly Dictionary<string, int> _kinds = new(StringComparer.Ordinal);
    private readonly List<string> _problems = new();
    private DateTime _started;
    private int _dropped;
    private bool _streamOn;
    private bool _timeUp;

    /// <param name="daemon">The driver connection, or null when the app has none (the recording says so).</param>
    public ProblemRecorder(IDaemonDebugSession? daemon, Func<DateTime>? now = null)
    {
        _daemon = daemon;
        _now = now ?? (() => DateTime.Now);
    }

    public bool IsRecording { get; private set; }

    /// <summary>True when the last recording stopped by itself at <see cref="MaxLength"/>.</summary>
    public bool StoppedAtLimit { get { lock (_lock) return _timeUp; } }
    private CancellationTokenSource? _limit;

    /// <summary>The finished recording (recording.txt), or null before the first Stop.</summary>
    public string? Trace { get; private set; }

    /// <summary>Reports kept so far.</summary>
    public int EventCount { get { lock (_lock) return _lines.Count; } }

    /// <summary>Raised on start, on stop and every 25 kept reports. May fire off the UI thread.</summary>
    public event Action? Changed;

    public async Task StartAsync()
    {
        if (IsRecording) return;
        lock (_lock)
        {
            _lines.Clear(); _kinds.Clear(); _problems.Clear();
            _dropped = 0; _timeUp = false;
            _started = _now();
        }
        Trace = null;
        IsRecording = true;
        _limit = new CancellationTokenSource();
        _ = StopAtLimitAsync(_limit.Token);
        AppLog.Info("=== Report: recording started (Record the problem) ===");
        if (_daemon == null)
        {
            Problem("The driver is not connected, so no tablet reports could be recorded.");
        }
        else
        {
            _daemon.DeviceReport += OnReport;
            try
            {
                await _daemon.SetTabletDebugAsync(true);
                _streamOn = true;
            }
            catch (Exception ex)
            {
                AppLog.Warn("Report: couldn't turn on the tablet report stream for the recording.", ex);
                Problem("Couldn't turn on the driver's tablet report stream: " + ex.Message);
            }
            await MarkDriverLogAsync("started");
        }
        Changed?.Invoke();
    }

    /// <summary>Ends the recording and returns recording.txt. Safe to call when not recording.</summary>
    public async Task<string> StopAsync()
    {
        lock (_lock)
        {
            if (!IsRecording) return Trace ?? "";
            IsRecording = false;
        }
        _limit?.Cancel();
        _limit = null;
        if (_daemon != null)
        {
            _daemon.DeviceReport -= OnReport;
            if (_streamOn)
            {
                _streamOn = false;
                try { await _daemon.SetTabletDebugAsync(false); }
                catch (Exception ex) { AppLog.Warn("Report: couldn't turn off the tablet report stream after the recording.", ex); }
            }
            await MarkDriverLogAsync("stopped");
        }
        var ended = _now();
        lock (_lock)
        {
            AppLog.Info($"=== Report: recording stopped ({_lines.Count} tablet reports, {(ended - _started).TotalSeconds:0.0} s) ===");
            Trace = Format(_started, ended, _lines, _kinds, _dropped, _timeUp, _problems);
        }
        Changed?.Invoke();
        return Trace;
    }

    private async Task MarkDriverLogAsync(string what)
    {
        if (_daemon is not IDaemonLogWriter writer)
        {
            Problem($"The driver log has no {what} mark: this driver connection can't write to it.");
            return;
        }
        bool ok;
        try { ok = await writer.WriteLogAsync(LogGroup, $"=== Report: recording {what} (Record the problem) ==="); }
        catch (Exception ex)
        {
            AppLog.Warn($"Report: couldn't write the recording {what} mark into the driver log.", ex);
            ok = false;
        }
        if (!ok) Problem($"Couldn't write the {what} mark into the driver log.");
    }

    private void Problem(string text)
    {
        lock (_lock) _problems.Add(text);
    }

    internal void OnReport(JObject data)
    {
        bool notify;
        lock (_lock)
        {
            if (!IsRecording) return;
            var at = _now() - _started;
            if (at > MaxLength) { _timeUp = true; _dropped++; notify = false; }
            else if (_lines.Count >= MaxEvents) { _dropped++; return; }
            else
            {
                var kind = Kind(data);
                _kinds[kind] = _kinds.TryGetValue(kind, out var n) ? n + 1 : 1;
                _lines.Add(FormatReport(at, kind, data));
                notify = _lines.Count == 1 || _lines.Count % 25 == 0;
            }
        }
        if (StoppedAtLimit && IsRecording) { _ = StopAsync(); return; }
        if (notify) Changed?.Invoke();
    }

    private async Task StopAtLimitAsync(CancellationToken token)
    {
        try { await Task.Delay(MaxLength, token); }
        catch (OperationCanceledException) { return; }
        lock (_lock) _timeUp = true;
        await StopAsync();
    }

    /// <summary>The report type from the daemon's "Path" (e.g. "…Wacom.IntuosV2.IntuosV2AuxReport" → "IntuosV2AuxReport").</summary>
    internal static string Kind(JObject data)
    {
        var path = data["Path"]?.ToString();
        if (string.IsNullOrWhiteSpace(path)) return "(no type)";
        var cut = path.LastIndexOfAny(['.', '/', '+']);
        return cut >= 0 && cut < path.Length - 1 ? path[(cut + 1)..] : path;
    }

    /// <summary>One line: time since start, report type, tablet and the report's values (raw bytes as hex).
    /// Serial-like values are removed; a long report is cut at <see cref="MaxDataChars"/>.</summary>
    internal static string FormatReport(TimeSpan at, string kind, JObject data)
    {
        var tablet = data["Tablet"]?["Properties"]?["Name"]?.ToString() ?? "?";
        string values;
        if (data["Data"] is JObject d)
        {
            var copy = (JObject)d.DeepClone();
            if (copy["Raw"]?.ToString() is { } raw)
            {
                copy.Remove("Raw");
                copy["RawHex"] = HasSerial(copy) ? "(left out: these bytes hold the pen's serial number)" : RawHex(raw);
            }
            values = copy.ToString(Formatting.None);
        }
        else values = data["Data"]?.ToString(Formatting.None) ?? "(no data)";
        values = SettingsBundle.RedactSerialText(values);
        if (values.Length > MaxDataChars) values = values[..MaxDataChars] + "…";
        return $"+{at.TotalSeconds,8:0.000} s  {kind}  [{tablet}]  {values}";
    }

    private static bool HasSerial(JObject data) =>
        data.Descendants().OfType<JProperty>().Any(p => p.Name.Contains("Serial", StringComparison.OrdinalIgnoreCase));

    private static string RawHex(string base64)
    {
        try { return Convert.ToHexString(Convert.FromBase64String(base64)); }
        catch (FormatException) { return "(unreadable)"; }
    }

    internal static string Format(DateTime started, DateTime ended, IReadOnlyList<string> lines,
        IReadOnlyDictionary<string, int> kinds, int dropped, bool timeUp, IReadOnlyList<string> problems)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Record the problem");
        sb.AppendLine("The user started the recording, made the problem happen, then stopped it.");
        sb.AppendLine("app.log and the driver log have '=== Report: recording started/stopped ===' marks at the same times.");
        sb.AppendLine();
        sb.AppendLine($"Started: {started:yyyy-MM-dd HH:mm:ss.fff}");
        sb.AppendLine($"Stopped: {ended:yyyy-MM-dd HH:mm:ss.fff} ({(ended - started).TotalSeconds:0.0} s)");
        sb.AppendLine($"Tablet reports kept: {lines.Count}");
        if (dropped > 0)
            sb.AppendLine($"Tablet reports not kept: {dropped} ({(timeUp ? $"after the {MaxLength.TotalMinutes:0} minute limit" : $"over the {MaxEvents} report limit")})");
        if (kinds.Count > 0)
            sb.AppendLine("By type: " + string.Join(", ", kinds.OrderByDescending(k => k.Value).ThenBy(k => k.Key, StringComparer.Ordinal)
                .Select(k => $"{k.Key} {k.Value}")));
        foreach (var p in problems) sb.AppendLine("PROBLEM: " + p);
        if (lines.Count == 0)
            sb.AppendLine(problems.Count == 0
                ? "The tablet sent nothing while recording (no pen, button, touch, ExpressKey or ring activity reached the driver)."
                : "No tablet reports were recorded.");
        sb.AppendLine();
        foreach (var l in lines) sb.AppendLine(l);
        return sb.ToString();
    }
}
