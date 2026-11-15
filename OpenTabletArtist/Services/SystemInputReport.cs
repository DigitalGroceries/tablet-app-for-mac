using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenTabletArtist.Services;

/// <summary>One active event tap: a program that sees (and, unless listen-only, can change or drop) mouse and keyboard events.</summary>
public sealed record EventTapInfo(uint Id, uint TapPoint, bool ListenOnly, ulong EventMask, int TappingPid, string TappingName,
    int TappedPid, bool Enabled);

/// <summary>
/// TASKS #81 (c) and (e): parts of the diagnostic report that show whether something outside our app changes the pen's
/// clicks and keys. (c) every active event tap with its owner (CGGetEventTapList: no permission needed); (e) the
/// Input Monitoring and Accessibility state. The driver is started by the app, so macOS credits its access to the app
/// (the responsible process): the app's state is the driver's state. Formatting is in pure functions for the tests.
/// </summary>
public static class SystemInputReport
{
    // ---------- (e) permissions ----------

    /// <summary>Input Monitoring answer from IOHIDCheckAccess: 0 granted, 1 denied, 2 unknown (never asked).</summary>
    internal static string FormatPermissions(bool accessibility, uint inputMonitoring) =>
        "  Input Monitoring (driver reads the tablet): " + inputMonitoring switch
        {
            0 => "allowed",
            1 => "NOT allowed",
            2 => "not asked yet",
            _ => $"unknown ({inputMonitoring})",
        } + "\n" +
        "  Accessibility / Device Control (clicks, keys, scroll): " + (accessibility ? "allowed" : "NOT allowed") + "\n" +
        "  (The driver runs under the app, so macOS gives it the app's permissions.)";

    /// <summary>Permissions section for summary.txt. Never throws.</summary>
    public static string CollectPermissions()
    {
        if (!OperatingSystem.IsMacOS()) return "  (macOS only)";
        try { return FormatPermissions(MacAccessibility.IsTrusted(), IOHIDCheckAccess(ListenEvent)); }
        catch (Exception ex)
        {
            AppLog.Warn("Diagnostics: couldn't read the permission state.", ex);
            return "  (could not be read: " + ex.GetType().Name + ")";
        }
    }

    // ---------- (c) event taps ----------

    /// <summary>Which kinds of events a tap's mask covers (CGEventType numbers).</summary>
    internal static string DescribeMask(ulong mask)
    {
        if (mask == ulong.MaxValue || mask == 0xFFFFFFFFUL) return "all events";
        bool Has(params int[] types) => types.Any(t => (mask & (1UL << t)) != 0);
        var kinds = new List<string>();
        if (Has(1, 2, 3, 4, 25, 26)) kinds.Add("mouse clicks");
        if (Has(5, 6, 7, 27)) kinds.Add("mouse moves/drags");
        if (Has(22)) kinds.Add("scroll");
        if (Has(10, 11)) kinds.Add("keys");
        if (Has(12)) kinds.Add("modifier keys");
        if (Has(23, 24)) kinds.Add("tablet");
        return kinds.Count == 0 ? $"other (mask 0x{mask:X})" : string.Join(", ", kinds);
    }

    static string Point(uint p) => p switch { 0 => "HID (before every app)", 1 => "session", 2 => "annotated session", _ => p.ToString() };

    /// <summary>Pure: the event-tap section. Taps owned by our own app (<paramref name="ownPid"/>) and the driver are marked.</summary>
    internal static string FormatEventTaps(IReadOnlyList<EventTapInfo>? taps, int ownPid, int? driverPid, string? problem = null)
    {
        if (taps == null) return "  (could not be read" + (problem != null ? ": " + problem : "") + ")";
        if (taps.Count == 0) return "  (none)";
        var sb = new StringBuilder();
        foreach (var t in taps.OrderBy(t => t.TappingName, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.Id))
        {
            var who = t.TappingPid == ownPid ? $"{t.TappingName} (this app)"
                : t.TappingPid == driverPid ? $"{t.TappingName} (our driver)" : t.TappingName;
            var target = t.TappedPid == 0 ? "all apps" : $"pid {t.TappedPid}";
            sb.AppendLine($"  {who} · pid {t.TappingPid} · {(t.ListenOnly ? "listens only" : "CAN CHANGE OR DROP events")} · "
                + $"{DescribeMask(t.EventMask)} · {Point(t.TapPoint)}, {target}{(t.Enabled ? "" : " · switched off")}");
        }
        var others = taps.Count(t => !t.ListenOnly && t.Enabled && t.TappingPid != ownPid && t.TappingPid != driverPid);
        sb.Append(others == 0 ? "  No other program can change mouse or keyboard events."
            : $"  {others} tap(s) of other programs can change or drop mouse/keyboard events.");
        return sb.ToString();
    }

    /// <summary>Event-tap section for summary.txt. Never throws.</summary>
    public static string CollectEventTaps(int? driverPid = null)
    {
        if (!OperatingSystem.IsMacOS()) return "  (macOS only)";
        try { return FormatEventTaps(ReadEventTaps(), Environment.ProcessId, driverPid); }
        catch (Exception ex)
        {
            AppLog.Warn("Diagnostics: couldn't read the event taps.", ex);
            return FormatEventTaps(null, Environment.ProcessId, driverPid, ex.GetType().Name);
        }
    }

    static IReadOnlyList<EventTapInfo> ReadEventTaps()
    {
        uint count = 0;
        var err = CGGetEventTapList(0, null, ref count);
        if (err != 0) throw new InvalidOperationException($"CGGetEventTapList returned {err}");
        if (count == 0) return Array.Empty<EventTapInfo>();
        var raw = new CGEventTapInformation[count];
        err = CGGetEventTapList(count, raw, ref count);
        if (err != 0) throw new InvalidOperationException($"CGGetEventTapList returned {err}");
        return raw.Take((int)count).Select(r => new EventTapInfo(r.eventTapID, r.tapPoint, r.options == 1, r.eventsOfInterest,
            r.tappingProcess, ProcessName(r.tappingProcess), r.processBeingTapped, r.enabled != 0)).ToList();
    }

    static string ProcessName(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return p.ProcessName; }
        catch (Exception) { return "(ended)"; }
    }

    const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    const string IOKit = "/System/Library/Frameworks/IOKit.framework/IOKit";
    const uint ListenEvent = 1;   // kIOHIDRequestTypeListenEvent

    [StructLayout(LayoutKind.Sequential)]
    struct CGEventTapInformation
    {
        public uint eventTapID;
        public uint tapPoint;
        public uint options;
        public ulong eventsOfInterest;
        public int tappingProcess;
        public int processBeingTapped;
        public byte enabled;
        public float minUsecLatency;
        public float avgUsecLatency;
        public float maxUsecLatency;
    }

    [DllImport(CoreGraphics)]
    static extern int CGGetEventTapList(uint maxNumberOfTaps, [Out] CGEventTapInformation[]? tapList, ref uint eventTapCount);

    [DllImport(IOKit)] static extern uint IOHIDCheckAccess(uint requestType);
}
