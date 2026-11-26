using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenTabletDriver.Configurations;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletArtist.Services;

/// <summary>A tablet set up before connecting it is connected, but another model came instead (TASKS #76).</summary>
/// <param name="Connected">The tablet that is plugged in now (not set up yet).</param>
/// <param name="Chosen">The model the user set up beforehand.</param>
public sealed record OfflineSetupOffer(string Connected, string Chosen);

/// <summary>
/// TASKS #76 (Version 28, agreed with Digital Groceries 14 Nov): the whole app is usable with no tablet plugged in. The user picks
/// their model from the built-in catalog (or a sample tablet), the app saves a normal OpenTabletDriver profile for it,
/// and every page works on that remembered tablet; the driver uses those settings the moment that model is plugged in.
/// If a different model is plugged in, the app offers to copy what the two share (pen and screen mapping).
/// </summary>
/// <remarks>
/// A profile for a tablet that isn't connected is what OpenTabletDriver already keeps for every tablet it has seen
/// (a "remembered" tablet); this only creates one up front. The Developer page's "force a tablet" used the same idea.
/// </remarks>
public static class OfflineTablet
{
    /// <summary>The sample tablet: the Intuos4 S the app was built for and is tested with (Digital Groceries' own).</summary>
    public const string SampleTablet = "Wacom PTK-440";

    private const string MarkPrefix = "SetUpBeforeConnecting:";
    private const string OfferPrefix = "OfflineSetupOffered:";
    private const string SeenPrefix = "TabletFirstSeen:";

    // ---------------------------------------------------------------- the catalog

    private static readonly ConcurrentDictionary<string, TabletConfiguration?> Configs = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, JObject?> Json = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The built-in configuration for <paramref name="name"/>, or null when the catalog doesn't have it.</summary>
    public static TabletConfiguration? ConfigFor(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return Configs.GetOrAdd(name, n =>
        {
            try
            {
                return new DeviceConfigurationProvider().TabletConfigurations
                    .FirstOrDefault(c => c != null && string.Equals(c.Name, n, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                AppLog.Warn($"Couldn't read the built-in tablet catalog for \"{n}\".", ex);
                return null;
            }
        });
    }

    /// <summary>The tablet as the driver reports a connected one (<c>{"Properties": {...}}</c>), built from the
    /// catalog, so the About, Mapping and ExpressKeys pages can show a tablet that isn't plugged in.</summary>
    public static JObject? CatalogTabletJson(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return Json.GetOrAdd(name, n =>
        {
            var config = ConfigFor(n);
            if (config == null) return null;
            try { return new JObject { ["Properties"] = JObject.FromObject(config) }; }
            catch (Exception ex) { AppLog.Warn($"Couldn't describe \"{n}\" from the built-in catalog.", ex); return null; }
        });
    }

    /// <summary>The connected tablets plus, for every remembered tablet that isn't connected, its catalog entry.
    /// Connected tablets always win: the driver's own report is the truth for a plugged-in tablet.</summary>
    public static JArray WithRemembered(JToken? connected, IEnumerable<string> rememberedNames)
    {
        var all = new JArray();
        var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (connected is JArray arr)
            foreach (var t in arr)
            {
                all.Add(t.DeepClone());
                if ((t["Properties"] ?? t)["Name"]?.ToString() is { Length: > 0 } n) have.Add(n);
            }
        foreach (var name in rememberedNames)
        {
            if (string.IsNullOrWhiteSpace(name) || !have.Add(name)) continue;
            if (CatalogTabletJson(name) is { } json) all.Add(json.DeepClone());
        }
        return all;
    }

    // ---------------------------------------------------------------- building the profile

    /// <summary>A full default profile for <paramref name="name"/> from the catalog (output mode, area, bindings,
    /// as the driver makes one for a new tablet), or null when the catalog doesn't know the model.</summary>
    public static Profile? BuildProfile(string name)
    {
        var config = ConfigFor(name);
        if (config == null) return null;
        Profile profile;
        try
        {
            profile = Profile.GetDefaults(new TabletReference(config, Array.Empty<DeviceIdentifier>()));
        }
        catch (Exception ex)
        {
            // Outside the driver the virtual-screen service is absent, so the area defaults can throw; the
            // rest of the profile is still made below. OTD 0.6.8 made Tablet and OutputMode 'required'.
            AppLog.Debug($"Profile defaults for \"{name}\" outside the driver: {ex.Message}");
            profile = new Profile
            {
                Tablet = config.Name,
                OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)),
                BindingSettings = BindingSettings.GetDefaults(config.Specifications),
                RelativeModeSettings = RelativeModeSettings.GetDefaults(),
            };
        }
        profile.Tablet = config.Name; // the catalog's own spelling, which is what the driver reports
        FillValidAreas(profile, config.Specifications?.Digitizer);
        return profile;
    }

