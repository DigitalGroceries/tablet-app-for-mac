using OpenTabletDriver.Desktop;

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
                if (profile != null) Domain.PenScrollBinding.SyncFilter(profile);
        if (ownedDaemon) ProfileFilterMaintenance.DisableUnapprovedFilters(workingCopy);
    }
}
