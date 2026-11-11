using System;
using System.IO;
using OpenTabletDriver.Desktop;
using OtdInterop;

namespace OpenTabletArtist.Services;

/// <summary>
/// The artist's own default settings baseline (#22): a full settings document, same scope as Save,
/// kept under the app's data folder so it survives restarts. Distinct from the driver's saved file
/// ("Revert to saved") and from factory defaults ("Restore factory settings").
/// </summary>
public static class UserDefaultSettings
{
    public static string FilePath => Path.Combine(AppPaths.LocalAppData, "user-default-settings.json");

    public static bool Exists => File.Exists(FilePath) || File.Exists(FilePath + AtomicFile.BackupSuffix);

    public static void Save(Settings settings, IPresetStore store)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        store.Save(settings, FilePath);
    }

    public static bool TryLoad(IPresetStore store, out Settings? settings) =>
        store.TryLoad(FilePath, out settings);

    /// <summary>True when live settings match the stored user default (byte-for-byte via the shared codec).</summary>
    public static bool Matches(Settings? live, IPresetStore store) =>
        live is not null && TryLoad(store, out var saved) && saved is not null && SettingsCodec.Same(live, saved);

    /// <summary>#38: the app's own switches and lists kept with the default (see <see cref="TabletAppState"/>).</summary>
    public static string AppStatePath => Path.Combine(AppPaths.LocalAppData, "user-default-app-state.json");

    public static void SaveAppState(System.Collections.Generic.IReadOnlyDictionary<string, string> state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(AppStatePath)!);
        AtomicFile.WriteText(AppStatePath, Newtonsoft.Json.JsonConvert.SerializeObject(state, Newtonsoft.Json.Formatting.Indented));
    }

    /// <summary>False when no copy was kept (a default saved before #38): then the app's switches are left as they are.</summary>
    public static bool TryLoadAppState(out System.Collections.Generic.Dictionary<string, string> state)
    {
        state = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            if (!File.Exists(AppStatePath)) return false;
            var read = Newtonsoft.Json.JsonConvert.DeserializeObject<System.Collections.Generic.Dictionary<string, string>>(File.ReadAllText(AppStatePath));
            if (read is null) return false;
            foreach (var (k, v) in read) state[k] = v;
            return true;
        }
        catch (Exception) { return false; }
    }

    public static void Delete()
    {
        try { if (File.Exists(AppStatePath)) File.Delete(AppStatePath); } catch { /* best-effort */ }
        try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { /* best-effort */ }
        try
        {
            var bak = FilePath + AtomicFile.BackupSuffix;
            if (File.Exists(bak)) File.Delete(bak);
        }
        catch { /* best-effort */ }
    }
}
