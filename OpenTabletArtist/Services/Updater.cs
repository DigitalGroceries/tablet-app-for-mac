using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace OpenTabletArtist.Services;

/// <summary>Version 7: in-app updater. Checks GitHub Releases (public API, no token) once a week at launch and on
/// request. Downloads the release zip, checks its SHA-256 against the release's SHA256SUMS, swaps the app in
/// /Applications after this app quits normally, and reopens it. Files this app downloads are not quarantined
/// (it does not set LSFileQuarantineEnabled and is not sandboxed), so Gatekeeper does not ask again; and the
/// app is ad-hoc signed (a self-signed certificate is refused by macOS at launch), so macOS may ask for the
/// permissions again after an update; the setup window handles that.</summary>
public static class Updater
{
    public const string Api = "https://api.github.com/repos/DigitalGroceries/tablet-app-for-mac/releases/latest";
    /// <summary>Version 7 test 3: one app per Mac type (no universal launcher), so the updater picks its own zip.</summary>
    public static string ZipName => ZipNameFor(System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture);
    public static string ZipNameFor(System.Runtime.InteropServices.Architecture a)
        => a == System.Runtime.InteropServices.Architecture.X64 ? "Tablet-App-for-Mac-Intel.zip" : "Tablet-App-for-Mac.zip";
    public const string SumsName = "SHA256SUMS";
    public static readonly TimeSpan Every = TimeSpan.FromDays(7);

    public sealed record Release(int Version, string ZipUrl, string SumsUrl);

