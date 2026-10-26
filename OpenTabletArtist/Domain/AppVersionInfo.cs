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
        return v;
    }

    /// <summary>English "Version N" for places that still use a fixed label (tests, Advanced).</summary>
    public static string Format(string? informationalVersion)
        => "Version " + Number(informationalVersion);
}
