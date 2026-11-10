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
        // Build 8 (Zak, 10 Nov 2026: label the test version so he knows what he is testing): "26-test8" reads
        // "26 test build 8"; the Home line says "Test version (build 8)" instead of "Version 26" (see TestBuild).
        var t = System.Text.RegularExpressions.Regex.Match(v, @"^(\d+)-test(\d*)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (t.Success) v = t.Groups[2].Value.Length > 0 ? $"{t.Groups[1].Value} test build {t.Groups[2].Value}" : $"{t.Groups[1].Value} test build";
        return v;
    }

    /// <summary>The test build number of an unpublished test build ("26-test8" → "8"); null for any other version.
    /// Test builds are never published: they are what Zak tests before a version goes out.</summary>
    public static string? TestBuild(string? informationalVersion)
    {
        var v = informationalVersion?.Trim() ?? "";
        var plus = v.IndexOf('+');
        if (plus >= 0) v = v[..plus];
        var t = System.Text.RegularExpressions.Regex.Match(v.TrimStart('v', 'V'), @"^\d+-test(\d*)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return t.Success ? t.Groups[1].Value : null;
    }

    /// <summary>English "Version N" for places that still use a fixed label (tests, Advanced).</summary>
    public static string Format(string? informationalVersion)
        => TestBuild(informationalVersion) is { } b
            ? (b.Length > 0 ? $"Test version (build {b})" : "Test version")
            : "Version " + Number(informationalVersion);
}
