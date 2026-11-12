using System.IO;

namespace OpenTabletArtist.Services;

/// <summary>
/// Copies a plugin file into the driver's plugin folder so that the copy can always be replaced later.
///
/// Build 8 (Zak, 12 Nov 2026): his installed OpenTabletArtist.Dynamics.dll was from 23 October and read-only
/// (r--r--r--, Finder "You can only read"). Every update since then failed inside File.Copy, the failure was
/// swallowed, and the app logged "the installed plugin matches the bundled one" while the driver kept loading the
/// old plugin ("Unable to find plugin matching name 'OpenTabletArtist.Dynamics.RadialMenuBinding'"). So: make an
/// existing copy (and its folder) writable before replacing it, and leave every new copy writable by its owner.
/// </summary>
public static class PluginFileCopy
{
    private const UnixFileMode FileMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    /// <summary>Copies <paramref name="source"/> over <paramref name="dest"/>, even when the existing
    /// <paramref name="dest"/> is read-only. Throws when it still can't (the caller reports it).</summary>
    public static void Replace(string source, string dest)
    {
        var dir = Path.GetDirectoryName(dest)!;
        Directory.CreateDirectory(dir);
        if (!OperatingSystem.IsWindows())
        {
            // The folder must let us write, or neither overwrite nor delete can work.
            TryAddMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, isDir: true);
            if (File.Exists(dest)) TryAddMode(dest, UnixFileMode.UserWrite, isDir: false);
        }
        else if (File.Exists(dest))
        {
            var attrs = File.GetAttributes(dest);
            if ((attrs & FileAttributes.ReadOnly) != 0) File.SetAttributes(dest, attrs & ~FileAttributes.ReadOnly);
        }

        try
        {
            File.Copy(source, dest, overwrite: true);
        }
        catch (UnauthorizedAccessException) when (File.Exists(dest))
        {
            // Last resort: a file we may not write but may remove (the folder is ours).
            File.Delete(dest);
            File.Copy(source, dest, overwrite: false);
        }

        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(dest, FileMode);
    }

    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private static void TryAddMode(string path, UnixFileMode add, bool isDir)
    {
        try
        {
            var mode = File.GetUnixFileMode(path);
            if ((mode & add) != add) File.SetUnixFileMode(path, mode | add);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Not ours to change (another owner): the copy below reports the real failure.
        }
    }
}
