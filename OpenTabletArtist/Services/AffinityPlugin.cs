using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenTabletArtist.Services;

/// <summary>
/// #39 (Zak, 11 Nov 2026): installs the "Smooth pen strokes" plugin into Affinity's plugin folder.
/// <para>
/// macOS merges pen points while an app is busy, so fast curves in Affinity get corners even though our driver
/// sends every point (about 200 a second). The plugin (source: tools/affinity-plugin) runs inside Affinity and
/// switches that merging off, as Blender did in 2020. The user ticks "Allow unknown plugins to be used" in
/// Affinity once and runs the plugin from the Filters menu each time Affinity opens. Best try; Zak tests it.
/// </para>
/// </summary>
public sealed class AffinityPlugin
{
    public const string BundleName = "SmoothPenStrokes.plugin";
    public const string AssetRoot = "avares://OpenTabletArtist/Assets/affinity-plugin/";
    public const string LogFileName = "TabletAppForMac-Affinity.log";

    private readonly string _home;
    private readonly string _applications;
    private readonly Func<IEnumerable<(string Path, Func<Stream> Open)>> _files;

    /// <param name="home">The user's home folder.</param>
    /// <param name="applications">Where apps are installed (/Applications).</param>
    /// <param name="files">The plugin's files, as paths inside <see cref="BundleName"/> ("Contents/Info.plist").</param>
    public AffinityPlugin(string home, string applications, Func<IEnumerable<(string Path, Func<Stream> Open)>> files)
    {
        _home = home;
        _applications = applications;
        _files = files;
    }

    /// <summary>The installer for this Mac, reading the plugin from the app's own resources.</summary>
    public static AffinityPlugin ForThisMac() => new(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "/Applications", EmbeddedFiles);

    private static IEnumerable<(string Path, Func<Stream> Open)> EmbeddedFiles()
    {
        var root = new Uri(AssetRoot);
        foreach (var uri in Avalonia.Platform.AssetLoader.GetAssets(root, null))
        {
            var rel = Uri.UnescapeDataString(uri.AbsolutePath).Split(BundleName + "/", 2);
            if (rel.Length != 2) continue;
            var u = uri;
            yield return (rel[1], () => Avalonia.Platform.AssetLoader.Open(u));
        }
    }

    /// <summary>Affinity apps found in the Applications folder (Affinity 3 is "Affinity.app").</summary>
    public IReadOnlyList<string> AffinityApps() =>
        Directory.Exists(_applications)
            ? Directory.GetDirectories(_applications, "Affinity*.app").Select(Path.GetFileName).OfType<string>().OrderBy(n => n).ToList()
            : Array.Empty<string>();

    public bool AffinityInstalled => AffinityApps().Count > 0;

    private string AppSupport => Path.Combine(_home, "Library", "Application Support");

    /// <summary>The plugin folders to use: Affinity 3's ("Affinity/Plugins") always, plus Affinity Photo 2's
    /// when that version has been used on this Mac.</summary>
    public IReadOnlyList<string> PluginFolders()
    {
        var folders = new List<string> { Path.Combine(AppSupport, "Affinity", "Plugins") };
        if (Directory.Exists(Path.Combine(AppSupport, "Affinity Photo 2")))
            folders.Add(Path.Combine(AppSupport, "Affinity Photo 2", "Plugins"));
        return folders;
    }

    public bool IsInstalled => PluginFolders().Any(f => File.Exists(Path.Combine(f, BundleName, "Contents", "MacOS", "SmoothPenStrokes")));

    /// <summary>Copies the plugin into every plugin folder, replacing an older copy. Returns the folders
    /// written to; throws when none could be written.</summary>
    public IReadOnlyList<string> Install()
    {
        var files = _files().ToList();
        if (files.Count == 0) throw new InvalidOperationException("The plugin files are missing from the app.");
        var done = new List<string>();
        Exception? last = null;
        foreach (var folder in PluginFolders())
        {
            try
            {
                var bundle = Path.Combine(folder, BundleName);
                if (Directory.Exists(bundle)) Directory.Delete(bundle, recursive: true);
                foreach (var (rel, open) in files)
                {
                    var target = Path.Combine(bundle, rel.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    using (var src = open())
                    using (var dst = File.Create(target))
                        src.CopyTo(dst);
                    if (rel.StartsWith("Contents/MacOS/", StringComparison.Ordinal) && !OperatingSystem.IsWindows())
                        File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                            | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                }
                done.Add(folder);
            }
            catch (Exception ex) { last = ex; AppLog.Warn($"Affinity plugin: couldn't write to {folder}.", ex); }
        }
        if (done.Count == 0) throw last ?? new IOException("No Affinity plugin folder could be written.");
        AppLog.Info($"Affinity plugin installed in {string.Join(", ", done)}.");
        return done;
    }

    /// <summary>When the plugin last switched point merging off inside Affinity (from its log), or null.</summary>
    public DateTimeOffset? LastActive()
    {
        var log = Path.Combine(_home, "Library", "Logs", LogFileName);
        if (!File.Exists(log)) return null;
        try
        {
            var line = File.ReadLines(log).LastOrDefault(l => l.Contains("now off", StringComparison.Ordinal));
            if (line == null) return null;
            var secs = line.Split(' ', 2)[0];
            return long.TryParse(secs, out var t) ? DateTimeOffset.FromUnixTimeSeconds(t) : null;
        }
        catch (IOException) { return null; }
    }
}
