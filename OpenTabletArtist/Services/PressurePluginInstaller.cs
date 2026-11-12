using System.IO;
using System.Linq;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Services;

/// <summary>What an ensure/copy did — the caller reacts differently to each.</summary>
public enum PluginInstallOutcome
{
    /// <summary>Nothing copied (already up to date, or source missing).</summary>
    None,
    /// <summary>Copied into a directory that didn't exist — the daemon hadn't loaded it, so a
    /// <c>LoadPlugins</c> imports it.</summary>
    Installed,
    /// <summary>Overwrote a plugin the daemon already loaded at startup. <c>LoadPlugins</c> won't
    /// replace an already-loaded directory, so the daemon must restart to pick up the new DLL.</summary>
    Updated,
    /// <summary>A bundle was present but declares no support for the OTD version we ship, so it was
    /// deliberately not installed (#739). Distinct from <see cref="None"/>: there is something there,
    /// it is just the wrong one, and installing it would break the pen rather than fix it.</summary>
    Incompatible,
    /// <summary>Build 8: the copy was needed but failed (for example a read-only old copy that couldn't be
    /// replaced). Used to be reported as <see cref="None"/>, which logged "matches the bundled one" and hid the
    /// driver running an old plugin for weeks.</summary>
    Failed,
    /// <summary>No bundled plugin was found next to the app (dev tree or a broken bundle).</summary>
    SourceMissing,
}

/// <summary>
/// Ensures our bundled pressure-curve plugin is present in the daemon's plugin directory so the
/// app-owned daemon loads it. The source DLL is resolved via <see cref="PressurePluginPaths"/>
/// (bundled next to the app in a release, or the plugin's build output in dev). Everything is
/// best-effort — failures are swallowed (the curve feature simply won't be available).
/// </summary>
public class PressurePluginInstaller
{
    /// <summary>Resolve the bundled DLL and copy it into <paramref name="pluginDirectory"/> if
    /// missing/out of date, reporting whether it was a fresh install or an update.</summary>
    public PluginInstallOutcome EnsureInstalled(string pluginDirectory) => EnsureInstalled(pluginDirectory, out _);

    /// <summary>As <see cref="EnsureInstalled(string)"/>, with what went wrong when the outcome is
    /// <see cref="PluginInstallOutcome.Failed"/>.</summary>
    public PluginInstallOutcome EnsureInstalled(string pluginDirectory, out string? error)
    {
        error = null;
        var source = PressurePluginPaths.SourceCandidates(AppContext.BaseDirectory).FirstOrDefault(File.Exists);
        return source == null ? PluginInstallOutcome.SourceMissing : CopyIfNeeded(source, pluginDirectory, out error);
    }

    /// <summary>Pure copy step (testable): copy <paramref name="sourceDll"/> into the plugin
    /// directory's subfolder when missing or stale.</summary>
    public static PluginInstallOutcome CopyIfNeeded(string sourceDll, string pluginDirectory)
        => CopyIfNeeded(sourceDll, pluginDirectory, out _);

    public static PluginInstallOutcome CopyIfNeeded(string sourceDll, string pluginDirectory, out string? error)
    {
        error = null;
        if (string.IsNullOrEmpty(pluginDirectory)) return PluginInstallOutcome.None;
        if (!File.Exists(sourceDll)) return PluginInstallOutcome.SourceMissing;
        try
        {
            var destDir = Path.Combine(pluginDirectory, PressurePluginPaths.PluginFolderName);
            var dest = Path.Combine(destDir, PressurePluginPaths.DllName);
            var existed = File.Exists(dest);
            if (existed && !IsStale(sourceDll, dest)) return PluginInstallOutcome.None;
            // Build 8: replaces a read-only old copy too (Zak's was read-only since 23 Oct).
            PluginFileCopy.Replace(sourceDll, dest);
            return existed ? PluginInstallOutcome.Updated : PluginInstallOutcome.Installed;
        }
        catch (Exception e)
        {
            error = $"{e.GetType().Name}: {e.Message}";
            return PluginInstallOutcome.Failed;
        }
    }

    /// <summary>Stale = the installed copy is not byte-for-byte the bundled one.
    /// Build 7 (10 Nov 2026): test builds are packed with every file's date set to the same fixed day, so "the
    /// bundled file is newer" never held, and the driver kept loading an older plugin without the new pen-zoom
    /// types (Zak's OTD log: "Unable to find plugin matching name 'OpenTabletArtist.Dynamics.PenZoomBinding'").
    /// Compare the content instead; the file is small (≈80 KB).</summary>
    internal static bool IsStale(string source, string dest)
    {
        var s = new FileInfo(source);
        var d = new FileInfo(dest);
        if (s.Length != d.Length) return true;
        using var a = File.OpenRead(source);
        using var b = File.OpenRead(dest);
        return !System.Security.Cryptography.SHA256.HashData(a).AsSpan()
            .SequenceEqual(System.Security.Cryptography.SHA256.HashData(b));
    }
}
