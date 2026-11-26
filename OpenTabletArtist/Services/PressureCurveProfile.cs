using System.Collections.ObjectModel;
using System.Linq;
using Newtonsoft.Json;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Services;

/// <summary>
/// Reads/writes our pressure-curve filter on a tablet's profile (the OTD <see cref="Settings"/>
/// model). The filter is stored as a <see cref="PluginSettingStore"/> in the profile's
/// <c>Filters</c>, keyed by the plugin's type name. The app doesn't reference the plugin assembly,
/// so the store is built by path (the daemon, which has the plugin, constructs the actual filter).
/// </summary>
public static class PressureCurveProfile
{
    /// <summary>Full type name of the filter in the plugin assembly (must match the daemon's view).</summary>
    public const string FilterTypeName = "OpenTabletArtist.Dynamics.DynamicsFilter";

    /// <summary>The current dynamics (curve + smoothing) + enabled state for a tablet's profile,
    /// or null if not present.</summary>
    public static (PenDynamicsSettings Dynamics, bool Enabled)? Read(Settings? settings, string tabletName) =>
        ReadStore(FindStore(settings, tabletName));

    /// <summary>Same as <see cref="Read(Settings?,string)"/> but reads straight from a profile —
    /// handy when the caller already has the <see cref="Profile"/> (e.g. the Test page).</summary>
    public static (PenDynamicsSettings Dynamics, bool Enabled)? ReadProfile(Profile? profile) =>
        ReadStore(FindStore(profile?.Filters));

    private static (PenDynamicsSettings Dynamics, bool Enabled)? ReadStore(PluginSettingStore? store)
    {
        if (store == null) return null;

        float Get(string name, float fallback) =>
            store.Settings?.FirstOrDefault(s => s.Property == name) is { HasValue: true } s ? s.GetValue<float>() : fallback;
        bool GetBool(string name, bool fallback) =>
            store.Settings?.FirstOrDefault(s => s.Property == name) is { HasValue: true } s ? s.GetValue<bool>() : fallback;

        var curve = new PressureCurveSettings(
            Softness: Get(nameof(PressureCurveSettings.Softness), 0),
            InputMinimum: Get(nameof(PressureCurveSettings.InputMinimum), 0),
            InputMaximum: Get(nameof(PressureCurveSettings.InputMaximum), 1),
            Minimum: Get(nameof(PressureCurveSettings.Minimum), 0),
            Maximum: Get(nameof(PressureCurveSettings.Maximum), 1),
            MinApproach: GetBool("CutBelowMinimum", false) ? PressureMinApproach.Cut : PressureMinApproach.Clamp);
        var dynamics = new PenDynamicsSettings(
            curve,
            PressureSmoothing: Get("PressureSmoothing", 0),
            PositionSmoothing: Get("PositionSmoothing", 0),
            SmoothAfterCurve: GetBool("SmoothAfterCurve", true));
        return (dynamics, store.Enable);
    }

    /// <summary>Version 1: "Disable pressure sensitivity" with the user's manual pressure per pen end (0–1).</summary>
    public readonly record struct ManualPressure(bool On, double Tip, double Eraser)
    {
        public const double DefaultValue = 0.5;
        public static ManualPressure Off => new(false, DefaultValue, DefaultValue);
    }

    /// <summary>The manual-pressure settings in a profile's filter (Off when absent).</summary>
    public static ManualPressure ReadManual(Profile? profile) => ReadManual(FindStore(profile?.Filters));
    public static ManualPressure ReadManual(Settings? settings, string tabletName) => ReadManual(FindStore(settings, tabletName));

    private static ManualPressure ReadManual(PluginSettingStore? store)
    {
        if (store?.Settings == null) return ManualPressure.Off;
        PluginSetting? S(string n) => store.Settings.FirstOrDefault(s => s.Property == n && s.HasValue);
        return new ManualPressure(S("ManualPressure")?.GetValue<bool>() ?? false,
            S("ManualTipPressure")?.GetValue<float>() ?? ManualPressure.DefaultValue,
            S("ManualEraserPressure")?.GetValue<float>() ?? ManualPressure.DefaultValue);
    }

    /// <summary>V29 #180 (Digital Groceries 21 Nov: "1 Can you make them two different setting values?"): the eraser end's own feel curve, or null
    /// when the profile has none (written by an older app: the eraser then uses the tip's curve).</summary>
    public static PressureCurveSettings? ReadEraserCurve(Profile? profile) => ReadEraserCurve(FindStore(profile?.Filters));
    public static PressureCurveSettings? ReadEraserCurve(Settings? settings, string tabletName) => ReadEraserCurve(FindStore(settings, tabletName));

    private static PressureCurveSettings? ReadEraserCurve(PluginSettingStore? store)
    {
        if (store?.Settings == null) return null;
        PluginSetting? S(string n) => store.Settings.FirstOrDefault(s => s.Property == n && s.HasValue);
        if (S("EraserCurveOwn")?.GetValue<bool>() != true) return null;
        double F(string n, double fallback) => S(n)?.GetValue<float>() ?? fallback;
        return new PressureCurveSettings(
            Softness: F("EraserSoftness", 0),
            InputMinimum: F("EraserInputMinimum", 0),
            InputMaximum: F("EraserInputMaximum", 1),
            Minimum: F("EraserMinimum", 0),
            Maximum: F("EraserMaximum", 1),
            MinApproach: S("EraserCutBelowMinimum")?.GetValue<bool>() == true ? PressureMinApproach.Cut : PressureMinApproach.Clamp);
    }

