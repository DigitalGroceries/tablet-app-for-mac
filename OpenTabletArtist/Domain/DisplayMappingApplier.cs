using System;
using System.Linq;
using System.Numerics;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;

namespace OpenTabletArtist.Domain;

/// <summary>
/// Writes a tablet profile's Absolute-mode area mapping to a chosen display, aspect-locked, and finds
/// which display a profile is currently mapped to. Extracted from the tablet settings dialog so the
/// same mapping is applied whether the user picks a display in the dialog or from the system tray's
/// "Switch display" menu (#187). The geometry itself lives in <see cref="AreaMappingCalculator"/>;
/// this just reads/writes OTD's <c>AbsoluteModeSettings</c>.
/// </summary>
public static class DisplayMappingApplier
{
    // Tolerance (px) for matching a stored display area back to an enumerated monitor.
    private const float MatchTolerance = 1.5f;

    /// <summary>
    /// The stored Display-area centre for <paramref name="display"/>, given the full
    /// <paramref name="displays"/> set. OTD's Display area is in <em>0-based virtual-desktop</em>
    /// coordinates: the desktop's min (top-left-most) corner is the origin, so each monitor's position
    /// is its raw virtual-desktop position shifted by <c>-min</c>. Writing the raw monitor centre
    /// (<c>monitor.X + width/2</c>) only matched when the desktop origin was already (0,0) — i.e. the
    /// top-left-most monitor sat at 0. With a monitor at a negative offset (e.g. the primary not being
    /// top-left, which duplicating displays can produce) the raw centre landed the mapping off to the
    /// left / onto the wrong display. Shifting by <c>-min</c> places it correctly, matching OTD's own
    /// area diagram (which draws the desktop from its min corner). (#displaydup)
    /// </summary>
    public static (float X, float Y) MappedCenter(DisplayInfo display, IReadOnlyList<DisplayInfo> displays)
    {
        float minX = displays is { Count: > 0 } ? displays.Min(d => d.X) : display.X;
        float minY = displays is { Count: > 0 } ? displays.Min(d => d.Y) : display.Y;
        return (
            display.X - minX + display.Width / 2f,
            display.Y - minY + display.Height / 2f);
    }

    /// <summary>
    /// The screen position of OTD's virtual-desktop origin: the top-left-most monitor corner. Everything OTD
    /// stores or computes in its Absolute-mode space — the Display area, and
    /// <see cref="AbsolutePositionMapper.MapToDesktop"/>'s result — is 0-based from this corner (see
    /// <see cref="MappedCenter"/>), so add it to get a true screen coordinate (what
    /// <c>Visual.PointToScreen</c> speaks). It is (0,0) unless a monitor sits left of / above the primary.
    /// </summary>
    public static Vector2 DesktopOrigin(IReadOnlyList<DisplayInfo> displays) =>
        displays is { Count: > 0 }
            ? new Vector2(displays.Min(d => d.X), displays.Min(d => d.Y))
            : Vector2.Zero;

