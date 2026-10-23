using System;
using System.IO;
using System.Threading.Tasks;
using OpenTabletDriver.Desktop;
using OtdInterop;

namespace OpenTabletArtist.Services;

/// <summary>
/// R11: unsaved editor state kept across a system quit (logout, shutdown, restart, Cmd+Q) WITHOUT
/// touching the real settings file, because unsaved edits may be experimental. Written only when
/// something is unsaved; restored on the next launch as unsaved changes; deleted once saved or discarded.
/// </summary>
internal static class UnsavedSnapshot
{
    public const string FileName = "pending-unsaved.json";
    public const string RestoredNotice = "Restored unsaved changes from before shutdown";

    public static string DefaultDirectory => AppPaths.LocalAppData;
    public static string PathIn(string dir) => Path.Combine(dir, FileName);
    public static bool Exists(string dir) => File.Exists(PathIn(dir));

    /// <summary>Atomic write (temp file + move) so a power cut never leaves half a snapshot.</summary>
    public static void Write(string dir, Settings settings)
    {
        Directory.CreateDirectory(dir);
        var target = PathIn(dir); var tmp = target + ".tmp";
        using (var fs = File.Create(tmp)) { SettingsCodec.Encode(settings, fs); fs.Flush(true); }
        File.Move(tmp, target, overwrite: true);
    }

    public static bool TryRead(string dir, out Settings? settings)
    {
        settings = null;
        try
        {
            if (!Exists(dir)) return false;
            using var fs = File.OpenRead(PathIn(dir));
            return SettingsCodec.TryDecode(fs, out settings) && settings != null;
        }
        catch (Exception ex) { AppLog.Warn("Couldn't read the unsaved-changes snapshot.", ex); return false; }
    }

    public static void Delete(string dir)
    {
        try { File.Delete(PathIn(dir)); File.Delete(PathIn(dir) + ".tmp"); }
        catch (Exception ex) { AppLog.Warn("Couldn't remove the unsaved-changes snapshot.", ex); }
    }

    /// <summary>Applies the snapshot to the workspace live (never saved), so it shows as unsaved.
    /// An unreadable snapshot is removed. True when restored.</summary>
    public static async Task<bool> RestoreIntoAsync(SettingsWorkspace workspace, string dir)
    {
        if (!Exists(dir)) return false;
        if (!TryRead(dir, out var snapshot)) { Delete(dir); return false; }
        var outcome = await workspace.ApplyAsync(snapshot!);
        return outcome.IsLive && workspace.HasUnsavedChanges;
    }
}
