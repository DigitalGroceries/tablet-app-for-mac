using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTabletArtist.Domain;

/// <summary>What one ExpressKey does from the factory, as far as the app can do it (TASKS #212 part 5). The plan
/// (T7 Team/v30/proposal-step-by-step-mapping.md, step 5) allows only these; every other Wacom function
/// (Settings, Precision Mode, Display Toggle, Bamboo Dock, Back, Exposé ...) stays "Not set up".</summary>
public enum FactoryFunction
{
    NotSetUp,
    Shift,
    Command,
    Option,
    /// <summary>Wacom's Pan/Scroll: the app's "Move pen to scroll".</summary>
    PenScroll,
    RadialMenu,
    /// <summary>Touch On/Off: only on tablets whose finger touch the app drives (Domain.TouchTrackpad).</summary>
    TouchOnOff,
}

/// <summary>One key of a model's factory list: Wacom's own name for it and what the app gives it.</summary>
public readonly record struct FactoryKey(string WacomName, FactoryFunction Function);

/// <summary>A model's factory ExpressKeys in Wacom's order (= the places of its proven <see cref="KeyLayout"/>), and
/// the manual page.</summary>
public sealed record FactoryKeyList(IReadOnlyList<FactoryKey> Keys, string Source);

/// <summary>
/// TASKS #212 part 5: "Give the keys Wacom's own functions?" - only for models whose factory functions are VERIFIED in
/// T7 Team/v30/wacom-factory-keys.md AND whose key places are proven (<see cref="KeyLayouts"/>), so place N of a run is
/// Wacom's key N. Models whose list has nothing the app can do (Graphire4, Bamboo 2007) get no offer.
/// </summary>
public static class WacomFactoryKeys
{
    private static FactoryKey K(string name, FactoryFunction f = FactoryFunction.NotSetUp) => new(name, f);

    private static readonly FactoryKey[] Intuos4Lower =
    {
        K("Shift", FactoryFunction.Shift), K("Ctrl/Cmd", FactoryFunction.Command),
        K("Alt/Opt", FactoryFunction.Option), K("Pan/Scroll", FactoryFunction.PenScroll),
    };

    private static FactoryKeyList Intuos4(string source) => new(new[]
        { K("Show Settings"), K("Radial Menu", FactoryFunction.RadialMenu), K("Precision Mode"), K("Display Toggle") }
        .Concat(Intuos4Lower).ToArray(), source);

    private static FactoryKeyList PenAndTouch8(string source) => new(new[]
        { K("Touch On/Off", FactoryFunction.TouchOnOff), K("Settings"), K("Precision Mode"), K("Display Toggle") }
        .Concat(Intuos4Lower).ToArray(), source);

    private static readonly FactoryKey[] Intuos3Group =
    {
        K("Command (Ctrl)", FactoryFunction.Command), K("Shift", FactoryFunction.Shift),
        K("Option (Alt)", FactoryFunction.Option), K("Pan/Scroll", FactoryFunction.PenScroll),
    };

    private const string Intuos3Source = "Intuos3 User's Manual v4.0 (2007) p.22";