    /// <summary>
    /// Maps <paramref name="profile"/>'s active area to <paramref name="display"/>: the display area
    /// becomes the whole monitor and the tablet area becomes the largest centered sub-area matching
    /// the monitor's aspect ratio (so the 1:1 mapping is undistorted). Uses <paramref name="digitizer"/>
    /// as the full tablet size, falling back to the profile's current tablet width/height.
    /// <paramref name="displays"/> is the full monitor set, needed to place the display area in OTD's
    /// virtual-screen coordinates (see <see cref="MappedCenter"/>).
    /// </summary>
    /// <returns>true if a full mapping was written; false if the profile has no Absolute settings or
    /// the dimensions are degenerate (in which case only the display area may have been set).</returns>
    public static bool ApplyToProfile(Profile profile, (float Width, float Height)? digitizer,
        DisplayInfo display, IReadOnlyList<DisplayInfo> displays)
    {
        var abs = profile.AbsoluteModeSettings;
        if (abs == null) return false;

        // Display area = the full selected monitor, centre-positioned the way OTD's own UX stores it.
        var (cx, cy) = MappedCenter(display, displays);
        abs.Display.Width = display.Width;
        abs.Display.Height = display.Height;
        abs.Display.X = cx;
        abs.Display.Y = cy;

        // Start from the full tablet digitizer area (fall back to the profile's stored area).
        float fullWidth = digitizer?.Width ?? abs.Tablet.Width;
        float fullHeight = digitizer?.Height ?? abs.Tablet.Height;
        if (fullWidth <= 0 || fullHeight <= 0 || display.Width <= 0 || display.Height <= 0)
            return false;

        // Honour any active-area rotation so re-applying a mapping keeps the rotated fit (#199).
        int rot = (((int)System.Math.Round(abs.Tablet.Rotation) % 360) + 360) % 360;
        var area = AreaMappingCalculator.FitForRotation(fullWidth, fullHeight, display.Width, display.Height, rot);
        abs.Tablet.Width = area.Width;
        abs.Tablet.Height = area.Height;
        abs.Tablet.X = area.X;
        abs.Tablet.Y = area.Y;
        abs.LockAspectRatio = true;
        return true;
    }

    /// <summary>
    /// The whole extended desktop as one area: the bounding box of every connected monitor, in OTD's
    /// 0-based virtual-desktop coordinates (centre-positioned, like <see cref="MappedCenter"/>). This is
    /// what Wacom's Mapping tab calls "Screen area: Full" and OpenTabletDriver's own UX produces with
    /// Display > Resize > Full area. Null when there are no displays.
    /// </summary>
    public static (float Width, float Height, float X, float Y)? AllDisplaysArea(IReadOnlyList<DisplayInfo> displays)
    {
        if (displays is not { Count: > 0 }) return null;
        float minX = displays.Min(d => d.X), minY = displays.Min(d => d.Y);
        float maxX = displays.Max(d => d.X + d.Width), maxY = displays.Max(d => d.Y + d.Height);
        float w = maxX - minX, h = maxY - minY;
        return (w, h, w / 2f, h / 2f);
    }

    /// <summary>
    /// Maps <paramref name="profile"/> to the whole extended desktop (<see cref="AllDisplaysArea"/>): the
    /// display area spans every monitor (Wacom's "Full" screen area). With <paramref name="forceProportions"/>
    /// (the default) the tablet area keeps the desktop's shape; without it the whole digitizer is used and the
    /// picture stretches. Returns false if the profile has no Absolute settings or nothing to map.
    /// </summary>
    public static bool ApplyAllDisplays(Profile profile, (float Width, float Height)? digitizer,
        IReadOnlyList<DisplayInfo> displays, bool forceProportions = true)
    {
        var abs = profile.AbsoluteModeSettings;
        if (abs == null || AllDisplaysArea(displays) is not { } all) return false;

        abs.Display.Width = all.Width;
        abs.Display.Height = all.Height;
        abs.Display.X = all.X;
        abs.Display.Y = all.Y;

        // Without the tablet's real size (not detected yet) leave its area alone: the stored area is not
        // the whole digitizer, and treating it as one used a corner of the tablet and stretched the picture
        // (seen on the user's Mac, 20 Oct). The area is fitted once the tablet is connected.
        if (digitizer is not { Width: > 0, Height: > 0 } full) return true;
        float fullWidth = full.Width;
        float fullHeight = full.Height;
        if (forceProportions)
        {
            // Wacom's "Force Proportions": the largest centred tablet area with the desktop's shape, so a circle
            // drawn on the tablet stays a circle on screen (what the user's working OTD setup uses).
            int rot = (((int)System.Math.Round(abs.Tablet.Rotation) % 360) + 360) % 360;
            var area = AreaMappingCalculator.FitForRotation(fullWidth, fullHeight, all.Width, all.Height, rot);
            abs.Tablet.Width = area.Width;
            abs.Tablet.Height = area.Height;
            abs.Tablet.X = area.X;
            abs.Tablet.Y = area.Y;
            abs.LockAspectRatio = true;
            return true;
        }
        abs.Tablet.Width = fullWidth;
        abs.Tablet.Height = fullHeight;
        abs.Tablet.X = fullWidth / 2f;
        abs.Tablet.Y = fullHeight / 2f;
        abs.Tablet.Rotation = 0;
        abs.LockAspectRatio = false;
        return true;
    }