    /// <summary>Keeps the Absolute-mode areas non-null and non-degenerate: tablet area = the whole digitizer (100 mm
    /// square if unknown), display = a plain 1080p rectangle until the app maps it to the user's screen.</summary>
    internal static void FillValidAreas(Profile profile, DigitizerSpecifications? digitizer)
    {
        var abs = profile.AbsoluteModeSettings ??= new AbsoluteModeSettings { Display = new AreaSettings(), Tablet = new AreaSettings() };
        abs.Tablet ??= new AreaSettings();
        abs.Display ??= new AreaSettings();
        if (abs.Tablet.Width <= 0 || abs.Tablet.Height <= 0)
        {
            float w = digitizer is { Width: > 0 } ? digitizer.Width : 100f;
            float h = digitizer is { Height: > 0 } ? digitizer.Height : 100f;
            abs.Tablet.Width = w; abs.Tablet.Height = h; abs.Tablet.X = w / 2f; abs.Tablet.Y = h / 2f;
        }
        if (abs.Display.Width <= 0 || abs.Display.Height <= 0)
        {
            abs.Display.Width = 1920; abs.Display.Height = 1080; abs.Display.X = 960; abs.Display.Y = 540;
        }
    }

    // ---------------------------------------------------------------- marks (app settings)

    private static string Key(string prefix, string name) => prefix + name.ToLowerInvariant();

    /// <summary>Remember that <paramref name="name"/> was set up before it was ever connected.</summary>
    public static void MarkSetUpBeforeConnecting(string name) =>
        AppSettings.Set(Key(MarkPrefix, name), DateTime.Now.ToString("o"));

    public static bool IsSetUpBeforeConnecting(string name) => AppSettings.Get(Key(MarkPrefix, name)) != null;

    /// <summary>When <paramref name="name"/> was set up beforehand, or null.</summary>
    public static DateTime? SetUpAt(string name) => Time(AppSettings.Get(Key(MarkPrefix, name)));

    /// <summary>Remembers the first time this app saw <paramref name="name"/> connected (kept once set).</summary>
    public static void MarkSeen(string name)
    {
        if (AppSettings.Get(Key(SeenPrefix, name)) == null) AppSettings.Set(Key(SeenPrefix, name), DateTime.Now.ToString("o"));
    }

    public static DateTime? FirstSeen(string name) => Time(AppSettings.Get(Key(SeenPrefix, name)));

    /// <summary>Pure: a connected tablet is "new, not set up yet" for a model set up beforehand only when the app first
    /// saw it after that set-up. A tablet the user already had (seen before, or before this version) is never offered
    /// the copy, so its own settings can't be overwritten by the model picked while it was unplugged.</summary>
    public static bool IsNewSince(DateTime? firstSeen, DateTime? setUpAt) =>
        firstSeen is { } seen && setUpAt is { } setUp && seen > setUp;