    sealed class State { public DateTime LastCheck { get; set; } public bool Automatic { get; set; } }
    static string StatePath => Path.Combine(AppPaths.LocalAppData, "updates.json");
    static State Load() { try { return JsonSerializer.Deserialize<State>(File.ReadAllText(StatePath)) ?? new(); } catch { return new(); } }
    static void Save(State s) { try { Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!); File.WriteAllText(StatePath, JsonSerializer.Serialize(s)); } catch (Exception ex) { AppLog.Warn("Couldn't save update settings.", ex); } }

    /// <summary>"Update automatically": off by default.</summary>
    public static bool Automatic { get => Load().Automatic; set { var s = Load(); s.Automatic = value; Save(s); } }

    /// <summary>Weekly check due? Pure, unit-tested.</summary>
    public static bool IsDue(DateTime lastCheck, DateTime now) => now - lastCheck >= Every;
    public static bool IsDueNow() => IsDue(Load().LastCheck, DateTime.UtcNow);

    /// <summary>"v7", "V7", "7" → 7; anything else → 0. Pure, unit-tested.</summary>
    public static int ParseVersion(string? tag)
    {
        var t = (tag ?? "").Trim().TrimStart('v', 'V');
        var head = new string(t.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(head, out var n) ? n : 0;
    }

    public static int Current => ParseVersion(System.Reflection.Assembly.GetExecutingAssembly()
        .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion);

    /// <summary>Reads a GitHub "latest release" reply. Pure, unit-tested.</summary>
    public static Release? Parse(string json)
    {
        using var d = JsonDocument.Parse(json);
        var r = d.RootElement;
        if (r.TryGetProperty("draft", out var dr) && dr.GetBoolean()) return null;
        string? Url(string name) => r.GetProperty("assets").EnumerateArray()
            .Where(a => a.GetProperty("name").GetString() == name)
            .Select(a => a.GetProperty("browser_download_url").GetString()).FirstOrDefault();
        var zip = Url(ZipName); var sums = Url(SumsName);
        return zip == null || sums == null ? null : new Release(ParseVersion(r.GetProperty("tag_name").GetString()), zip, sums);
    }

    /// <summary>The hash for a file in SHA256SUMS ("hash  name" lines). Pure, unit-tested.</summary>
    public static string? HashFor(string sums, string name) => sums.Split('\n')
        .Select(l => l.Trim().Split((char[])null!, 2, StringSplitOptions.RemoveEmptyEntries))
        .Where(p => p.Length == 2 && p[1].TrimStart('*') == name).Select(p => p[0].ToLowerInvariant()).FirstOrDefault();

    static HttpClient Http() { var h = new HttpClient { Timeout = TimeSpan.FromMinutes(10) }; h.DefaultRequestHeaders.UserAgent.ParseAdd("TabletAppForMac-Updater"); return h; }

    /// <summary>The newer release, or null when up to date (or offline).</summary>
    public static async Task<Release?> CheckAsync()
    {
        var s = Load(); s.LastCheck = DateTime.UtcNow; Save(s);
        try
        {
            using var h = Http();
            var r = Parse(await h.GetStringAsync(Api));
            return r != null && r.Version > Current ? r : null;
        }
        catch (Exception ex) { AppLog.Warn("Update check failed.", ex); return null; }
    }

    public sealed record Prepared(int Version, string CurrentApp, string FreshApp, string StagingDir);

    static Prepared? _prepared;

    /// <summary>The last successful <see cref="DownloadAsync"/>, if any.</summary>
    public static Prepared? PreparedUpdate => _prepared;

    /// <summary>Downloads and checks the update zip; does not install. Returns an error to show, or null.</summary>
    public static async Task<string?> DownloadAsync(Release r)
    {
        if (!OperatingSystem.IsMacOS()) return "Updates install on macOS only.";
        var exe = Environment.ProcessPath ?? "";
        var i = exe.IndexOf(".app/", StringComparison.Ordinal);
        if (i < 0) return "Couldn't find the app to update.";
        var app = exe[..(i + 4)];
        var dir = Path.Combine(Path.GetTempPath(), "tablet-app-update-" + r.Version);
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            var zip = Path.Combine(dir, ZipName);
            using (var h = Http())
            {
                var sums = await h.GetStringAsync(r.SumsUrl);
                await File.WriteAllBytesAsync(zip, await h.GetByteArrayAsync(r.ZipUrl));
                var want = HashFor(sums, ZipName);
                await using var f = File.OpenRead(zip);
                var got = Convert.ToHexString(await SHA256.HashDataAsync(f)).ToLowerInvariant();
                if (want == null || got != want) return "The download didn't match its checksum, so it wasn't installed.";
            }
            var unpack = Process.Start("/usr/bin/ditto", new[] { "-x", "-k", zip, Path.Combine(dir, "new") })!;
            await unpack.WaitForExitAsync();
            var fresh = Directory.GetDirectories(Path.Combine(dir, "new"), "*.app").FirstOrDefault();
            if (unpack.ExitCode != 0 || fresh == null) return "Couldn't unpack the update.";
            _prepared = new Prepared(r.Version, app, fresh, dir);
            return null;
        }
        catch (Exception ex) { AppLog.Warn("Update download failed.", ex); _prepared = null; return "The update couldn't be downloaded. Try again later."; }
    }

    /// <summary>Applies a downloaded update: quits, swaps the app in place, reopens. Returns an error, or null.</summary>
    public static string? ApplyPrepared()
    {
        if (_prepared is not { } p) return "No update is ready to install.";
        try
        {
            // After this app quits normally: move the old app aside, put the new one in place, reopen it.
            var psi = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add($"while kill -0 {Environment.ProcessId} 2>/dev/null; do sleep 0.5; done; " +
                "mv \"$0\" \"$2/old.app\" && mv \"$1\" \"$0\" && open \"$0\" || { [ -d \"$0\" ] || mv \"$2/old.app\" \"$0\"; open \"$0\"; }");
            psi.ArgumentList.Add(p.CurrentApp); psi.ArgumentList.Add(p.FreshApp); psi.ArgumentList.Add(p.StagingDir);
            Process.Start(psi);
            (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown();
            return null;
        }
        catch (Exception ex) { AppLog.Warn("Update apply failed.", ex); return "The update couldn't be installed. Try again later."; }
    }

    /// <summary>Downloads, checks, then quits and installs. Returns an error to show, or null.</summary>
    public static async Task<string?> InstallAsync(Release r)
    {
        var err = await DownloadAsync(r);
        return err ?? ApplyPrepared();
    }
}
