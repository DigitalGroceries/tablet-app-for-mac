namespace OpenTabletArtist.Domain;

/// <summary>
/// Formats the app's <c>AssemblyInformationalVersion</c> for display on the About section of Home — strips
/// any <c>+build</c> metadata and ensures a leading <c>v</c>. Kept pure so it's unit-tested.
/// </summary>
public static class AppVersionInfo
{
    public static string Format(string? informationalVersion)
    {
        var v = informationalVersion?.Trim();
        if (string.IsNullOrEmpty(v)) return "Version ?";

        var plus = v.IndexOf('+');      // drop +<commit>/build metadata (e.g. "0.6.0+abc1234")
        if (plus >= 0) v = v[..plus];
        v = v.Trim();
        if (v.Length == 0) return "Version ?";

        // Version 1: shown as "Version 1"; any old "v" prefix is dropped.
        if (v.StartsWith('v') || v.StartsWith('V')) v = v[1..];
        return "Version " + v;
    }
}