    private static DateTime? Time(string? s) =>
        DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t) ? t : null;

    /// <summary>The chosen model has been plugged in: from now on it is an ordinary tablet.</summary>
    public static void ClearMark(string name) => AppSettings.Remove(Key(MarkPrefix, name));

    private static string OfferKey(OfflineSetupOffer offer) => Key(OfferPrefix, offer.Connected + "|" + offer.Chosen);

    public static void MarkOfferAnswered(OfflineSetupOffer offer) => AppSettings.Set(OfferKey(offer), DateTime.Now.ToString("o"));

    public static bool IsOfferAnswered(OfflineSetupOffer offer) => AppSettings.Get(OfferKey(offer)) != null;

    /// <summary>Pure: which offer to make, if any. A tablet is connected that the user didn't set up beforehand,
    /// while a model they did set up beforehand has never been connected. Offers already answered are skipped.</summary>
    public static OfflineSetupOffer? PickOffer(IEnumerable<string> connected, IEnumerable<string> setUpBeforehand,
        Func<OfflineSetupOffer, bool> answered, Func<OfflineSetupOffer, bool>? isNew = null)
    {
        var chosen = setUpBeforehand.Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        if (chosen.Count == 0) return null;
        foreach (var c in connected)
        {
            if (string.IsNullOrWhiteSpace(c) || chosen.Contains(c, StringComparer.OrdinalIgnoreCase)) continue;
            foreach (var ch in chosen)
            {
                var offer = new OfflineSetupOffer(c, ch);
                if (!answered(offer) && (isNew == null || isNew(offer))) return offer;
            }
        }
        return null;
    }

    // ---------------------------------------------------------------- copying what the two share

    /// <summary>Copies what two different tablets share from <paramref name="from"/> to <paramref name="to"/>: the
    /// pen (tip, eraser, pen buttons that both have), the filters (pressure curve, smoothing, the radial menu's),
    /// the output mode and the screen mapping (screen area as it is; tablet area at the same share of the new
    /// tablet's surface). ExpressKeys and Touch Rings/Strips stay as the new tablet's own: their number and layout
    /// differ between models. <paramref name="fromDigitizer"/>/<paramref name="toDigitizer"/>: sizes in mm, when known.</summary>
    public static void CopyShared(Profile from, Profile to, (float W, float H)? fromDigitizer, (float W, float H)? toDigitizer)
    {
        var fb = from.BindingSettings;
        var tb = to.BindingSettings;
        if (fb != null && tb != null)
        {
            tb.TipButton = Clone(fb.TipButton);
            tb.TipActivationThreshold = fb.TipActivationThreshold;
            tb.EraserButton = Clone(fb.EraserButton);
            tb.EraserActivationThreshold = fb.EraserActivationThreshold;
            if (fb.PenButtons != null && tb.PenButtons != null)
                for (int i = 0; i < Math.Min(fb.PenButtons.Count, tb.PenButtons.Count); i++)
                    tb.PenButtons[i] = Clone(fb.PenButtons[i]);
            tb.DisablePressure = fb.DisablePressure;
            tb.DisableTilt = fb.DisableTilt;
            tb.DisableRotation = fb.DisableRotation;
            tb.EnableDragBindings = fb.EnableDragBindings;
            tb.TipDoubleClickDistance = fb.TipDoubleClickDistance;
            tb.DoubleClickFix = fb.DoubleClickFix;
        }

        if (from.Filters != null) to.Filters = Clone(from.Filters)!;
        if (from.OutputMode != null) to.OutputMode = Clone(from.OutputMode)!;
        if (from.RelativeModeSettings != null) to.RelativeModeSettings = Clone(from.RelativeModeSettings);

        if (from.AbsoluteModeSettings is { } fa)
        {
            var ta = to.AbsoluteModeSettings ??= Clone(fa)!;
            ta.Display = Clone(fa.Display)!;
            ta.EnableClipping = fa.EnableClipping;
            ta.EnableAreaLimiting = fa.EnableAreaLimiting;
            ta.LockAspectRatio = fa.LockAspectRatio;
            if (fa.Tablet is { } ft && fromDigitizer is { W: > 0, H: > 0 } fd && toDigitizer is { W: > 0, H: > 0 } td)
            {
                float sx = td.W / fd.W, sy = td.H / fd.H;
                ta.Tablet = new AreaSettings
                {
                    Width = ft.Width * sx, Height = ft.Height * sy,
                    X = ft.X * sx, Y = ft.Y * sy, Rotation = ft.Rotation,
                };
            }
        }
    }

    private static T? Clone<T>(T? value) where T : class =>
        value == null ? null : JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value));
}
