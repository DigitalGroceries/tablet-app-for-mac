namespace OpenTabletArtist.Domain;

/// <summary>
/// Formats the app's <c>AssemblyInformationalVersion</c> for display on the About section of Home — strips
/// any <c>+build</c> metadata. Kept pure so it's unit-tested. UI wrappers use <see cref="Number"/> with
/// <c>home.version</c> so "Version" can be translated.
/// </summary>
public static class AppVersionInfo
{
    /// <summary>Bare version token (e.g. "9"), or "?" when missing.</summary>
    public static string Number(string? informationalVersion)
    {
        var v = informationalVersion?.Trim();
        if (string.IsNullOrEmpty(v)) return "?";

        var plus = v.IndexOf('+');      // drop +<commit>/build metadata (e.g. "0.6.0+abc1234")
        if (plus >= 0) v = v[..plus];
        v = v.Trim();
        if (v.Length == 0) return "?";

        // Version 1: any old "v" prefix is dropped.
        if (v.StartsWith('v') || v.StartsWith('V')) v = v[1..];
        // Version 14: a BETA build ("14-beta2") reads "14 BETA 2", like the website and GitHub. From Version 23 a
        // BETA has no number (the owner, 4 Nov 2026): "23-beta" reads "23 BETA".
        var m = System.Text.RegularExpressions.Regex.Match(v, @"^(\d+)-beta(\d*)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (m.Success) v = m.Groups[2].Value.Length > 0 ? $"{m.Groups[1].Value} BETA {m.Groups[2].Value}" : $"{m.Groups[1].Value} BETA";
        // Test builds (Zak, 4 and 12 Nov 2026: a version name is one number, or one number and one word; a test build is
        // the number followed by "test", never a build number): "26-test" reads "26 test", so Home says "Version 26 test".
        // An old numbered tag ("26-test8") reads the same.
        var t = System.Text.RegularExpressions.Regex.Match(v, @"^(\d+)-test\d*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (t.Success) v = $"{t.Groups[1].Value} test";
        return v;
    }

    /// <summary>English "Version N" for places that still use a fixed label (tests, Advanced).</summary>
    public static string Format(string? informationalVersion) => "Version " + Number(informationalVersion);
}
