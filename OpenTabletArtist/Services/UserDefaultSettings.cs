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

    public static void Delete()
    {
        try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { /* best-effort */ }
        try
        {
            var bak = FilePath + AtomicFile.BackupSuffix;
            if (File.Exists(bak)) File.Delete(bak);
        }
        catch { /* best-effort */ }
    }
}
