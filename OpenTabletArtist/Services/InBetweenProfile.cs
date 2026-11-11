using System.Collections.ObjectModel;
using System.Linq;
using Newtonsoft.Json;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;

namespace OpenTabletArtist.Services;

/// <summary>
/// #40: reads and writes the "Smoother fast curves" filter (the in-between points filter in the bundled plugin) on a
/// tablet's profile, like <see cref="HoverProfile"/>. Off by default. When on it is kept LAST among the filters, so
/// the curve is drawn through the points the other filters (calibration, pen dynamics) produce.
/// </summary>
public static class InBetweenProfile
{
    public const string FilterTypeName = "OpenTabletArtist.Dynamics.InBetweenFilter";

    /// <summary>In-between points per second: 2.5 for every real Intuos4 report (about 200 a second).</summary>
    public const float PointsPerSecond = 500f;

    private const string FrequencyProperty = "Frequency";   // AsyncPositionedPipelineElement's own setting

    public static bool IsOn(Settings? settings, string tabletName) =>
        IsOn(settings?.Profiles?.FirstOrDefault(p => p.Tablet == tabletName));

    public static bool IsOn(Profile? profile) =>
        profile?.Filters?.FirstOrDefault(f => f.Path == FilterTypeName) is { Enable: true };

    /// <summary>Turns the filter on (adding it last) or off. No-op if the tablet has no profile.</summary>
    public static void Write(Settings? settings, string tabletName, bool on)
    {
        var profile = settings?.Profiles?.FirstOrDefault(p => p.Tablet == tabletName);
        if (profile?.Filters == null) return;

        var store = profile.Filters.FirstOrDefault(f => f.Path == FilterTypeName);
        if (store != null) profile.Filters.Remove(store);
        if (!on && store == null) return;

        store ??= NewStore();
        store.Enable = on;
        store.Settings = new ObservableCollection<PluginSetting> { new(FrequencyProperty, PointsPerSecond) };
        profile.Filters.Add(store);
    }

    private static PluginSettingStore NewStore()
    {
        var store = JsonConvert.DeserializeObject<PluginSettingStore>("{}")!;
        store.Path = FilterTypeName;
        store.Settings = new ObservableCollection<PluginSetting>();
        return store;
    }
}
