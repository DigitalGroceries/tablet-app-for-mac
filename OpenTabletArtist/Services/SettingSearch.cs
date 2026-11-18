using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Platform;

namespace OpenTabletArtist.Services;

/// <summary>One place the setting search can open: the words shown there, where it is (the tab names in the
/// app's language, "Settings › Theme"), and the route the app follows to open it ("settings/Theme").</summary>
public sealed record SettingSearchEntry(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("route")] string Route);

/// <summary>Where an index entry leads: a page ("home", "scribble", "settings", "advanced", "tablet"), its tab
/// (a Settings or Advanced tab name; on the tablet page a Tool tile, "ToolPenButton"), then a sub-tab of its rail ("BackdropSubtab";
/// on the tablet page a tab, "EraserTab") and a details panel that starts closed ("EraserFeelDetails").</summary>
public sealed record SettingSearchRoute(string Page, string? Tab = null, string? Sub = null, string? Panel = null)
{
    public static bool TryParse(string? route, out SettingSearchRoute result)
    {
        result = new SettingSearchRoute("home");
        var p = (route ?? "").Split('/');
        string? At(int i) => i < p.Length ? p[i] : null;
        bool ok = p[0] switch
        {
            "home" or "scribble" => p.Length == 1,
            "settings" => p.Length is 2 or 3 && Enum.TryParse<Domain.SettingsTab>(p[1], out _),
            "advanced" => p.Length is 2 or 3 && Enum.TryParse<Domain.AdvancedTab>(p[1], out _),
            "tablet" => p.Length is 3 or 4 && p[1] is "ToolFunctionsButton" or "ToolPenButton" or "ToolMoreButton",
            _ => false,
        };
        if (!ok || p.Any(string.IsNullOrWhiteSpace)) return false;
        result = new SettingSearchRoute(p[0], At(1), At(2), At(3));
        return true;
    }
}

/// <summary>Version 29: Home's "Setting search". The index (one file per app language, under
/// Assets/search/) lists the words on every page and tab. It is written by a walk through the real pages
/// (tests/OpenTabletArtist.UiTests/SettingSearchIndexTests.cs), and a test fails when a page changes and
/// the index wasn't written again, so it stays current.</summary>
public static class SettingSearch
{
    /// <summary>The most matches the drop-down lists.</summary>
    public const int MaxResults = 12;

    static readonly Dictionary<string, IReadOnlyList<SettingSearchEntry>> Loaded = new(StringComparer.OrdinalIgnoreCase);

    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The index for a language (default: the app's language). English when that language has
    /// none; empty, with a warning in the log, when even English can't be read.</summary>
    public static IReadOnlyList<SettingSearchEntry> Index(string? code = null)
    {
        code ??= Localizer.ResolvedCode;
        lock (Loaded)
        {
            if (Loaded.TryGetValue(code, out var list)) return list;
            list = Read(code) ?? (code == "en" ? null : Index("en")) ?? Array.Empty<SettingSearchEntry>();
            Loaded[code] = list;
            return list;
        }
    }

    static IReadOnlyList<SettingSearchEntry>? Read(string code)
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri($"avares://OpenTabletArtist/Assets/search/{code}.json"));
            return JsonSerializer.Deserialize<List<SettingSearchEntry>>(stream, Json);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Setting search: couldn't read the {code} index", ex);
            return null;
        }
    }

    /// <summary>The places whose words (or tab names) hold every word typed, best first: words that start
    /// with what was typed, then words that hold it, then tab names. Case and accents don't matter.</summary>
    public static IReadOnlyList<SettingSearchEntry> Find(string? query, IReadOnlyList<SettingSearchEntry>? index = null)
    {
        var words = Fold(query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return Array.Empty<SettingSearchEntry>();
        index ??= Index();
        var hits = new List<(int Rank, int Order, SettingSearchEntry Entry)>();
        for (int i = 0; i < index.Count; i++)
        {
            var e = index[i];
            var text = Fold(e.Text);
            var path = Fold(e.Path);
            int rank = 0;
            foreach (var w in words)
            {
                int r = text.StartsWith(w, StringComparison.Ordinal) ? 0
                    : text.Contains(" " + w, StringComparison.Ordinal) ? 1
                    : text.Contains(w, StringComparison.Ordinal) ? 2
                    : path.Contains(w, StringComparison.Ordinal) ? 3
                    : -1;
                if (r < 0) { rank = -1; break; }
                rank = Math.Max(rank, r);
            }
            if (rank >= 0) hits.Add((rank, i, e));
        }
        return hits.OrderBy(h => h.Rank).ThenBy(h => h.Order).Select(h => h.Entry).Take(MaxResults).ToList();
    }

    /// <summary>Lower case, no accents, one space between words ("Pressión  Curve" → "presion curve").</summary>
    internal static string Fold(string s)
    {
        var d = s.Normalize(NormalizationForm.FormD);
        var b = new StringBuilder(d.Length);
        bool space = true;
        foreach (var c in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsWhiteSpace(c) || c is '›' or '/' or '-' or '…')
            {
                if (!space) b.Append(' ');
                space = true;
                continue;
            }
            b.Append(char.ToLowerInvariant(c));
            space = false;
        }
        return b.ToString().TrimEnd().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Forget the loaded indexes (tests, and the index walk after it wrote new ones).</summary>
    internal static void ResetForTests()
    {
        lock (Loaded) Loaded.Clear();
    }
}
