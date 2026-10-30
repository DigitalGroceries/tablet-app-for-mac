using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OpenTabletArtist.Services;

/// <summary>
/// One-click diagnostic file (idea from MockTab by Cyzor, GPL-3; our own code): a zip with a summary,
/// the app and OpenTabletDriver logs, a settings snapshot and the devices seen. Tablet serial numbers
/// are removed and the home folder is written as "~", so it can be attached to a GitHub issue.
/// </summary>
public static class DiagnosticBundle
{
    public static string SuggestedFileName(DateTime now) => $"TabletApp-diagnostics-{now:yyyy-MM-dd-HHmm}.zip";

    public static string DefaultFolder()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        return Directory.Exists(desktop) ? desktop : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    /// <summary>Writes the zip into <paramref name="folder"/> and returns its full path.</summary>
    public static string Create(string folder, IEnumerable<string> devices, IEnumerable<string> conflicts,
        string? daemonVersion, DateTime? now = null)
    {
        var stamp = now ?? DateTime.Now;
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, SuggestedFileName(stamp));
        var home = SettingsBundle.HomeDir();
        string Clean(string s) => SettingsBundle.ToPortablePath(SettingsBundle.RedactSerialText(s), home)
            .Replace(home, "~", StringComparison.Ordinal);

        using (var fs = File.Create(path))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var sb = new StringBuilder();
            sb.AppendLine("Tablet App for Mac — diagnostic summary");
            sb.AppendLine($"Created: {stamp:yyyy-MM-dd HH:mm:ss zzz}");
            sb.AppendLine($"App version: {SettingsBundle.AppVersion}");
            sb.AppendLine($"OpenTabletDriver daemon: {daemonVersion ?? "not connected"}");
            sb.AppendLine($"System: {MacVersion()} ({RuntimeInformation.OSArchitecture}), .NET {Environment.Version}");
            sb.AppendLine($"Language: {Localizer.Preference} → {Localizer.ResolvedCode}");
            sb.AppendLine();
            sb.AppendLine("Tablets:");
            var dev = devices.ToList();
            if (dev.Count == 0) sb.AppendLine("  (none connected)");
            foreach (var d in dev) sb.AppendLine("  " + d);
            sb.AppendLine();
            sb.AppendLine("Other tablet drivers found:");
            var con = conflicts.ToList();
            if (con.Count == 0) sb.AppendLine("  (none)");
            foreach (var c in con) sb.AppendLine("  " + c);
            sb.AppendLine();
            sb.AppendLine("Serial numbers and your user name have been removed from this file.");
            Add(zip, "summary.txt", Clean(sb.ToString()));

            AddFile(zip, "logs/app.log", AppLog.LogFilePath, Clean);
            AddFile(zip, "logs/app.log.1", AppLog.LogFilePath + ".1", Clean);
            var otdLogs = Path.Combine(SettingsBundle.OtdDataDir(), "Logs");
            if (Directory.Exists(otdLogs))
                foreach (var f in Directory.EnumerateFiles(otdLogs).OrderByDescending(File.GetLastWriteTimeUtc).Take(5))
                    AddFile(zip, "logs/otd/" + Path.GetFileName(f), f, Clean);

            // Settings snapshot: the same portable export (serials removed, home as "~").
            foreach (var (name, text) in SettingsBundle.Collect(portable: true))
                Add(zip, "settings/" + name, Clean(text));
        }
        return path;
    }

    const long MaxLogBytes = 5_000_000;

    static void AddFile(ZipArchive zip, string name, string path, Func<string, string> clean)
    {
        try
        {
            if (!File.Exists(path)) return;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (fs.Length > MaxLogBytes) fs.Seek(-MaxLogBytes, SeekOrigin.End);   // keep the newest part
            using var r = new StreamReader(fs);
            Add(zip, name, clean(r.ReadToEnd()));
        }
        catch (Exception ex) { AppLog.Debug($"Diagnostics: couldn't add {name}", ex); }
    }

    static void Add(ZipArchive zip, string name, string text)
    {
        var e = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
        w.Write(text);
    }

    static string MacVersion()
    {
        if (!OperatingSystem.IsMacOS()) return RuntimeInformation.OSDescription;
        try
        {
            var psi = new ProcessStartInfo("/usr/bin/sw_vers", "-productVersion") { RedirectStandardOutput = true, UseShellExecute = false };
            using var p = Process.Start(psi)!;
            var v = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit(2000);
            return "macOS " + v;
        }
        catch { return RuntimeInformation.OSDescription; }
    }
}
