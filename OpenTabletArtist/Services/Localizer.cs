using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Avalonia.Platform;

namespace OpenTabletArtist.Services;

/// <summary>Version 8: GUI string tables (JSON per language). British English is the source of truth;
/// missing keys fall back to English. Preference key <see cref="PreferenceKey"/> stores "system" or a
/// language code (en, es, de, fr, bg, ru, ja, zh-CN).</summary>
public static class Localizer
{
    public const string PreferenceKey = "ui.language";
    public const string SystemCode = "system";

    /// <summary>Languages offered in Settings (native labels). Order matches the GitHub Pages menu.</summary>
    public static readonly (string Code, string NativeName)[] Languages =
    {
        (SystemCode, "Match system"),
        ("en", "English"),
        ("es", "Español"),
        ("de", "Deutsch"),
        ("fr", "Français"),
        ("bg", "Български"),
        ("ru", "Русский"),
        ("ja", "日本語"),
        ("zh-CN", "中文"),
    };

    static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase)
    {
        "en", "es", "de", "fr", "bg", "ru", "ja", "zh-CN",
    };

    static Dictionary<string, string> _en = new(StringComparer.Ordinal);
    static Dictionary<string, string> _cur = new(StringComparer.Ordinal);
    static string _resolved = "en";
    static string _preference = SystemCode;
    static bool _loaded;

    /// <summary>Raised after a language change so views/view-models can refresh bound text.</summary>
    public static event Action? LanguageChanged;

    /// <summary>Effective language code after resolving Match system (never "system").</summary>
    public static string ResolvedCode => _resolved;

    /// <summary>Stored preference: "system" or a language code.</summary>
    public static string Preference => _preference;

    public static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        _en = LoadTable("en") ?? new Dictionary<string, string>(StringComparer.Ordinal);
        _preference = AppSettings.Get(PreferenceKey) ?? SystemCode;
        ApplyPreference(_preference, raise: false);
    }

    /// <summary>Sets and persists the preference, then reloads strings. Pass "system" or a code from
    /// <see cref="Languages"/>.</summary>
    public static void SetPreference(string? code)
    {
        EnsureLoaded();
        var pref = string.IsNullOrWhiteSpace(code) ? SystemCode : code.Trim();
        if (!pref.Equals(SystemCode, StringComparison.OrdinalIgnoreCase) && !Supported.Contains(pref))
            pref = SystemCode;
        AppSettings.Set(PreferenceKey, pref);
        ApplyPreference(pref, raise: true);
    }

    static void ApplyPreference(string pref, bool raise)
    {
        _preference = pref.Equals(SystemCode, StringComparison.OrdinalIgnoreCase) ? SystemCode : pref;
        _resolved = _preference == SystemCode ? ResolveSystem() : Normalize(_preference);
        _cur = _resolved == "en" ? _en : (LoadTable(_resolved) ?? _en);
        if (raise) LanguageChanged?.Invoke();
    }

    static string Normalize(string code)
    {
        if (code.Equals("zh", StringComparison.OrdinalIgnoreCase) ||
            code.StartsWith("zh-", StringComparison.OrdinalIgnoreCase))
            return "zh-CN";
        foreach (var s in Supported)
            if (s.Equals(code, StringComparison.OrdinalIgnoreCase)) return s;
        return "en";
    }

    /// <summary>Pick the best supported language from macOS / .NET culture, else English.</summary>
    public static string ResolveSystem()
    {
        try
        {
            var cultures = new List<string>();
            var ui = CultureInfo.CurrentUICulture;
            cultures.Add(ui.Name);
            cultures.Add(ui.TwoLetterISOLanguageName);
            if (ui.Parent != null && !Equals(ui.Parent, CultureInfo.InvariantCulture))
            {
                cultures.Add(ui.Parent.Name);
                cultures.Add(ui.Parent.TwoLetterISOLanguageName);
            }
            foreach (var c in cultures)
            {
                var n = Normalize(c);
                if (Supported.Contains(n) && n != "en") return n;
                if (Supported.Contains(n)) return n;
            }
        }
        catch { /* fall through */ }
        return "en";
    }

    public static string T(string key)
    {
        EnsureLoaded();
        if (_cur.TryGetValue(key, out var s) && !string.IsNullOrEmpty(s)) return s;
        if (_en.TryGetValue(key, out var e) && !string.IsNullOrEmpty(e)) return e;
        return key;
    }

    /// <summary>Like <see cref="T"/>, but when tables are not loaded (unit tests without Avalonia
    /// assets) returns <paramref name="english"/> instead of the raw key.</summary>
    public static string Te(string key, string english)
    {
        var t = T(key);
        return t == key ? english : t;
    }

    public static string Tf(string key, params (string Name, object? Value)[] args)
    {
        var s = T(key);
        foreach (var (name, value) in args)
            s = s.Replace("{" + name + "}", value?.ToString() ?? "", StringComparison.Ordinal);
        return s;
    }

    static Dictionary<string, string>? LoadTable(string code)
    {
        try
        {
            var uri = new Uri($"avares://OpenTabletArtist/Assets/i18n/{code}.json");
            using var stream = AssetLoader.Open(uri);
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return map == null ? null : new Dictionary<string, string>(map, StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Localizer: couldn't load {code}.json", ex);
            return null;
        }
    }
}
