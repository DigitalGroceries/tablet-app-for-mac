using System;
using System.Collections.Generic;
using System.Linq;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;

namespace OpenTabletArtist.Domain;

/// <summary>
/// App side of pen scroll (hold a pen button or ExpressKey and move the pen to scroll; plugin types
/// <c>OpenTabletArtist.Dynamics.PenScrollBinding</c> and <c>PenScrollFilter</c>). Builds and reads the
/// binding store by path, as the app doesn't reference the plugin, and keeps the filter in the profile
/// exactly while a pen scroll binding exists.
/// </summary>
public static class PenScrollBinding
{
    public const string BindingTypeName = "OpenTabletArtist.Dynamics.PenScrollBinding";
    public const string FilterTypeName = "OpenTabletArtist.Dynamics.PenScrollFilter";

    /// <summary>The value the Scroll tab uses for "move the pen to scroll".</summary>
    public const string ScrollValue = "Pen";

    public static bool Is(PluginSettingStore? store) => store?.Path == BindingTypeName;

    /// <summary>A pen scroll binding with the given settings (defaults: both directions, Speed 50, page
    /// follows the pen).</summary>
    public static PluginSettingStore Make(int speed = PenScrollMath.DefaultSpeed, PenScrollAxes axes = PenScrollAxes.Both,
        bool invert = false)
    {
        var store = AuxKeyBinding.NewStore(BindingTypeName);
        store.Settings.Add(new PluginSetting("Speed", Math.Clamp(speed, PenScrollMath.MinSpeed, PenScrollMath.MaxSpeed)));
        store.Settings.Add(new PluginSetting("Direction", axes.ToString()));
        store.Settings.Add(new PluginSetting("Invert", invert));
        return store;
    }

    public static int ReadSpeed(PluginSettingStore? store) =>
        store?.Settings?.FirstOrDefault(s => s.Property == "Speed") is { HasValue: true } s
            ? Math.Clamp(s.GetValue<int>(), PenScrollMath.MinSpeed, PenScrollMath.MaxSpeed)
            : PenScrollMath.DefaultSpeed;

    private static readonly IEnumerable<PluginSettingStore?> None = Array.Empty<PluginSettingStore?>();

    /// <summary>Every binding store of a profile, wherever it sits.</summary>
    public static IEnumerable<PluginSettingStore?> AllBindings(Profile profile)
    {
        var b = profile.BindingSettings;
        if (b == null) yield break;
        yield return b.TipButton;
        yield return b.EraserButton;
        yield return b.MouseScrollUp;
        yield return b.MouseScrollDown;
        foreach (var s in b.PenButtons ?? None) yield return s;
        foreach (var s in b.AuxButtons ?? None) yield return s;
        foreach (var s in b.MouseButtons ?? None) yield return s;
        foreach (var w in b.WheelBindings ?? Enumerable.Empty<WheelBindingSettings>())
        {
            if (w == null) continue;
            yield return w.ClockwiseRotation;
            yield return w.CounterClockwiseRotation;
            foreach (var s in w.WheelButtons ?? None) yield return s;
        }
    }

    public static bool IsUsed(Profile profile) => AllBindings(profile).Any(Is);

    /// <summary>The pen scroll filter is in the profile (enabled, once) exactly while a pen scroll binding
    /// is; otherwise our filter store is removed. Never touches other filters. Returns true if it changed
    /// anything.</summary>
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
