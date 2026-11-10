using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using OpenTabletDriver.Desktop.Profiles;

namespace OpenTabletArtist.Services;

/// <summary>
/// TASKS #29 (Zak, 10 Nov 2026): each Touch Ring or Touch Strip has its own On/Off, which never clashes with the
/// main switch (Touch Rings / Touch Strips). An Off ring keeps its settings but does nothing.
///
/// How it is stored (the same way the main switch keeps its settings): the tablet's profile holds <b>no binding</b>
/// for an Off ring (no turn actions, no button actions), and the settings it had are kept here, in the app
/// settings, under <c>WheelOff:{tablet}</c>, as a JSON object <c>{ "2": &lt;WheelBindingSettings&gt; }</c>: each listed
/// ring number (1-based) is Off, and its value is what comes back when it is turned On. A ring that is not listed
/// is On. Like the main switch, this is per tablet (not per application).
/// A ring works only when the main switch is On <i>and</i> the ring is On (<see cref="Works"/>); switching the main
/// switch never changes any ring's own On/Off.
/// </summary>
public static class WheelOffStore
{
    public static string Key(string tablet) => $"WheelOff:{tablet}";

    /// <summary>A ring works only when the main switch is On and the ring itself is On.</summary>
    public static bool Works(bool allRingsOn, bool ringOn) => allRingsOn && ringOn;

    /// <summary>The Off rings of one tablet: ring number (1-based) → the settings kept for it.</summary>
    public static Dictionary<int, WheelBindingSettings> Load(string tablet)
    {
        var raw = AppSettings.Get(Key(tablet));
        if (string.IsNullOrWhiteSpace(raw)) return new();
        try
        {
            return JsonConvert.DeserializeObject<Dictionary<int, WheelBindingSettings>>(raw) ?? new();
        }
        catch (Exception ex)
        {
            AppLog.Warn("Couldn't read the Off rings; treating every ring as On.", ex);
            return new();
        }
    }

    public static void Save(string tablet, Dictionary<int, WheelBindingSettings> off) =>
        AppSettings.Set(Key(tablet), off.Count == 0 ? "" : JsonConvert.SerializeObject(off));

    /// <summary>A deep copy (the profile's object must not be shared with the kept copy).</summary>
    public static WheelBindingSettings Copy(WheelBindingSettings w) =>
        JsonConvert.DeserializeObject<WheelBindingSettings>(JsonConvert.SerializeObject(w))!;

    /// <summary>Empties the listed rings (1-based) in a table: no turn actions, no button actions. Thresholds and
    /// step size stay, so the ring comes back exactly as it was. Numbers past the table's end are ignored.</summary>
    public static void Clear(IList<WheelBindingSettings> wheels, IEnumerable<int> ringNumbers)
    {
        foreach (var n in ringNumbers.Distinct())
        {
            if (n < 1 || n > wheels.Count) continue;
            var w = wheels[n - 1];
            w.ClockwiseRotation = null;
            w.CounterClockwiseRotation = null;
            for (int b = 0; b < w.WheelButtons.Count; b++) w.WheelButtons[b] = null!;
        }
    }

    /// <summary>Puts a kept ring's turn and button actions back into a table (1-based ring number).</summary>
    public static void Restore(IList<WheelBindingSettings> wheels, int ringNumber, WheelBindingSettings kept)
    {
        if (ringNumber < 1 || ringNumber > wheels.Count) return;
        var w = wheels[ringNumber - 1];
        w.ClockwiseRotation = kept.ClockwiseRotation;
        w.CounterClockwiseRotation = kept.CounterClockwiseRotation;
        for (int b = 0; b < w.WheelButtons.Count && b < kept.WheelButtons.Count; b++) w.WheelButtons[b] = kept.WheelButtons[b];
    }

    /// <summary>The rings an application's table must have emptied before it goes to the driver: every ring when the
    /// main switch is Off, otherwise the Off rings. (All's own table is already emptied by the switches.)</summary>
    public static IReadOnlyList<int> RingsToEmpty(int ringCount, bool allRingsOn, IEnumerable<int> offRings) =>
        allRingsOn ? offRings.Where(n => n >= 1 && n <= ringCount).Distinct().OrderBy(n => n).ToList()
                   : Enumerable.Range(1, ringCount).ToList();
}