    /// <summary>Writes (and enables/disables) the filter on the tablet's profile. Mutates
    /// <paramref name="settings"/>. No-op if the profile isn't found. <paramref name="manual"/> null keeps
    /// the profile's current manual-pressure settings; <paramref name="eraser"/> null keeps the eraser's own curve (if any).</summary>
    public static void Write(Settings? settings, string tabletName, PenDynamicsSettings dynamics, bool enable,
        ManualPressure? manual = null, PressureCurveSettings? eraser = null)
    {
        var profile = settings?.Profiles?.FirstOrDefault(p => p.Tablet == tabletName);
        if (profile == null) return;
        WriteProfile(profile, dynamics, enable, manual, eraser);
    }

    /// <summary>Same as <see cref="Write"/> for a profile the caller already has (V29 #159: the factory Tip Feel).</summary>
    public static void WriteProfile(Profile profile, PenDynamicsSettings dynamics, bool enable, ManualPressure? manual = null,
        PressureCurveSettings? eraser = null)
    {
        var store = profile.Filters?.FirstOrDefault(f => f.Path == FilterTypeName);
        if (store == null)
        {
            store = NewStore();
            profile.Filters?.Add(store);
        }

        var curve = dynamics.Curve;
        var m = manual ?? ReadManual(store);
        var e = eraser ?? ReadEraserCurve(store);
        store.Enable = enable;
        var list = new ObservableCollection<PluginSetting>
        {
            new(nameof(PressureCurveSettings.Softness), (float)curve.Softness),
            new(nameof(PressureCurveSettings.InputMinimum), (float)curve.InputMinimum),
            new(nameof(PressureCurveSettings.InputMaximum), (float)curve.InputMaximum),
            new(nameof(PressureCurveSettings.Minimum), (float)curve.Minimum),
            new(nameof(PressureCurveSettings.Maximum), (float)curve.Maximum),
            new("CutBelowMinimum", curve.MinApproach == PressureMinApproach.Cut),
            new("PressureSmoothing", (float)dynamics.PressureSmoothing),
            new("PositionSmoothing", (float)dynamics.PositionSmoothing),
            new("SmoothAfterCurve", dynamics.SmoothAfterCurve),
            new("ManualPressure", m.On),
            new("ManualTipPressure", (float)System.Math.Clamp(m.Tip, 0, 1)),
            new("ManualEraserPressure", (float)System.Math.Clamp(m.Eraser, 0, 1)),
        };
        if (e is { } ec)
        {
            list.Add(new("EraserCurveOwn", true));
            list.Add(new("EraserSoftness", (float)ec.Softness));
            list.Add(new("EraserInputMinimum", (float)ec.InputMinimum));
            list.Add(new("EraserInputMaximum", (float)ec.InputMaximum));
            list.Add(new("EraserMinimum", (float)ec.Minimum));
            list.Add(new("EraserMaximum", (float)ec.Maximum));
            list.Add(new("EraserCutBelowMinimum", ec.MinApproach == PressureMinApproach.Cut));
        }
        store.Settings = list;
    }

    /// <summary>Ensure every profile carries the Pen Dynamics filter, present and enabled — the app's
    /// "always on internally" invariant (#dynamics-always-on). A newly added store gets the factory curve (V29 #159:
    /// Tip Feel 80, no smoothing); an existing store's settings are left untouched. Returns true if anything changed (so the caller
    /// can persist once).</summary>
    public static bool EnsureEnabled(Settings? settings)
    {
        if (settings?.Profiles == null) return false;
        bool changed = false;
        foreach (var profile in settings.Profiles)
        {
            if (profile.Filters == null) continue;
            var store = profile.Filters.FirstOrDefault(f => f.Path == FilterTypeName);
            if (store == null)
            {
                // V29 #159: a profile without the filter (a new install or a tablet seen for the first time) starts
                // at the factory Tip Feel, 80, like Restore factory settings. A profile that has the filter keeps its curve.
                WriteProfile(profile, new PenDynamicsSettings(PressureCurveSettings.Factory, 0, 0, SmoothAfterCurve: true),
                    enable: true, ManualPressure.Off, eraser: PressureCurveSettings.Factory);   // V29 #180: the eraser too
                changed = true;
            }
            else if (!store.Enable)
            {
                store.Enable = true;
                changed = true;
            }
        }
        return changed;
    }

    private static PluginSettingStore? FindStore(Settings? settings, string tabletName) =>
        FindStore(settings?.Profiles?.FirstOrDefault(p => p.Tablet == tabletName)?.Filters);

    private static PluginSettingStore? FindStore(System.Collections.Generic.IEnumerable<PluginSettingStore>? filters) =>
        filters?.FirstOrDefault(f => f.Path == FilterTypeName);

    // The app doesn't have the plugin Type (it's a separate net8 assembly), and PluginSettingStore's
    // only public ctors take a Type/instance — so build an empty store via its JSON constructor and
    // fill the fields. The daemon (which has the plugin) constructs the real filter from Path + Settings.
    private static PluginSettingStore NewStore()
    {
        var store = JsonConvert.DeserializeObject<PluginSettingStore>("{}")!;
        store.Path = FilterTypeName;
        store.Settings = new ObservableCollection<PluginSetting>();
        return store;
    }
}
