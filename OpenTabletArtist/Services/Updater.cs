using System;
using System.Collections.Generic;
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
    /// <summary>Version 14: all recent releases (stable and BETA pre-releases), so the user can choose.</summary>
    public const string ListApi = "https://api.github.com/repos/DigitalGroceries/tablet-app-for-mac/releases?per_page=30";
    /// <summary>Version 7 test 3: one app per Mac type (no universal launcher), so the updater picks its own zip.</summary>
    public static string ZipName => ZipNameFor(System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture);
    public static string ZipNameFor(System.Runtime.InteropServices.Architecture a)
        => a == System.Runtime.InteropServices.Architecture.X64 ? "Tablet-App-for-Mac-Intel.zip" : "Tablet-App-for-Mac.zip";
    public const string SumsName = "SHA256SUMS";
    public static readonly TimeSpan Every = TimeSpan.FromDays(7);

    /// <summary>A release. <paramref name="Beta"/> 0 = stable; 1, 2, … = BETA number (a GitHub pre-release).</summary>
    public sealed record Release(int Version, string ZipUrl, string SumsUrl, int Beta = 0)
    {
        public bool IsBeta => Beta > 0;
        public int Rank => RankOf(Version, Beta);
        /// <summary>"14" or "14 BETA 2" (used after "Version ").</summary>
        public string Label => IsBeta ? $"{Version} BETA {Beta}" : Version.ToString();
    }

    /// <summary>Version 14: what the user can choose from. <see cref="Stable"/> is a newer stable version, or —
    /// when <see cref="StableIsGoBack"/> — the stable version a BETA user can go back to. <see cref="Beta"/> is a
    /// BETA newer than both the running app and the newest stable version.
    /// Version 15 (Zak, 27 Oct 2026: "All versions stable or beta should allow the users to downgrade to an
    /// earlier version"): <see cref="Earlier"/> holds the stable versions older than the running app, newest
    /// first, at most <see cref="EarlierCount"/> (the same two the website links to). Not counted in
    /// <see cref="Any"/>: an earlier version is never announced as an update.</summary>
    public sealed record UpdateChoices(Release? Stable, Release? Beta, bool StableIsGoBack, IReadOnlyList<Release>? Earlier = null)
    {
        public static readonly UpdateChoices None = new(null, null, false);
        public bool Any => Stable != null || Beta != null;
        public Release? StableUpgrade => StableIsGoBack ? null : Stable;
        public Release? GoBack => StableIsGoBack ? Stable : null;
        public IReadOnlyList<Release> EarlierVersions => Earlier ?? Array.Empty<Release>();
        public bool HasEarlier => EarlierVersions.Count > 0;
    }

    /// <summary>Version 15: how many earlier stable versions the app offers to go back to.</summary>
    public const int EarlierCount = 2;

    /// <summary>Order of versions: 14 BETA 1 &lt; 14 BETA 2 &lt; 14 (stable) &lt; 15 BETA 1. Pure, unit-tested.</summary>
    public static int RankOf(int version, int beta) => version * 1000 + (beta > 0 ? Math.Min(beta, 998) : 999);

    /// <summary>"v14-beta2" / "14-beta2+abc" → 2; "v14-beta" → 1; "v14", "14+abc" → 0. Pure, unit-tested.</summary>
    public static int ParseBeta(string? tag)
    {
        var m = System.Text.RegularExpressions.Regex.Match(tag ?? "", @"(?i)-beta(\d*)");
        if (!m.Success) return 0;
        return m.Groups[1].Value.Length == 0 ? 1 : (int.TryParse(m.Groups[1].Value, out var n) && n > 0 ? n : 1);
    }

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

    static string? InformationalVersion => System.Reflection.Assembly.GetExecutingAssembly()
        .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion;

    public static int Current => ParseVersion(InformationalVersion);

    /// <summary>Version 14: 0 for a stable build; the BETA number for a BETA build (built with
    /// InformationalVersion "14-beta2").</summary>
    public static int CurrentBeta => ParseBeta(InformationalVersion);

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

    /// <summary>Version 14: reads a GitHub release list (stable and pre-releases; drafts and releases without this
    /// Mac's zip are skipped). A pre-release is a BETA; its number comes from the tag ("v14-beta2"), else 1.
    /// Pure, unit-tested.</summary>
    public static System.Collections.Generic.List<Release> ParseAll(string json)
    {
        var list = new System.Collections.Generic.List<Release>();
        using var d = JsonDocument.Parse(json);
        if (d.RootElement.ValueKind != JsonValueKind.Array) return list;
        foreach (var r in d.RootElement.EnumerateArray())
        {
            if (r.TryGetProperty("draft", out var dr) && dr.ValueKind == JsonValueKind.True) continue;
            if (!r.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) continue;
            string? Url(string name) => assets.EnumerateArray()
                .Where(a => a.TryGetProperty("name", out var n) && n.GetString() == name)
                .Select(a => a.GetProperty("browser_download_url").GetString()).FirstOrDefault();
            var zip = Url(ZipName); var sums = Url(SumsName);
            var tag = r.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            var version = ParseVersion(tag);
            if (zip == null || sums == null || version == 0) continue;
            var pre = r.TryGetProperty("prerelease", out var p) && p.ValueKind == JsonValueKind.True;
            list.Add(new Release(version, zip, sums, pre ? Math.Max(1, ParseBeta(tag)) : 0));
        }
        return list;
    }

    /// <summary>Version 14: the stable / BETA choice for an app at <paramref name="version"/> (BETA
    /// <paramref name="beta"/>, 0 = stable). Pure, unit-tested.</summary>
    public static UpdateChoices Choose(System.Collections.Generic.IEnumerable<Release> all, int version, int beta)
    {
        var cur = RankOf(version, beta);
        var releases = all.ToList();
        var stable = releases.Where(r => !r.IsBeta).OrderByDescending(r => r.Rank).FirstOrDefault();
        var newestBeta = releases.Where(r => r.IsBeta).OrderByDescending(r => r.Rank).FirstOrDefault();
        Release? s = null; var goBack = false;
        if (stable != null && stable.Rank > cur) s = stable;
        else if (stable != null && beta > 0 && stable.Rank < cur) { s = stable; goBack = true; }
        var b = newestBeta != null && newestBeta.Rank > cur && (stable == null || newestBeta.Rank > stable.Rank) ? newestBeta : null;
        // Version 15: the earlier stable versions anyone (stable or BETA) can go back to. BETAs are never offered
        // here: going back is for something tested.
        var earlier = releases.Where(r => !r.IsBeta && r.Rank < cur)
            .GroupBy(r => r.Rank).Select(g => g.First())
            .OrderByDescending(r => r.Rank).Take(EarlierCount).ToList();
        return new UpdateChoices(s, b, goBack, earlier);
    }

    /// <summary>The hash for a file in SHA256SUMS ("hash  name" lines). Pure, unit-tested.</summary>
    public static string? HashFor(string sums, string name) => sums.Split('\n')
        .Select(l => l.Trim().Split((char[])null!, 2, StringSplitOptions.RemoveEmptyEntries))
        .Where(p => p.Length == 2 && p[1].TrimStart('*') == name).Select(p => p[0].ToLowerInvariant()).FirstOrDefault();

    static HttpClient Http() { var h = new HttpClient { Timeout = TimeSpan.FromMinutes(10) }; h.DefaultRequestHeaders.UserAgent.ParseAdd("TabletAppForMac-Updater"); return h; }

    /// <summary>The newer stable release, or null when up to date (or offline).</summary>
    public static async Task<Release?> CheckAsync() => (await CheckChoicesAsync()).StableUpgrade;

    /// <summary>Version 14: what the user can choose (newer stable, newer BETA, or back to stable), or
    /// <see cref="UpdateChoices.None"/> when up to date or offline.</summary>
    public static async Task<UpdateChoices> CheckChoicesAsync()
    {
        var s = Load(); s.LastCheck = DateTime.UtcNow; Save(s);
        try
        {
            using var h = Http();
            var all = ParseAll(await h.GetStringAsync(ListApi));
            var c = Choose(all, Current, CurrentBeta);
            AppLog.Info($"Update check: running {(CurrentBeta > 0 ? $"{Current} BETA {CurrentBeta}" : Current.ToString())}; " +
                        $"stable {(c.StableUpgrade?.Label ?? "-")}, BETA {(c.Beta?.Label ?? "-")}, back to stable {(c.GoBack?.Label ?? "-")}.");
            return c;
        }
        catch (Exception ex) { AppLog.Warn("Update check failed.", ex); return UpdateChoices.None; }
    }

    public sealed record Prepared(int Version, string CurrentApp, string FreshApp, string StagingDir, string Label = "");

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
        var dir = Path.Combine(Path.GetTempPath(), "tablet-app-update-" + r.Label.Replace(' ', '-'));
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
            _prepared = new Prepared(r.Version, app, fresh, dir, r.Label);
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
            // Version 15: also stop this app's own driver once the app has quit ($3 = its process ids, each
            // re-checked by name first), so the installed version — newer or earlier, which may not have
            // StaleDriverGuard — starts with its own driver instead of finding this one still running.
            var driverPids = string.Join(' ', StaleDriverGuard.DriverPidsOf(p.CurrentApp));
            var psi = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(SwapScript(Environment.ProcessId));
            psi.ArgumentList.Add(p.CurrentApp); psi.ArgumentList.Add(p.FreshApp); psi.ArgumentList.Add(p.StagingDir);
            psi.ArgumentList.Add(driverPids);
            AppLog.Info($"Update: installing Version {p.Label}; the driver to stop after quitting: {(driverPids.Length > 0 ? driverPids : "none")}.");
            Process.Start(psi);
            (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown();
            return null;
        }
        catch (Exception ex) { AppLog.Warn("Update apply failed.", ex); return "The update couldn't be installed. Try again later."; }
    }

    /// <summary>The swap script run by /bin/sh -c after the app with process id <paramref name="appPid"/> quits.
    /// $0 = the installed app, $1 = the new app, $2 = the staging folder, $3 = driver process ids to stop.
    /// Pure (text only), unit-tested and run in a test against a fake app.</summary>
    public static string SwapScript(int appPid) =>
        $"while kill -0 {appPid} 2>/dev/null; do sleep 0.5; done; " +
        "for d in $3; do case \"$(ps -p \"$d\" -o command= 2>/dev/null)\" in *OpenTabletDriver*) kill \"$d\" 2>/dev/null;; esac; done; " +
        "[ -n \"$3\" ] && sleep 1; " +
        "mv \"$0\" \"$2/old.app\" && mv \"$1\" \"$0\" && open \"$0\" || { [ -d \"$0\" ] || mv \"$2/old.app\" \"$0\"; open \"$0\"; }";

    /// <summary>Downloads, checks, then quits and installs. Returns an error to show, or null.</summary>
    public static async Task<string?> InstallAsync(Release r)
    {
        var err = await DownloadAsync(r);
        return err ?? ApplyPrepared();
    }
}
