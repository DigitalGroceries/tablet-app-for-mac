using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace OpenTabletArtist.Converters;

/// <summary>The app-wide heading rule (the maintainer's): headings, page titles, section titles and nav labels
/// show in sentence case — first letter capitalised, never all lowercase, never ALL CAPS — while the
/// <see cref="Exceptions"/> keep their own casing ("About ExpressKeys", "OTD settings").
/// Build M: nav button labels in "first letter only" title case — "Home", "Tablet", "Wheels"
/// (never lowercase, never ALL CAPS), whatever case the label is stored in.</summary>
public sealed class TitleCaseConverter : IValueConverter
{
    public static readonly TitleCaseConverter Instance = new();

    /// <summary>Labels shown exactly as written here, whatever case they are stored in.</summary>
    public static readonly System.Collections.Generic.IReadOnlyDictionary<string, string> Exceptions =
        new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["expresskeys"] = "ExpressKeys",
            ["json"] = "JSON",
            ["otd"] = "OTD",
            ["ota"] = "OTA",
            ["opentabletartist"] = "OpenTabletArtist",
            ["opentabletdriver"] = "OpenTabletDriver",
            ["vmulti"] = "VMulti",
        };

    public static string? Apply(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return s;
        // Build N.1: proper names / acronyms keep their own casing (Wacom's "ExpressKeys", "JSON").
        var t = s.Trim();
        if (Exceptions.TryGetValue(t, out var fixedCase)) return fixedCase;
        // Text stored ALL CAPS (the nav labels; the first letter is ignored, so "sCRIBBLE" folds too) is folded down first; otherwise the written casing is kept,
        // so "About ExpressKeys" stays as is and "about touch ring" only gains its capital.
        bool hasLower = false, hasUpper = false;
        foreach (var c in t[1..]) { hasLower |= char.IsLower(c); hasUpper |= char.IsUpper(c); }
        if (hasUpper && !hasLower) t = t.ToLowerInvariant();
        t = System.Text.RegularExpressions.Regex.Replace(t, @"[A-Za-z]+",
            m => Exceptions.TryGetValue(m.Value, out var e) ? e : m.Value);
        return char.ToUpperInvariant(t[0]) + t[1..];
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string s ? Apply(s) : value;   // R12: non-text content (a control) passes through

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value;
}