    /// <summary>
    /// True when the profile is mapped to the whole extended desktop but its tablet area is not the one
    /// <see cref="ApplyAllDisplays"/> would fit for <paramref name="digitizer"/> — e.g. All displays was chosen
    /// before the tablet was detected (the area was left as it was) or the rotation changed since. False when the
    /// tablet's size is still unknown, since there is nothing to fit to yet. (Build F, review item 1.)
    /// </summary>
    public static bool NeedsAllDisplaysFit(Profile profile, (float Width, float Height)? digitizer,
        IReadOnlyList<DisplayInfo> displays, bool forceProportions = true)
    {
        var abs = profile.AbsoluteModeSettings;
        if (abs?.Tablet == null || !IsAllDisplays(profile, displays)) return false;
        if (digitizer is not { Width: > 0, Height: > 0 } full || AllDisplaysArea(displays) is not { } all) return false;
        float w, h, x, y;
        if (forceProportions)
        {
            int rot = (((int)System.Math.Round(abs.Tablet.Rotation) % 360) + 360) % 360;
            var area = AreaMappingCalculator.FitForRotation(full.Width, full.Height, all.Width, all.Height, rot);
            (w, h, x, y) = (area.Width, area.Height, area.X, area.Y);
        }
        else
        {
            (w, h, x, y) = (full.Width, full.Height, full.Width / 2f, full.Height / 2f);
        }
        const float mm = 0.5f;   // tablet areas are in millimetres
        return System.Math.Abs(abs.Tablet.Width - w) > mm || System.Math.Abs(abs.Tablet.Height - h) > mm
            || System.Math.Abs(abs.Tablet.X - x) > mm || System.Math.Abs(abs.Tablet.Y - y) > mm;
    }

    /// <summary>True when the stored display area is the whole extended desktop (<see cref="AllDisplaysArea"/>).</summary>
    public static bool IsAllDisplays(Profile profile, IReadOnlyList<DisplayInfo> displays)
    {
        var disp = profile.AbsoluteModeSettings?.Display;
        if (disp == null || displays is not { Count: > 1 } || AllDisplaysArea(displays) is not { } all) return false;
        return Approx(disp.Width, all.Width) && Approx(disp.Height, all.Height)
            && Approx(disp.X, all.X) && Approx(disp.Y, all.Y);
    }

    /// <summary>Set the active-area <paramref name="rotationDeg"/> (0/90/180/270) and re-fit the tablet
    /// area so it still fills the currently-mapped display without distortion — for 90/270 the area
    /// shrinks to the largest that fits the tablet once rotated (#199). Returns true if a rotation was
    /// written (the area is only re-fitted when the profile maps cleanly to a single monitor).</summary>
    public static bool ApplyRotation(Profile profile, (float Width, float Height)? digitizer,
        int rotationDeg, IReadOnlyList<DisplayInfo> displays)
    {
        var abs = profile.AbsoluteModeSettings;
        if (abs?.Tablet == null) return false;

        abs.Tablet.Rotation = rotationDeg;

        float fullWidth = digitizer?.Width ?? abs.Tablet.Width;
        float fullHeight = digitizer?.Height ?? abs.Tablet.Height;
        var display = CurrentlyMapped(profile, displays);
        if (display == null || fullWidth <= 0 || fullHeight <= 0 || display.Width <= 0 || display.Height <= 0)
            return true; // rotation applied; no clean single-display mapping to re-fit against

        var area = AreaMappingCalculator.FitForRotation(fullWidth, fullHeight, display.Width, display.Height, rotationDeg);
        abs.Tablet.Width = area.Width;
        abs.Tablet.Height = area.Height;
        abs.Tablet.X = area.X;
        abs.Tablet.Y = area.Y;
        abs.LockAspectRatio = true;
        return true;
    }