    private static readonly Dictionary<string, Func<FactoryKeyList>> Lists = new(StringComparer.Ordinal)
    {
        ["PTK-640"] = () => Intuos4("Intuos4 User's Manual p.22"),
        ["PTK-840"] = () => Intuos4("Intuos4 User's Manual p.22"),
        ["PTK-1240"] = () => Intuos4("Intuos4 User's Manual p.22"),
        ["PTK-540WL"] = () => Intuos4("Intuos4 Wireless User's Manual p.31"),
        ["PTK-440"] = () => new(new[]
        {
            K("Show Settings"), K("Precision Mode"), K("Shift", FactoryFunction.Shift),
            K("Ctrl/Cmd", FactoryFunction.Command), K("Alt/Opt", FactoryFunction.Option), K("Pan/Scroll", FactoryFunction.PenScroll),
        }, "Intuos4 User's Manual p.22 (the bracketed Intuos4 S entries)"),
        ["PTH-650"] = () => PenAndTouch8("Intuos5 User's Manual p.26"),
        ["PTH-850"] = () => PenAndTouch8("Intuos5 User's Manual p.26"),
        ["PTK-650"] = () => new(new[]
            { K("Settings"), K("Radial Menu", FactoryFunction.RadialMenu), K("Precision Mode"), K("Display Toggle") }
            .Concat(Intuos4Lower).ToArray(), "Intuos5 User's Manual p.26"),
        ["PTH-651"] = () => PenAndTouch8("Intuos Pro User's Manual (2013) p.19"),
        ["PTH-851"] = () => PenAndTouch8("Intuos Pro User's Manual (2013) p.19"),
        ["PTZ-430"] = () => new(Intuos3Group, Intuos3Source),
        ["PTZ-431W"] = () => new(Intuos3Group, Intuos3Source),
        // The larger Intuos3: the left and the right group have the same list.
        ["PTZ-630"] = () => new(Intuos3Group.Concat(Intuos3Group).ToArray(), Intuos3Source),
        ["PTZ-631W"] = () => new(Intuos3Group.Concat(Intuos3Group).ToArray(), Intuos3Source),
        ["PTZ-930"] = () => new(Intuos3Group.Concat(Intuos3Group).ToArray(), Intuos3Source),
        ["PTZ-1230"] = () => new(Intuos3Group.Concat(Intuos3Group).ToArray(), Intuos3Source),
        ["PTZ-1231W"] = () => new(Intuos3Group.Concat(Intuos3Group).ToArray(), Intuos3Source),
        // Intuos Pen / Pen & Touch 2013, Mac defaults: Shift, Option, Command, Control (Control is not in the plan's list).
        ["CTL-480"] = IntuosPen2013,
        ["CTH-480"] = IntuosPen2013,
        ["CTH-680"] = IntuosPen2013,
        // Bamboo 2009-2011: Touch Toggle, Bamboo Dock, Right Click, Click (only Touch Toggle is in the plan's list).
        ["CTH-460"] = () => Bamboo2009("Bamboo User's Manual v3.0 (2010) p.28"),
        ["CTH-461"] = () => Bamboo2009("Bamboo User's Manual v3.0 (2010) p.28"),
        ["CTH-661"] = () => Bamboo2009("Bamboo User's Manual v3.0 (2010) p.28"),
        ["CTH-470"] = () => Bamboo2009("Bamboo User's Manual (Bamboo3) p.37"),
        ["CTH-670"] = () => Bamboo2009("Bamboo User's Manual (Bamboo3) p.37"),
        // Graphire4 (Back, Forward) and Bamboo 2007 (Back, Forward, Show Desktop, Exposé): verified, but nothing the
        // plan's list holds, so no offer.
        ["CTE-440"] = () => new(new[] { K("Back"), K("Forward") }, "Graphire4 User's Manual (copy) site page 16"),
        ["CTE-640"] = () => new(new[] { K("Back"), K("Forward") }, "Graphire4 User's Manual (copy) site page 16"),
        ["CTE-450"] = () => Bamboo2007("Bamboo Fun User's Manual v1.0 (2007) p.17"),
        ["CTE-650"] = () => Bamboo2007("Bamboo Fun User's Manual v1.0 (2007) p.17"),
        ["MTE-450"] = () => Bamboo2007("Bamboo User's Manual v1.1 (2007) p.17"),
    };

    private static FactoryKeyList IntuosPen2013() => new(new[]
    {
        K("Shift", FactoryFunction.Shift), K("Option", FactoryFunction.Option),
        K("Command", FactoryFunction.Command), K("Control"),
    }, "Intuos User's Manual v1.0 Rev J3113 (2013) p.17");

    private static FactoryKeyList Bamboo2009(string source) => new(new[]
        { K("Touch Toggle", FactoryFunction.TouchOnOff), K("Bamboo Dock"), K("Right Click"), K("Click") }, source);

