using System.Collections.Generic;
using System.IO;

namespace OpenTabletArtist.Domain;

/// <summary>
/// Locates the bundled pen-dynamics plugin DLL the app installs into the daemon's plugin
/// directory. Pure (no filesystem access) so the candidate ordering is unit-testable; the caller
/// picks the first that exists. Covers the published layout (bundled next to the app) and the dev
/// build tree (the plugin's own bin output).
/// </summary>
public static class PressurePluginPaths
{
    public const string DllName = "OpenTabletArtist.Dynamics.dll";

    /// <summary>Subfolder name used both for the bundle next to the app and inside the OTD plugin dir.</summary>
    public const string PluginFolderName = "OpenTabletArtistDynamics";

    public static IEnumerable<string> SourceCandidates(string baseDir)
    {
        // 1. Bundled next to the app — published release layout: <app>/BundledPlugins/<folder>/<dll>
        yield return Path.GetFullPath(Path.Combine(baseDir, "BundledPlugins", PluginFolderName, DllName));

        // 2. macOS .app: Contents/Resources/BundledPlugins. A loose DLL in Contents/MacOS isn't sealed
        //    by the code signature, so the bundle keeps it with the resources instead.
        yield return Path.GetFullPath(Path.Combine(baseDir, "..", "Resources", "BundledPlugins", PluginFolderName, DllName));

        // 3+. Dev build tree: <app>/bin/<cfg>/net10.0 → repo root → plugin build output.
        foreach (var tfm in new[] { "net10.0", "net8.0" })
            foreach (var config in new[] { "Debug", "Release" })
                yield return Path.GetFullPath(Path.Combine(
                    baseDir, "..", "..", "..", "..",
                    "plugins", "OpenTabletArtist.Dynamics", "bin", config, tfm, DllName));
    }
}
