using System;
using System.Collections.Generic;
using System.Linq;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;

namespace OpenTabletArtist.Domain;

/// <summary>
/// App side of pen zoom / pan+zoom (hold a pen button or ExpressKey and move the pen). Plugin types
/// <c>OpenTabletArtist.Dynamics.PenZoomBinding</c> and <c>PenZoomFilter</c>. Zoom uses the same
/// Option+scroll path as touch-ring Smooth zoom. Pan+Zoom pans with horizontal movement and zooms
/// with vertical movement.
/// </summary>
public static class PenZoomBinding
{
    public const string BindingTypeName = "OpenTabletArtist.Dynamics.PenZoomBinding";
    public const string FilterTypeName = "OpenTabletArtist.Dynamics.PenZoomFilter";

    /// <summary>Scroll-tab value: move the pen up/down to zoom.</summary>
    public const string ZoomValue = "Zoom";

    /// <summary>Scroll-tab value: move the pen to pan (horizontal) and zoom (vertical).</summary>
    public const string PanZoomValue = "PanZoom";

    public const string ModeZoom = "Zoom";
    public const string ModePanZoom = "PanZoom";

    public static bool Is(PluginSettingStore? store) => store?.Path == BindingTypeName;

    public static bool IsZoom(PluginSettingStore? store) =>
        Is(store) && ReadMode(store) == ModeZoom;

    public static bool IsPanZoom(PluginSettingStore? store) =>
        Is(store) && ReadMode(store) == ModePanZoom;

    public static PluginSettingStore MakeZoom(int speed = PenScrollMath.DefaultSpeed, bool invert = false) =>
        Make(ModeZoom, speed, invert);

    public static PluginSettingStore MakePanZoom(int speed = PenScrollMath.DefaultSpeed, bool invert = false) =>
        Make(ModePanZoom, speed, invert);

    public static PluginSettingStore Make(string mode, int speed = PenScrollMath.DefaultSpeed, bool invert = false)
    {
        var store = AuxKeyBinding.NewStore(BindingTypeName);
        store.Settings.Add(new PluginSetting("Mode", mode == ModePanZoom ? ModePanZoom : ModeZoom));
        store.Settings.Add(new PluginSetting("Speed", Math.Clamp(speed, PenScrollMath.MinSpeed, PenScrollMath.MaxSpeed)));
        store.Settings.Add(new PluginSetting("Invert", invert));
        return store;
    }

    public static string ReadMode(PluginSettingStore? store)
    {
        var m = store?.Settings?.FirstOrDefault(s => s.Property == "Mode")?.GetValue<string>();
        return m == ModePanZoom ? ModePanZoom : ModeZoom;
    }

    public static int ReadSpeed(PluginSettingStore? store) =>
        store?.Settings?.FirstOrDefault(s => s.Property == "Speed") is { HasValue: true } s
            ? Math.Clamp(s.GetValue<int>(), PenScrollMath.MinSpeed, PenScrollMath.MaxSpeed)
            : PenScrollMath.DefaultSpeed;

    public static bool ReadInvert(PluginSettingStore? store) =>
        store?.Settings?.FirstOrDefault(s => s.Property == "Invert") is { HasValue: true } s
        && s.GetValue<bool>();

    private static readonly IEnumerable<PluginSettingStore?> None = Array.Empty<PluginSettingStore?>();

    public static IEnumerable<PluginSettingStore?> AllBindings(Profile profile) =>
        PenScrollBinding.AllBindings(profile);

    public static bool IsUsed(Profile profile) => AllBindings(profile).Any(Is);

    /// <summary>Pen zoom filter is in the profile (enabled, once) exactly while a pen zoom binding is.</summary>
    public static bool SyncFilter(Profile profile)
    {
        if (profile.Filters == null) return false;
        var existing = profile.Filters.OfType<PluginSettingStore>().Where(f => f.Path == FilterTypeName).ToList();
        bool changed = false;
        if (IsUsed(profile))
        {
            if (existing.Count == 0)
            {
                var filter = AuxKeyBinding.NewStore(FilterTypeName);
                filter.Enable = true;
                profile.Filters.Add(filter);
                return true;
            }
            foreach (var f in existing.Where(f => !f.Enable)) { f.Enable = true; changed = true; }
            foreach (var extra in existing.Skip(1)) { profile.Filters.Remove(extra); changed = true; }
        }
        else
        {
            foreach (var f in existing) { profile.Filters.Remove(f); changed = true; }
        }
        return changed;
    }
}
