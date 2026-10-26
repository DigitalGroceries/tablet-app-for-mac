using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using OpenTabletArtist.Domain;
using OpenTabletDriver.Desktop.Profiles;

namespace OpenTabletArtist.Services;

/// <summary>Version 14: per-pen settings, kept in the app's own settings (not OTD's settings.json), like
/// <see cref="PerAppBindingStore"/>. OTD's profile keeps the main pen's settings; extra pens are overlaid at
/// runtime by <see cref="PerAppBindingRuntime"/> when they come near the tablet.</summary>
public static class PerPenStore
{
    static string Key(string tablet) => $"PerPenTables:{tablet}";
    static string MainKey(string tablet) => $"MainPen:{tablet}";

    public static IReadOnlyList<PenProfile> List(string tablet)
    {
        if (string.IsNullOrEmpty(tablet)) return Array.Empty<PenProfile>();
        var raw = AppSettings.Get(Key(tablet));
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<PenProfile>();
        try
        {
            return JsonConvert.DeserializeObject<List<PenProfile>>(raw)?
                       .Where(p => !string.IsNullOrWhiteSpace(p.Key)).ToList()
                   ?? (IReadOnlyList<PenProfile>)Array.Empty<PenProfile>();
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Couldn't read per-pen settings for {tablet}.", ex);
            return Array.Empty<PenProfile>();
        }
    }

    public static PenProfile? Find(string tablet, string? key) =>
        string.IsNullOrEmpty(key) ? null : List(tablet).FirstOrDefault(p => p.Key == key);

    /// <summary>The key of the tablet's main pen (the first pen seen), or null before any pen was seen.</summary>
    public static string? MainPen(string tablet)
    {
        var v = AppSettings.Get(MainKey(tablet));
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    public enum SeenResult { Ignored, BecameMainPen, MainPen, Known, Added }

    /// <summary>A pen came near the tablet. The first pen becomes the main pen (the ordinary Pen tile);
    /// any other new pen is added with a copy of <paramref name="mainPenBindings"/>. Pucks are ignored.
    /// The eraser end of a known pen is the same pen (same serial).</summary>
    public static SeenResult Seen(string tablet, PenToolSignal pen, BindingSettings mainPenBindings, out PenProfile? added)
    {
        added = null;
        if (string.IsNullOrEmpty(tablet) || PenBindings.IsPuck(pen.RawToolId)) return SeenResult.Ignored;
        var main = MainPen(tablet);
        if (main is null)
        {
            AppSettings.Set(MainKey(tablet), pen.Key);
            return SeenResult.BecameMainPen;
        }
        if (main == pen.Key) return SeenResult.MainPen;
        var list = List(tablet).ToList();
        if (list.Any(p => p.Key == pen.Key)) return SeenResult.Known;
        // A pen without a serial is keyed by tool id, so its eraser end looks like a new tool: skip erasers
        // until the tip end has been seen once.
        if (pen.Serial == 0 && pen.Eraser) return SeenResult.Ignored;
        added = new PenProfile
        {
            Key = pen.Key,
            Serial = pen.Serial,
            RawToolId = pen.RawToolId,
            DisplayName = PenBindings.UniqueName(pen.RawToolId, list.Select(p => p.DisplayName)),
            Bindings = PerAppBindingStore.CloneBindings(mainPenBindings),
        };
        list.Add(added);
        Save(tablet, list);
        return SeenResult.Added;
    }

    public static bool Remove(string tablet, string key)
    {
        var list = List(tablet).ToList();
        if (list.RemoveAll(p => p.Key == key) == 0) return false;
        Save(tablet, list);
        return true;
    }

    public static void SaveBindings(string tablet, string key, BindingSettings bindings)
    {
        var list = List(tablet).ToList();
        var i = list.FindIndex(p => p.Key == key);
        if (i < 0) return;
        list[i].Bindings = PerAppBindingStore.CloneBindings(bindings);
        Save(tablet, list);
    }

    static void Save(string tablet, List<PenProfile> list) =>
        AppSettings.Set(Key(tablet), JsonConvert.SerializeObject(list));
}