    private static FactoryKeyList Bamboo2007(string source) => new(new[]
        { K("Back"), K("Forward"), K("Show Desktop"), K("Exposé") }, source);

    /// <summary>The models with a verified factory list (whether or not anything in it can be offered).</summary>
    public static IReadOnlyCollection<string> VerifiedModels => Lists.Keys;

    /// <summary>The factory list for a tablet, or null when it is not verified, its key places are not proven, or the
    /// key count does not match. <paramref name="touchWorks"/> = the app drives this tablet's finger touch; without it
    /// Touch On/Off stays "Not set up".</summary>
    public static FactoryKeyList? For(string? tabletName, int keyCount, bool touchWorks)
    {
        var model = KeyLayouts.ModelOf(tabletName);
        if (model == null || !Lists.TryGetValue(model, out var make)) return null;
        var layout = KeyLayouts.For(tabletName, keyCount);
        if (!layout.Proven) return null;
        var list = make();
        if (list.Keys.Count != keyCount) return null;
        if (!touchWorks)
            list = list with { Keys = list.Keys.Select(k => k.Function == FactoryFunction.TouchOnOff ? k with { Function = FactoryFunction.NotSetUp } : k).ToArray() };
        return list;
    }

    /// <summary>What each driver key gets (driver key number from 0 -> function) from a run's mapping: only places
    /// whose key was recorded, and only functions the app can do. Empty = nothing to offer.</summary>
    public static IReadOnlyDictionary<int, FactoryFunction> Plan(FactoryKeyList? list, KeyOrderMap map)
    {
        var plan = new Dictionary<int, FactoryFunction>();
        if (list == null || map.Flipped) return plan;   // places are the tablet's own order only when not saved turned
        for (int place = 0; place < list.Keys.Count && place < map.Keys.Count; place++)
        {
            int key = map.Keys[place];
            var f = list.Keys[place].Function;
            if (key >= 0 && f != FactoryFunction.NotSetUp) plan[key] = f;
        }
        return plan;
    }

    /// <summary>The app's binding for a function, or null for <see cref="FactoryFunction.NotSetUp"/>.</summary>
    public static AuxBinding? Binding(FactoryFunction f) => f switch
    {
        // A modifier on its own: the driver holds it down while the key is held (MacOSVirtualKeyboard keeps its flag).
        FactoryFunction.Shift => Keyboard("Shift"),
        FactoryFunction.Command => Keyboard("Application"),   // OTD's Application key is Command on a Mac
        FactoryFunction.Option => Keyboard("Alt"),
        FactoryFunction.PenScroll => new AuxBinding(AuxKind.Scroll, AuxCombo.Unbound, AuxKeyBinding.None,
            PenScrollBinding.ScrollValue, PenScrollMath.DefaultSpeed),
        FactoryFunction.RadialMenu => AuxBinding.RadialMenu(null),
        FactoryFunction.TouchOnOff => AuxBinding.TouchToggle,
        _ => null,
    };

    private static AuxBinding Keyboard(string key) =>
        new(AuxKind.Keyboard, new AuxCombo(false, false, false, key), AuxKeyBinding.None, AuxKeyBinding.None);

    /// <summary>The function's name on the sheet, in the app's words.</summary>
    public static string Name(FactoryFunction f) => f switch
    {
        FactoryFunction.Shift => Services.Localizer.Te("keymap.fn.shift", "Shift"),
        FactoryFunction.Command => Services.Localizer.Te("keymap.fn.command", "Command"),
        FactoryFunction.Option => Services.Localizer.Te("keymap.fn.option", "Option"),
        FactoryFunction.PenScroll => Services.Localizer.Te("binding.scroll.pen", "Move pen to scroll"),
        FactoryFunction.RadialMenu => Services.Localizer.Te("keymap.fn.radial", "Radial menu"),
        FactoryFunction.TouchOnOff => Services.Localizer.Te("keymap.fn.touch", "Touch on/off"),
        _ => Services.Localizer.Te("keymap.fn.not_set_up", "Not set up"),
    };
}
