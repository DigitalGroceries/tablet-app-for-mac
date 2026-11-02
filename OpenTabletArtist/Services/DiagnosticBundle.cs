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

    /// <summary>Adds user-picked screenshots (images only, max 10, 15 MB each) to an existing zip under screenshots/.
    /// Returns how many were added.</summary>
    public static int AddScreenshots(string zipPath, IEnumerable<string> images)
    {
        var ok = new[] { ".png", ".jpg", ".jpeg", ".heic", ".gif", ".tiff", ".webp" };
        int n = 0;
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Update);
        foreach (var img in images.Take(10))
        {
            try
            {
                if (!File.Exists(img) || !ok.Contains(Path.GetExtension(img).ToLowerInvariant())) continue;
                if (new FileInfo(img).Length > 15_000_000) continue;
                var name = "screenshots/" + (n + 1) + "-" + Path.GetFileName(img);
                zip.CreateEntryFromFile(img, name, CompressionLevel.Optimal);
                n++;
            }
            catch (Exception ex) { AppLog.Debug($"Diagnostics: couldn't add screenshot {img}", ex); }
        }
        return n;
    }

    public const string IssueRepo = "DigitalGroceries/tablet-app-for-mac";

    /// <summary>Pre-filled "new issue" page for the report (no token in the app: the user's browser posts it).</summary>
    public static string IssueUrl(string zipName, string? daemonVersion, IEnumerable<string> devices)
    {
        var body = new StringBuilder();
        body.AppendLine("Diagnostic report sent from the Tablet App.");
        body.AppendLine();
        body.AppendLine($"- App version: {SettingsBundle.AppVersion}");
        body.AppendLine($"- Driver: {daemonVersion ?? "not connected"}");
        foreach (var d in devices) body.AppendLine($"- Tablet: {SettingsBundle.RedactSerialText(d)}");
        body.AppendLine();
        body.AppendLine($"**Please drag `{zipName}` (now shown in Finder) into this box, then press Create.**");
        body.AppendLine();
        body.AppendLine("What happened (optional):");
        return $"https://github.com/{IssueRepo}/issues/new?labels=diagnostic&title="
            + Uri.EscapeDataString("Diagnostic from user — " + SettingsBundle.AppVersion)
            + "&body=" + Uri.EscapeDataString(body.ToString());
    }

    /// <summary>Writes (or replaces) one text entry in an existing zip, e.g. the user's own words as report.txt.</summary>
    public static void AddText(string zipPath, string name, string text)
    {
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Update);
        zip.GetEntry(name)?.Delete();
        Add(zip, name, text);
    }

    /// <summary>How much of the user's text goes into the issue page itself. The whole text is always in
    /// report.txt; a long text would make the "new issue" link too long for the browser.</summary>
    public const int MaxIssueText = 1500;

    /// <summary>Version 22 BETA 3 (Zak, 2 Nov): the report from the app window, the pen menu and the app menu.
    /// A pre-filled "new issue" page: the user's text, the kind (problem or recommendation) and the versions.
    /// No token in the app: the user's own browser posts it.</summary>
    public static string ReportIssueUrl(string zipName, bool recommendation, string? text, string? daemonVersion,
        IEnumerable<string> devices)
    {
        var words = (text ?? "").Trim();
        var firstLine = words.Split('\n', 2)[0].Trim();
        if (firstLine.Length > 70) firstLine = firstLine[..70].TrimEnd() + "…";
        var kind = recommendation ? "Recommendation" : "Problem";
        var title = firstLine.Length > 0 ? $"{kind}: {firstLine}" : $"{kind} from the app — {SettingsBundle.AppVersion}";

        var body = new StringBuilder();
        body.AppendLine($"**Please drag `{zipName}` (now shown in Finder) into this box, then press Create.**");
        body.AppendLine();
        body.AppendLine(recommendation ? "### Recommendation" : "### Problem");
        if (words.Length == 0) body.AppendLine("(no text)");
        else if (words.Length <= MaxIssueText) body.AppendLine(words);
        else
        {
            body.AppendLine(words[..MaxIssueText].TrimEnd() + "…");
            body.AppendLine();
            body.AppendLine("(The full text is in report.txt in the file.)");
        }
        body.AppendLine();
        body.AppendLine("### Sent from the app");
        body.AppendLine($"- App version: {SettingsBundle.AppVersion}");
        body.AppendLine($"- Driver: {daemonVersion ?? "not connected"}");
        foreach (var d in devices) body.AppendLine($"- Tablet: {SettingsBundle.RedactSerialText(d)}");
        return $"https://github.com/{IssueRepo}/issues/new?labels=" + (recommendation ? "enhancement" : "bug")
            + "&title=" + Uri.EscapeDataString(title)
            + "&body=" + Uri.EscapeDataString(body.ToString());
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
