using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;

namespace OpenTabletArtist.Services;

/// <summary>Artist-specific rules run before admitting a settings operation to OTD Interop.</summary>
public static class OtaSettingsPolicy
{
    public static void Prepare(Settings workingCopy, bool ownedDaemon)
    {
        ProfileFilterMaintenance.CleanLegacyFilters(workingCopy);
        // Pen scroll needs its filter to read pen movement: in the profile exactly while a pen scroll
        // binding is. Here, because every settings send (All, per-app and per-pen bindings) passes through.
        if (workingCopy.Profiles != null)
            foreach (var profile in workingCopy.Profiles)
                if (profile != null)
                {
                    Domain.PenScrollBinding.SyncFilter(profile);
                    Domain.TouchTrackpad.SyncFilter(profile);   // Discussion #31: finger touch as a trackpad
                    SyncRelativeRotation(profile);              // Issue #69: Mouse-like reads RelativeRotation
                }
        if (ownedDaemon) ProfileFilterMaintenance.DisableUnapprovedFilters(workingCopy);
    }

    /// <summary>
    /// Issue #69: Mapping writes <c>AbsoluteModeSettings.Tablet.Rotation</c> only; Mouse-like reads
    /// <c>RelativeModeSettings.RelativeRotation</c>, which was never set. Same sign as OTD CreateRotation.
    /// </summary>
    internal static void SyncRelativeRotation(Profile profile)
    {
        var tablet = profile.AbsoluteModeSettings?.Tablet;
        if (tablet == null) return;
        var rotation = (tablet.Rotation % 360f + 360f) % 360f;
        profile.RelativeModeSettings ??= RelativeModeSettings.GetDefaults();
        if (profile.RelativeModeSettings.RelativeRotation != rotation)
            profile.RelativeModeSettings.RelativeRotation = rotation;
    }
}
