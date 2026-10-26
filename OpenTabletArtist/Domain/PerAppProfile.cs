using OpenTabletDriver.Desktop.Profiles;

namespace OpenTabletArtist.Domain;

/// <summary>One Application-row profile: bindings for a macOS app identified by bundle id.</summary>
public sealed class PerAppProfile
{
    /// <summary>macOS bundle identifier, e.g. <c>com.adobe.Photoshop</c>.</summary>
    public string BundleId { get; set; } = "";

    /// <summary>Display name from the .app (or the last path segment).</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>Absolute path to the .app when known; empty when only the bundle id was recorded.</summary>
    public string AppPath { get; set; } = "";

    /// <summary>ExpressKeys / Touch Ring / pen tip+buttons / eraser bindings for this app.
    /// Mapping and pressure curves stay on All (v1).</summary>
    public BindingSettings Bindings { get; set; } = new();
}

/// <summary>Running or browsed macOS app offered in the Add Application dialog.</summary>
public sealed record MacAppCandidate(string BundleId, string DisplayName, string AppPath);
