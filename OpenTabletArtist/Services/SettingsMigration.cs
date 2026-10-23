using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace OpenTabletArtist.Services;

/// <summary>Build R: first launch after the rename from OpenTabletArtist. When the new data folder is
/// missing and the old OpenTabletArtist folder exists, copy the app's own settings across. The old folder is
/// never changed or deleted (an older build can still be run).</summary>
public static class SettingsMigration
{
    /// <summary>Keys copied when they start with one of these (RingModes:* etc.) or equal one exactly.</summary>
    public static readonly string[] Prefixes = { "RingModes:", "RingModesBackup:", "MapAllDisplays:" };
    public static readonly string[] ExactKeys = { "AllDisplaysForceProportions", "RingModeHud", "UiLightness" };

    /// <summary>The old start-at-login choice, stored under this key so the new app can re-register itself.</summary>
    public const string LegacyAutoStartKey = "LegacyAutoStartChoice";

    public static bool ShouldCopy(string key) =>
        ExactKeys.Contains(key) || Prefixes.Any(p => key.StartsWith(p, StringComparison.Ordinal));

    /// <summary>Build R2: first launch as "Tablet App for Mac". Copy build R's folder ("Free Tablet App for Mac")
    /// whole and unchanged when present (every file and every settings field, known or not); otherwise fall
    /// back to the filtered copy from the old OpenTabletArtist folder. Old folders are never changed or deleted.
    /// Returns the number of files (R folder) or keys (OpenTabletArtist folder) copied, or -1. Never throws.</summary>
    public static int Migrate(string rDir, string oldDir, string newDir)
    {
        try
        {
            if (Directory.Exists(newDir)) return -1;
            if (Directory.Exists(rDir)) return CopyTree(rDir, newDir);
        }
        catch (Exception) { return -1; }
        return Migrate(oldDir, newDir);
    }

    private static int CopyTree(string from, string to)
    {
        var tmp = to + ".migrating-" + Guid.NewGuid().ToString("N");
        int n = 0;
        try
        {
            foreach (var dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(tmp, Path.GetRelativePath(from, dir)));
            Directory.CreateDirectory(tmp);
            foreach (var f in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                File.Copy(f, Path.Combine(tmp, Path.GetRelativePath(from, f)));
                n++;
            }
            Directory.Move(tmp, to);   // all or nothing: a half copy never looks like a finished one
            return n;
        }
        catch (Exception)
        {
            try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch (Exception) { }
            return -1;
        }
    }

    /// <summary>Returns the number of keys copied, or -1 when nothing was done (new folder exists, old missing,
    /// or the old settings are unreadable). Never throws.</summary>
    public static int Migrate(string oldDir, string newDir)
    {
        try
        {
            if (Directory.Exists(newDir) || !Directory.Exists(oldDir)) return -1;
            var oldFile = Path.Combine(oldDir, "settings.json");
            if (!File.Exists(oldFile)) return -1;
            var old = JObject.Parse(File.ReadAllText(oldFile));
            var copy = new JObject();
            foreach (var p in old.Properties().Where(p => ShouldCopy(p.Name))) copy[p.Name] = p.Value.DeepClone();
            if (old[AutoStartCoordinator.ChoiceKey] is { } choice) copy[LegacyAutoStartKey] = choice.DeepClone();
            Directory.CreateDirectory(newDir);
            File.WriteAllText(Path.Combine(newDir, "settings.json"), copy.ToString());
            return copy.Count;
        }
        catch (Exception) { return -1; }
    }
}
