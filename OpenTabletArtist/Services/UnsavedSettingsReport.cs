using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using OtdInterop;

namespace OpenTabletArtist.Services;

/// <summary>The settings as the app holds them when the report is made: save state and the working copy (JSON).</summary>
public sealed record SettingsState(string SaveState, bool HasUnsavedChanges, string? LiveJson, string? Problem = null);

/// <summary>
/// TASKS #81 (b): the report shows the saved settings file next to the settings the app is using now (every edit is
/// live on the driver before Save), with the differences, so we can tell an unsaved edit from a bug.
/// </summary>
public static class UnsavedSettingsReport
{
    public const string FileName = "settings/unsaved-changes.txt";
    public const string LiveFileName = "settings/live-otd-settings.json";
    const int MaxLines = 400;

    /// <summary>Takes the working copy on the caller's thread (the UI thread). Never throws; a failure is in the result.</summary>
    public static SettingsState? Capture(AppSession? session)
    {
        if (session == null) return null;
        try
        {
            string? json = null;
            if (session.CurrentSettings is { } live)
            {
                using var ms = new MemoryStream();
                SettingsCodec.Encode(live, ms);
                json = System.Text.Encoding.UTF8.GetString(ms.ToArray());
            }
            return new SettingsState(session.SaveState.ToString(), session.HasUnsavedChanges, json);
        }
        catch (Exception ex)
        {
            AppLog.Warn("Report: couldn't read the working settings.", ex);
            return new SettingsState("(unknown)", false, null, ex.GetType().Name + ": " + ex.Message);
        }
    }

    /// <summary>Pure: every value that differs, as "path: saved → live". Paths use JSON-path style.</summary>
    internal static IReadOnlyList<string> Diff(JToken saved, JToken live)
    {
        var a = Flatten(saved); var b = Flatten(live);
        var lines = new List<string>();
        foreach (var key in a.Keys.Union(b.Keys).OrderBy(k => k, StringComparer.Ordinal))
        {
            var has1 = a.TryGetValue(key, out var v1); var has2 = b.TryGetValue(key, out var v2);
            if (has1 && has2 && v1 == v2) continue;
            lines.Add(!has1 ? $"+ {key}: {v2}" : !has2 ? $"- {key}: {v1}" : $"~ {key}: {v1} → {v2}");
        }
        return lines;
    }

    static Dictionary<string, string> Flatten(JToken root)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        void Walk(JToken t)
        {
            switch (t)
            {
                case JObject o when o.HasValues: foreach (var p in o.Properties()) Walk(p.Value); break;
                case JArray arr when arr.HasValues: foreach (var c in arr) Walk(c); break;
                case JContainer c: map[c.Path] = c is JObject ? "{}" : "[]"; break;
                case JValue v: map[v.Path] = v.Type == JTokenType.Null ? "null" : v.Type == JTokenType.String ? "\"" + v + "\"" : v.ToString(Newtonsoft.Json.Formatting.None); break;
            }
        }
        Walk(root);
        return map;
    }

    /// <summary>Pure: unsaved-changes.txt. <paramref name="savedJson"/> and the state's JSON are already made portable.</summary>
    internal static string Format(SettingsState? state, string? savedJson, string? livePortableJson)
    {
        var sb = new StringBuilder();
        if (state == null) return "Settings state: not available (the report was made outside the main window)." + Environment.NewLine;
        sb.AppendLine("Save state: " + state.SaveState + (state.HasUnsavedChanges ? " (there are unsaved changes)" : ""));
        sb.AppendLine("Compared here: the driver settings only. The app's own settings (per-app ExpressKeys and pen bindings, ring modes) are saved as soon as they change, so they are not compared; their saved copy is in settings/ of this report.");
        if (state.Problem != null) sb.AppendLine("The working settings couldn't be read: " + state.Problem);
        if (livePortableJson == null) { sb.AppendLine("Working settings: none (no settings loaded from the driver)."); return sb.ToString(); }
        if (savedJson == null) { sb.AppendLine("Saved settings file: not found or unreadable."); return sb.ToString(); }
        IReadOnlyList<string> diff;
        try { diff = Diff(JToken.Parse(savedJson), JToken.Parse(livePortableJson)); }
        catch (Exception ex) { sb.AppendLine("The two settings couldn't be compared: " + ex.GetType().Name); return sb.ToString(); }
        sb.AppendLine(diff.Count == 0
            ? "The settings in use are the same as the saved file."
            : $"{diff.Count} difference(s) between the saved file (otd-settings.json) and the settings in use ({Path.GetFileName(LiveFileName)}):");
        foreach (var l in diff.Take(MaxLines)) sb.AppendLine("  " + l);
        if (diff.Count > MaxLines) sb.AppendLine($"  … and {diff.Count - MaxLines} more");
        sb.AppendLine("(+ only in use, - only saved, ~ changed: saved → in use)");
        return sb.ToString();
    }

    /// <summary>Adds unsaved-changes.txt and the working copy to the zip. Never throws; a failure is logged and written.</summary>
    public static void AddTo(string zip, SettingsState? state, Func<string?>? savedPortable = null)
    {
        try
        {
            var live = state?.LiveJson is { } j ? SettingsBundle.MakePortable(j) : null;
            var saved = (savedPortable ?? ReadSavedPortable)();
            DiagnosticBundle.AddText(zip, FileName, Format(state, saved, live));
            if (live != null) DiagnosticBundle.AddText(zip, LiveFileName, live);
        }
        catch (Exception ex)
        {
            AppLog.Warn("Report: couldn't add the unsaved-settings comparison.", ex);
            try { DiagnosticBundle.AddText(zip, FileName, "The settings comparison failed: " + ex.GetType().Name + Environment.NewLine); }
            catch (Exception ex2) { AppLog.Warn("Report: couldn't write the settings comparison note.", ex2); }
        }
    }

    static string? ReadSavedPortable() =>
        SettingsBundle.Collect(portable: true).TryGetValue("otd-settings.json", out var s) ? s : null;
}
