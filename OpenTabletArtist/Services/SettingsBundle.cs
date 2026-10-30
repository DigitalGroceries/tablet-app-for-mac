using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OpenTabletArtist.Services;

/// <summary>One line of an import preview: what an imported file would do to this Mac.</summary>
public sealed record SettingsChange(string Area, string Item, string Kind);

/// <summary>What a settings file holds compared with this Mac, shown before anything is applied.</summary>
public sealed record SettingsPreview(string SourceVersion, DateTime Created, IReadOnlyList<SettingsChange> Changes)
{
    public bool HasChanges => Changes.Any(c => c.Kind != "same");
}

/// <summary>
/// Full settings export and import (idea from MockTab by Cyzor, GPL-3; our own code).
/// One file (a zip with the <see cref="Extension"/> extension) holds the app's own preferences
/// (tablets' per-pen and per-app tables, ring modes, language, theme …), OpenTabletDriver's
/// settings (bindings, mapping, filters and calibration), presets and custom tablet configurations.
/// A <em>portable</em> export can be emailed to another user: tablet serial numbers are removed,
/// the home folder is written as "~", and keys that only make sense on this Mac are left out.
/// Every version change makes an automatic local backup; the last <see cref="KeepAutoBackups"/> are kept.
/// </summary>
public static class SettingsBundle
{
    public const string Extension = "tabletsettings";
    /// <summary>The document type declared in Info.plist (UTExportedTypeDeclarations).</summary>
    public const string TypeIdentifier = "com.freetabletapp.mac.tabletsettings";
    public const int FormatVersion = 1;
    public const int KeepAutoBackups = 5;
    public const string LastVersionKey = "backup.lastVersion";
    const string AutoPrefix = "auto-";

    /// <summary>App-settings keys that describe this Mac rather than the user's choices. Never exported
    /// portably and never overwritten by an import.</summary>
    public static readonly string[] MachineKeyPrefixes =
    {
        "daemon.userPath", "backup.", "window.", "Window", "update.", "updater.", "legacyPath",
        "LoginItem", "autostart", "AutoStart", "firstRun", "lastSeen",
    };

    // Test seams: where the data lives. Defaults follow the app and OpenTabletDriver.
    public static Func<string> AppDataDir = () => AppPaths.LocalAppData;
    public static Func<string> OtdDataDir = DefaultOtdDir;
    public static Func<string> HomeDir = () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string BackupsDir => Path.Combine(AppDataDir(), "Backups");

    static string DefaultOtdDir()
    {
        try { return OpenTabletDriver.Desktop.AppInfo.Current.AppDataDirectory; }
        catch
        {
            return OperatingSystem.IsMacOS()
                ? Path.Combine(HomeDir(), "Library", "Application Support", "OpenTabletDriver")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenTabletDriver");
        }
    }

    public static string AppVersion =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0";

    /// <summary>The suggested file name for an export: tablet model without spaces or punctuation, "_",
    /// then the date as ddMMMyy — e.g. "Intuos4PTK440_30Oct26.tabletsettings"; "TabletApp_30Oct26" when no
    /// tablet is connected. The user can still type another name.</summary>
    public static string SuggestedFileName(string? tabletName, DateTime now)
    {
        var model = new string((tabletName ?? "").Where(char.IsLetterOrDigit).ToArray());
        if (model.StartsWith("Wacom", StringComparison.OrdinalIgnoreCase) && model.Length > 5) model = model[5..];
        // OpenTabletDriver names the Intuos4 by its model code only ("Wacom PTK-440"); add the family name.
        if (Regex.IsMatch(model, "^PTK\\d40", RegexOptions.IgnoreCase)) model = "Intuos4" + model;
        if (model.Length == 0) model = "TabletApp";
        var date = now.ToString("ddMMMyy", System.Globalization.CultureInfo.InvariantCulture);
        return $"{model}_{date}.{Extension}";
    }

    // ---------------------------------------------------------------- export

