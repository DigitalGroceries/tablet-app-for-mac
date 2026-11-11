using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTabletArtist.Services;

/// <summary>
/// #38 (follow-up to #22/#81): the app's own settings for a tablet's main ("All") page that live beside the
/// driver profile: the "All buttons" and Touch Ring switches and what they put aside, each ExpressKey's and
/// each ring's own Off, the pen tip / eraser / pressure-curve switches' saved copies, the ring-mode lists,
/// "All displays" and the manual pressure values. Restore factory settings resets them; Save as default
/// keeps a copy and Revert to default puts that copy back, so the switches always match the bindings.
/// Per-app and per-pen tables are left alone here (Zak decides).
/// </summary>
public static class TabletAppState
{
    /// <summary>Keys stored as <c>Name:{tablet}</c>.</summary>
    public static readonly IReadOnlyList<string> Names = new[]
    {
        "AuxEnabled", "AuxBackup", "AuxKeyOff", "WheelEnabled", "WheelBackup", "WheelOff",
        "TipBackup", "EraserBackup", "CurveBackup", "MapAllDisplays", "ManualTipPressure", "ManualEraserPressure",
    };

    /// <summary>Every main-page key of <paramref name="tablet"/> that is stored now, plus the fixed names.</summary>
    public static IReadOnlyList<string> KeysOf(string tablet)
    {
        var keys = Names.Select(n => $"{n}:{tablet}").ToList();
        foreach (var prefix in new[] { $"RingModes:{tablet}:", $"RingModesBackup:{tablet}:" })
            keys.AddRange(AppSettings.Keys(prefix).Where(k => IsMainScope(k, prefix)));
        return keys;
    }

    /// <summary>"RingModes:{tablet}:0" is the main page; "…:0:app:…" belongs to a per-app table.</summary>
    public static bool IsMainScope(string key, string prefix) =>
        key.Length > prefix.Length && key[prefix.Length..].All(char.IsDigit);

    /// <summary>The stored values of every main-page key of these tablets.</summary>
    public static Dictionary<string, string> Snapshot(IEnumerable<string> tablets)
    {
        var snap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var tablet in Tablets(tablets))
            foreach (var key in KeysOf(tablet))
                if (AppSettings.Get(key) is { } value)
                    snap[key] = value;
        return snap;
    }

    /// <summary>Put these tablets' main-page keys back to <paramref name="snapshot"/>: keys it doesn't hold
    /// are removed (back to the app's own defaults). Other keys, other tablets and per-app tables are kept.
    /// One write. Returns false when the write failed.</summary>
    public static bool Restore(IReadOnlyDictionary<string, string> snapshot, IEnumerable<string> tablets)
    {
        var list = Tablets(tablets).ToList();
        var owned = new HashSet<string>(list.SelectMany(KeysOf), StringComparer.Ordinal);
        var values = snapshot.Where(kv => list.Any(t => Belongs(kv.Key, t)))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        foreach (var key in values.Keys) owned.Add(key);
        return AppSettings.ReplaceAll(values, key => !owned.Contains(key));
    }

    /// <summary>Back to the app's own defaults: every switch On, nothing put aside, no ring-mode lists.</summary>
    public static bool ResetToFactory(IEnumerable<string> tablets) =>
        Restore(new Dictionary<string, string>(), tablets);

    private static bool Belongs(string key, string tablet) =>
        Names.Any(n => key == $"{n}:{tablet}")
        || IsMainScope(key, $"RingModes:{tablet}:") && key.StartsWith($"RingModes:{tablet}:", StringComparison.Ordinal)
        || IsMainScope(key, $"RingModesBackup:{tablet}:") && key.StartsWith($"RingModesBackup:{tablet}:", StringComparison.Ordinal);

    private static IEnumerable<string> Tablets(IEnumerable<string> tablets) =>
        tablets.Where(t => !string.IsNullOrEmpty(t)).Distinct(StringComparer.Ordinal);
}