    /// <summary>The display this profile is currently mapped to (a full-monitor match against the
    /// stored display area), or null if it isn't mapped to a whole monitor / has no Absolute settings.</summary>
    public static DisplayInfo? CurrentlyMapped(Profile profile, IReadOnlyList<DisplayInfo> displays)
    {
        var disp = profile.AbsoluteModeSettings?.Display;
        if (disp == null) return null;
        foreach (var d in displays)
        {
            // Match against the same centre ApplyToProfile would store for this monitor.
            var (cx, cy) = MappedCenter(d, displays);
            if (Approx(disp.Width, d.Width) && Approx(disp.Height, d.Height)
                && Approx(disp.X, cx) && Approx(disp.Y, cy))
                return d;
        }
        return null;
    }

    private static bool Approx(float a, float b) => System.Math.Abs(a - b) <= MatchTolerance;

    /// <summary>
    /// Classify a profile's stored Absolute-mode display mapping for predictability, so the UI can flag
    /// an unusual or broken mapping instead of silently rendering it. The stored Display area is the
    /// output region in 0-based virtual-desktop coordinates (X/Y is its <em>centre</em>; see
    /// <see cref="MappedCenter"/>). Coverage is measured against the union of connected monitors:
    /// <list type="bullet">
    /// <item><see cref="DisplayMappingValidity.Clean"/> — the area matches one whole monitor.</item>
    /// <item><see cref="DisplayMappingValidity.Custom"/> — fully on-screen but not a single monitor
    /// (a sub-region, or spanning several).</item>
    /// <item><see cref="DisplayMappingValidity.OffScreen"/> — part of the area falls outside every
    /// monitor, so the pen maps to space with no screen there.</item>
    /// <item><see cref="DisplayMappingValidity.None"/> — no (or degenerate) mapping / no displays.</item>
    /// </list>
    /// </summary>
    public static DisplayMappingValidity ClassifyMapping(Profile profile, IReadOnlyList<DisplayInfo> displays) =>
        // The whole extended desktop is a deliberate, standard choice (Wacom's "Full"), not a broken mapping:
        // the gaps between differently-sized monitors are simply where no screen is.
        IsAllDisplays(profile, displays) ? DisplayMappingValidity.Clean :
        (DisplayMappingValidity)OtdHealth.Collector.ProfileInspector.ClassifyMapping(profile,
            displays.Select(d => new OtdHealth.Collector.DisplayBounds(d.X, d.Y, d.Width, d.Height)).ToArray());



}

/// <summary>The stored display-output rectangle in 0-based virtual-desktop coordinates (top-left origin,
/// the same space the display diagram lays monitors out in). Lets the mapping diagram overlay where the
/// tablet actually maps — and, for an off-screen/custom mapping, how it sits relative to the monitors.</summary>
public readonly record struct MappedOutputArea(double Left, double Top, double Width, double Height);


/// <summary>How a tablet's stored Absolute-mode display mapping relates to the connected monitors
/// (see <see cref="DisplayMappingApplier.ClassifyMapping"/>).</summary>
public enum DisplayMappingValidity
{
    /// <summary>No mapping to assess (no Absolute settings, a degenerate area, or no displays).</summary>
    None,
    /// <summary>Maps to exactly one whole monitor — the standard case.</summary>
    Clean,
    /// <summary>Fully on-screen but not a single monitor (a sub-region or spanning several).</summary>
    Custom,
    /// <summary>Part of the mapped area falls outside every monitor — the pen hits off-screen dead zones.</summary>
    OffScreen,
}