    /// <summary>Writes every setting into one file at <paramref name="path"/>.</summary>
    public static void Export(string path, bool portable)
    {
        var files = Collect(portable);
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        using (var fs = File.Create(tmp))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var manifest = new JObject
            {
                ["format"] = FormatVersion,
                ["app"] = "Tablet App for Mac",
                ["appVersion"] = AppVersion,
                ["created"] = DateTime.UtcNow.ToString("o"),
                ["portable"] = portable,
                ["files"] = new JArray(files.Keys.OrderBy(k => k)),
            };
            Write(zip, "manifest.json", manifest.ToString(Formatting.Indented));
            foreach (var (name, text) in files) Write(zip, name, text);
        }
        File.Move(tmp, path, overwrite: true);
    }

    static void Write(ZipArchive zip, string name, string text)
    {
        var e = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
        w.Write(text);
    }

    /// <summary>The bundle's entries (name → text) as they would be written now.</summary>
    internal static Dictionary<string, string> Collect(bool portable)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        var app = Path.Combine(AppDataDir(), "settings.json");
        if (TryReadJson(app, out var appJson) && appJson is JObject appObj)
        {
            if (portable)
                foreach (var p in appObj.Properties().ToList())
                    if (IsMachineKey(p.Name)) p.Remove();
            files["app-settings.json"] = Finish(appObj, portable);
        }
        var otd = OtdDataDir();
        if (TryReadJson(Path.Combine(otd, "settings.json"), out var otdJson))
            files["otd-settings.json"] = Finish(otdJson!, portable);
        foreach (var (sub, prefix) in new[] { ("Presets", "presets/"), ("Configurations", "configurations/") })
        {
            var d = Path.Combine(otd, sub);
            if (!Directory.Exists(d)) continue;
            foreach (var f in Directory.EnumerateFiles(d, "*.json", SearchOption.AllDirectories))
                if (TryReadJson(f, out var j))
                    files[prefix + Path.GetRelativePath(d, f).Replace('\\', '/')] = Finish(j!, portable);
        }
        return files;
    }

    static string Finish(JToken json, bool portable)
    {
        if (portable)
        {
            RedactSerials(json);
            json = Rewrite(json, s => ToPortablePath(s, HomeDir()));
        }
        return json.ToString(Formatting.Indented);
    }

    public static bool IsMachineKey(string key) =>
        MachineKeyPrefixes.Any(p => key.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    // ---------------------------------------------------------------- redaction (shared with diagnostics)

    static readonly Regex SerialText = new(
        @"(?i)(serial(?:\s*number|_?no|number)?\s*[""']?\s*[:=]\s*[""']?)([^""',\s\]\}\)]+)",
        RegexOptions.Compiled);

    /// <summary>Removes the value of every JSON property whose name mentions a serial number.</summary>
    public static void RedactSerials(JToken token)
    {
        foreach (var p in All(token).OfType<JProperty>().ToList())
            if (p.Name.Contains("serial", StringComparison.OrdinalIgnoreCase) && p.Value.Type == JTokenType.String)
                p.Value = "[removed]";
    }

    /// <summary>Removes serial numbers from free text such as a log ("Serial: 8HAB001234" → "Serial: [removed]").</summary>
    public static string RedactSerialText(string text) => SerialText.Replace(text, "$1[removed]");

    /// <summary>Writes this user's home folder as "~" so a file works on another Mac.</summary>
    public static string ToPortablePath(string s, string home) =>
        string.IsNullOrEmpty(home) ? s : s.Replace(home.TrimEnd('/') + "/", "~/", StringComparison.Ordinal);

    /// <summary>The reverse of <see cref="ToPortablePath"/> for the importing user.</summary>
    public static string FromPortablePath(string s, string home) =>
        s.StartsWith("~/", StringComparison.Ordinal) ? home.TrimEnd('/') + s.Substring(1) : s;

    static IEnumerable<JToken> All(JToken t) => t is JContainer c ? c.DescendantsAndSelf() : new[] { t };

    static JToken Rewrite(JToken t, Func<string, string> f)
    {
        var copy = t.DeepClone();
        foreach (var v in All(copy).OfType<JValue>().ToList())
            if (v.Type == JTokenType.String && v.Value is string s) v.Value = f(s);
        // App settings store some tables as JSON inside strings; the replace above already covers them.
        return copy;
    }

    static bool TryReadJson(string path, out JToken? json)
    {
        json = null;
        try
        {
            if (!File.Exists(path)) return false;
            json = JToken.Parse(File.ReadAllText(path));
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Settings backup: couldn't read {Path.GetFileName(path)}", ex);
            return false;
        }
    }

    // ---------------------------------------------------------------- import

    public sealed class Bundle
    {
        public JObject Manifest { get; init; } = new();
        public Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>Reads a settings file. Throws <see cref="InvalidDataException"/> when it isn't one.</summary>
    public static Bundle Read(string path)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using var zip = ZipFile.OpenRead(path);
            foreach (var e in zip.Entries)
            {
                // Only plain names inside the file; never a path that climbs out of its folder.
                if (e.FullName.EndsWith('/') || e.FullName.Contains("..") || Path.IsPathRooted(e.FullName)) continue;
                if (e.Length > 50_000_000) continue;
                using var r = new StreamReader(e.Open());
                entries[e.FullName] = r.ReadToEnd();
            }
        }
        catch (InvalidDataException) { throw new InvalidDataException("This isn't a Tablet App settings file."); }
        if (!entries.Remove("manifest.json", out var m))
            throw new InvalidDataException("This isn't a Tablet App settings file.");
        var b = new Bundle { Manifest = JObject.Parse(m) };
        if ((int?)b.Manifest["format"] is not int f) throw new InvalidDataException("This isn't a Tablet App settings file.");
        if (f > FormatVersion) throw new InvalidDataException("This settings file was made by a newer version. Update the app first.");
        foreach (var (k, v) in entries) b.Files[k] = v;
        return b;
    }

    /// <summary>What importing <paramref name="b"/> would change, without changing anything.</summary>
    public static SettingsPreview Preview(Bundle b)
    {
        var changes = new List<SettingsChange>();
        var home = HomeDir();
        var current = Collect(portable: false);

        if (b.Files.TryGetValue("app-settings.json", out var appText) && JToken.Parse(appText) is JObject incoming)
        {
            current.TryGetValue("app-settings.json", out var curText);
            var cur = curText == null ? new JObject() : (JObject)JToken.Parse(curText);
            foreach (var p in incoming.Properties())
            {
                if (IsMachineKey(p.Name)) continue;
                var val = Rewrite(p.Value, s => FromPortablePath(s, home));
                var kind = cur[p.Name] == null ? "new" : JToken.DeepEquals(cur[p.Name], val) ? "same" : "changed";
                changes.Add(new SettingsChange(AreaOfAppKey(p.Name), Describe(p.Name), kind));
            }
        }
        if (b.Files.TryGetValue("otd-settings.json", out var otdText))
        {
            var inc = JToken.Parse(otdText);
            current.TryGetValue("otd-settings.json", out var curOtd);
            var curTok = curOtd == null ? null : JToken.Parse(curOtd);
            var incProfiles = inc["Profiles"] as JArray ?? new JArray();
            var curProfiles = curTok?["Profiles"] as JArray ?? new JArray();
            foreach (var prof in incProfiles)
            {
                var name = (string?)prof["Tablet"] ?? "?";
                var match = curProfiles.FirstOrDefault(c => (string?)c["Tablet"] == name);
                var kind = match == null ? "new" : JToken.DeepEquals(Normalise(match), Normalise(prof)) ? "same" : "changed";
                changes.Add(new SettingsChange("tablet", name, kind));
            }
        }
        foreach (var (name, text) in b.Files.Where(f => f.Key.StartsWith("presets/") || f.Key.StartsWith("configurations/")))
        {
            var area = name.StartsWith("presets/") ? "preset" : "config";
            var item = Path.GetFileNameWithoutExtension(name);
            var kind = !current.TryGetValue(name, out var c) ? "new"
                : JToken.DeepEquals(JToken.Parse(c), Rewrite(JToken.Parse(text), s => FromPortablePath(s, home))) ? "same" : "changed";
            changes.Add(new SettingsChange(area, item, kind));
        }
        DateTime.TryParse((string?)b.Manifest["created"], null, System.Globalization.DateTimeStyles.RoundtripKind, out var created);
        return new SettingsPreview((string?)b.Manifest["appVersion"] ?? "?", created.ToLocalTime(), changes);
    }

    static JToken Normalise(JToken t)
    {
        var c = t.DeepClone();
        RedactSerials(c);
        return c;
    }

    static string AreaOfAppKey(string key) =>
        key.StartsWith("PerPen") || key.StartsWith("MainPen") ? "pen"
        : key.StartsWith("PerApp") ? "app"
        : key.StartsWith("RingModes") ? "ring"
        : "pref";

    static string Describe(string key)
    {
        var i = key.IndexOf(':');
        return i > 0 ? key.Substring(i + 1) : key;
    }

    /// <summary>Applies an imported file. Makes an automatic backup first so the import can be undone.
    /// Returns the path of that safety backup.</summary>
    public static string Apply(Bundle b)
    {
        var safety = MakeBackup("before-import");
        var home = HomeDir();
        string Local(string text) => Rewrite(JToken.Parse(text), s => FromPortablePath(s, home)).ToString(Formatting.Indented);

        if (b.Files.TryGetValue("app-settings.json", out var appText) && JToken.Parse(Local(appText)) is JObject inc)
        {
            var values = new Dictionary<string, string>();
            foreach (var p in inc.Properties())
                if (!IsMachineKey(p.Name))
                    values[p.Name] = p.Value.Type == JTokenType.String ? (string)p.Value! : p.Value.ToString(Formatting.None);
            AppSettings.SetMany(values);
        }
        var otd = OtdDataDir();
        Directory.CreateDirectory(otd);
        if (b.Files.TryGetValue("otd-settings.json", out var otdText))
        {
            var incoming = JToken.Parse(Local(otdText));
            // A portable file has serials removed; keep this Mac's own where the tablet matches.
            var curPath = Path.Combine(otd, "settings.json");
            if (TryReadJson(curPath, out var cur) && cur != null) KeepLocalSerials(incoming, cur);
            WriteAtomic(curPath, incoming.ToString(Formatting.Indented));
        }
        foreach (var (name, text) in b.Files)
        {
            string? dest = name.StartsWith("presets/") ? Path.Combine(otd, "Presets", name["presets/".Length..])
                : name.StartsWith("configurations/") ? Path.Combine(otd, "Configurations", name["configurations/".Length..])
                : null;
            if (dest == null) continue;
            var full = Path.GetFullPath(dest);
            if (!full.StartsWith(Path.GetFullPath(otd), StringComparison.Ordinal)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            WriteAtomic(full, Local(text));
        }
        return safety;
    }

    static void KeepLocalSerials(JToken incoming, JToken current)
    {
        foreach (var p in All(incoming).OfType<JProperty>().ToList())
        {
            if (!p.Name.Contains("serial", StringComparison.OrdinalIgnoreCase) || (string?)p.Value != "[removed]") continue;
            var local = current.SelectToken(p.Path);
            p.Value = local?.DeepClone() ?? JValue.CreateNull();
        }
    }

    static void WriteAtomic(string path, string text)
    {
        var tmp = path + ".import-tmp";
        File.WriteAllText(tmp, text);
        File.Move(tmp, path, overwrite: true);
    }

    // ---------------------------------------------------------------- automatic backups

    /// <summary>Writes a full local backup into <see cref="BackupsDir"/> and prunes old automatic ones.</summary>
    public static string MakeBackup(string reason)
    {
        Directory.CreateDirectory(BackupsDir);
        var safeReason = Regex.Replace(reason, "[^A-Za-z0-9._-]", "_");
        var path = Path.Combine(BackupsDir, $"{AutoPrefix}{DateTime.Now:yyyy-MM-dd-HHmmss}-{safeReason}.{Extension}");
        Export(path, portable: false);
        Prune();
        return path;
    }

    /// <summary>Keeps the newest <see cref="KeepAutoBackups"/> automatic backups.</summary>
    public static void Prune()
    {
        if (!Directory.Exists(BackupsDir)) return;
        foreach (var old in Directory.EnumerateFiles(BackupsDir, AutoPrefix + "*." + Extension)
                     .OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal).Skip(KeepAutoBackups))
        {
            try { File.Delete(old); } catch (Exception ex) { AppLog.Debug("Couldn't prune an old backup.", ex); }
        }
    }

    public static IReadOnlyList<string> AutoBackups() =>
        Directory.Exists(BackupsDir)
            ? Directory.EnumerateFiles(BackupsDir, AutoPrefix + "*." + Extension)
                .OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal).ToList()
            : Array.Empty<string>();

    /// <summary>On the first launch of a new version, back everything up. Never throws.</summary>
    public static string? AutoBackupIfVersionChanged(string? version = null)
    {
        try
        {
            version ??= AppVersion;
            var last = AppSettings.Get(LastVersionKey);
            if (last == version) return null;
            string? path = null;
            var hasAnything = File.Exists(Path.Combine(AppDataDir(), "settings.json"))
                              || File.Exists(Path.Combine(OtdDataDir(), "settings.json"));
            if (hasAnything)
                path = MakeBackup($"v{Regex.Replace(last ?? "old", "[^A-Za-z0-9.-]", "_")}-to-v{Regex.Replace(version, "[^A-Za-z0-9.-]", "_")}");
            AppSettings.Set(LastVersionKey, version);
            if (path != null) AppLog.Info($"Settings backup made before first run of version {version}: {path}");
            return path;
        }
        catch (Exception ex)
        {
            AppLog.Warn("Automatic settings backup failed.", ex);
            return null;
        }
    }
}
