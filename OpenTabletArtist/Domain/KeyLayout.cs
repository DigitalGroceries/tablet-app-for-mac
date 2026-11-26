using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace OpenTabletArtist.Domain;

/// <summary>One drawn key of a <see cref="KeyLayout"/>: a box in the layout's drawing box, and the group it belongs to.</summary>
public readonly record struct KeySpot(double X, double Y, double W, double H, int Group)
{
    public bool Overlaps(KeySpot o) => X < o.X + o.W && o.X < X + W && Y < o.Y + o.H && o.Y < Y + H;

    /// <summary>The same spot with the drawing box turned half round (the tablet turned for left-handed use).</summary>
    public KeySpot Turned(double boxW, double boxH) => this with { X = boxW - X - W, Y = boxH - Y - H };
}

/// <summary>A named group of keys ("Above the ring"): the translation key and its English words.</summary>
public readonly record struct KeyGroupName(string Key, string English);

/// <summary>
/// TASKS #212, step-by-step button mapping: a simple drawing of where a tablet family's ExpressKeys sit, so that a
/// first run of "Set up my buttons" lights a key in its real place instead of in one plain column.
///
/// The spots are in the tablet's own order (Wacom's key numbering, right-handed, keys on the left), so place N of a
/// run is the spot with index N. A layout is only made from a Wacom manual (rule 6.1: say only what you checked);
/// <see cref="Proven"/> and <see cref="Source"/> say which. Every other model gets the plain column
/// (<see cref="KeyLayouts.Column"/>), which is not proven.
/// </summary>
public sealed class KeyLayout
{
    public KeyLayout(string family, double width, double height, IEnumerable<KeySpot> spots,
        IEnumerable<KeyGroupName>? groupNames = null, (double X, double Y, double D)? ring = null,
        bool proven = false, string source = "")
    {
        Family = family;
        Width = width;
        Height = height;
        Spots = spots.ToArray();
        GroupNames = (groupNames ?? Array.Empty<KeyGroupName>()).ToArray();
        Ring = ring;
        Proven = proven;
        Source = source;
    }

    /// <summary>"Intuos4 M / L / XL", or "plain column".</summary>
    public string Family { get; }

    /// <summary>The drawing box (the part of the tablet that holds the keys), in drawing units.</summary>
    public double Width { get; }
    public double Height { get; }

    /// <summary>The keys in the tablet's own order; groups follow one another (all of group 0, then group 1 ...).</summary>
    public IReadOnlyList<KeySpot> Spots { get; }

    /// <summary>Names of the groups, one per group, or empty (then the sheet says "Group 1 of 2").</summary>
    public IReadOnlyList<KeyGroupName> GroupNames { get; }

    /// <summary>A Touch Ring drawn among the keys (a circle: left, top, diameter), or null.</summary>
    public (double X, double Y, double D)? Ring { get; }

    /// <summary>True when the layout comes from the manual named in <see cref="Source"/>.</summary>
    public bool Proven { get; }

    /// <summary>Where the layout comes from (manual and page, as in T7 Team/v30/wacom-factory-keys.md).</summary>
    public string Source { get; }

    public int KeyCount => Spots.Count;

    /// <summary>How many keys each group holds, first group first (one entry = one group).</summary>
    public IReadOnlyList<int> GroupSizes =>
        Spots.Count == 0 ? Array.Empty<int>() : Spots.GroupBy(s => s.Group).OrderBy(g => g.Key).Select(g => g.Count()).ToArray();

    /// <summary>The spots as they are seen with the tablet turned half round.</summary>
    public IReadOnlyList<KeySpot> TurnedSpots => Spots.Select(s => s.Turned(Width, Height)).ToArray();

    /// <summary>The ring as seen with the tablet turned half round.</summary>
    public (double X, double Y, double D)? TurnedRing =>
        Ring is { } r ? (Width - r.X - r.D, Height - r.Y - r.D, r.D) : null;
}

/// <summary>TASKS #212: the drawn layout for each tablet family whose key places a Wacom manual shows; facts and pages
/// from T7 Team/v30/wacom-factory-keys.md (researched 23 Nov 2026, checked again by the leader 26 Nov 2026).</summary>
public static class KeyLayouts
{
    // Sizes of a key in drawing units (the sheet draws one unit as one pixel, or smaller for a wide layout).
    private const double KW = 40, KH = 22, Gap = 6;

    /// <summary>The layout for this tablet, or the plain column when no manual shows where its keys sit, or when the
    /// driver reports another number of keys than the manual has (a layout must never hide or add a key).</summary>
    public static KeyLayout For(string? tabletName, int keyCount)
    {
        var model = ModelOf(tabletName);
        if (model != null && Builders.TryGetValue(model, out var make))
        {
            var layout = make();
            if (layout.KeyCount == keyCount) return layout;
        }
        return Column(keyCount);
    }

    /// <summary>The models that have a proven layout (for the tests and the report).</summary>
    public static IReadOnlyCollection<string> ProvenModels => Builders.Keys;

    /// <summary>The model code in a driver tablet name: "Wacom PTK-440" -> "PTK-440",
    /// "Wacom Cintiq 13HD (DTK-1300)" -> "DTK-1300". Null when there is none.</summary>
    public static string? ModelOf(string? tabletName)
    {
        if (string.IsNullOrWhiteSpace(tabletName)) return null;
        var found = Regex.Matches(tabletName.ToUpperInvariant(), @"(?<![A-Z0-9-])[A-Z]{2,4}-[0-9]{3,4}[A-Z]{0,3}(?![A-Z0-9-])");
        return found.Count == 0 ? null : found[^1].Value;
    }

    /// <summary>One plain column, top to bottom, as the sheet drew it before (not proven: the driver's own order).
    /// <paramref name="groups"/> (from an earlier saved mapping) puts a gap before each new group.</summary>
    public static KeyLayout Column(int keyCount, IEnumerable<int>? groups = null)
    {
        keyCount = Math.Max(0, keyCount);
        var g = (groups ?? Array.Empty<int>()).Where(x => x > 0).ToArray();
        if (g.Length < 2 || g.Sum() != keyCount) g = new[] { keyCount };
        var spots = new List<KeySpot>();
        double y = 12;
        for (int gi = 0, i = 0; gi < g.Length; gi++)
        {
            if (gi > 0) y += 14;
            for (int k = 0; k < g[gi]; k++, i++) { spots.Add(new KeySpot(15, y, KW, KH, gi)); y += KH + 8; }
        }
        return new KeyLayout("plain column", 70, Math.Max(y + 4, 40), spots);
    }

    // ── the families (only what the manuals show) ───────────────────────────────────────────────────────────

    private static readonly KeyGroupName AboveRing = new("keymap.group_above_ring", "Above the ring");
    private static readonly KeyGroupName BelowRing = new("keymap.group_below_ring", "Below the ring");
    private static readonly KeyGroupName LeftGroup = new("keymap.group_left", "Left group");
    private static readonly KeyGroupName RightGroup = new("keymap.group_right", "Right group");
    private static readonly KeyGroupName UpperLeft = new("keymap.group_upper_left", "Upper left corner");
    private static readonly KeyGroupName UpperRight = new("keymap.group_upper_right", "Upper right corner");
    private static readonly KeyGroupName TopKeys = new("keymap.group_top", "Keys across the top");
    private static readonly KeyGroupName PowerButton = new("keymap.group_power", "Power button");

    /// <summary>Keys above a Touch Ring and keys below it, on the left edge (Intuos4, Intuos5, Intuos Pro 2013).</summary>
    private static KeyLayout RingColumn(string family, int above, int below, string source)
    {
        var spots = new List<KeySpot>();
        double y = 10;
        for (int i = 0; i < above; i++) { spots.Add(new KeySpot(15, y, KW, KH, 0)); y += KH + Gap; }
        const double d = 50;
        var ring = (X: 10.0, Y: y + 6, D: d);
        y += d + 18;
        for (int i = 0; i < below; i++) { spots.Add(new KeySpot(15, y, KW, KH, 1)); y += KH + Gap; }
        return new KeyLayout(family, 70, y + 4, spots, new[] { AboveRing, BelowRing }, ring, true, source);
    }

    /// <summary>One Intuos3 key group: key 1 tall on the outer edge, 2 above 3 on the inner side, 4 wide at the bottom
    /// (Intuos3 manual p.22 figure). The right group is the mirror image.</summary>
    private static IEnumerable<KeySpot> Intuos3Group(double left, double width, bool mirrored, int group)
    {
        const double pad = 8, tallW = 24, tallH = 80, innerH = 37, wideH = 30;
        double innerW = width - 2 * pad - tallW - Gap;
        double outerX = mirrored ? left + width - pad - tallW : left + pad;
        double innerX = mirrored ? left + pad : left + pad + tallW + Gap;
        yield return new KeySpot(outerX, pad, tallW, tallH, group);
        yield return new KeySpot(innerX, pad, innerW, innerH, group);
        yield return new KeySpot(innerX, pad + innerH + Gap, innerW, innerH, group);
        yield return new KeySpot(left + pad, pad + tallH + Gap, width - 2 * pad, wideH, group);
    }

    private const string Intuos3Source = "Intuos3 User's Manual v4.0 (30 May 2007), p.22 figure (cdn.wacom.com)";

    private static KeyLayout Intuos3One() =>
        new("Intuos3 4x5 / 4x6", 80, 132, Intuos3Group(0, 80, false, 0), proven: true, source: Intuos3Source);

    private static KeyLayout Intuos3Two() =>
        new("Intuos3 6x8 and larger", 400, 132,
            Intuos3Group(0, 80, false, 0).Concat(Intuos3Group(320, 80, true, 1)),
            new[] { LeftGroup, RightGroup }, proven: true, source: Intuos3Source);

    private static KeyLayout IntuosPen2013() =>
        new("Intuos Pen / Pen & Touch 2013", 320, 50, new[]
            {
                new KeySpot(12, 14, KW, KH, 0), new KeySpot(12 + KW + Gap, 14, KW, KH, 0),
                new KeySpot(320 - 12 - 2 * KW - Gap, 14, KW, KH, 1), new KeySpot(320 - 12 - KW, 14, KW, KH, 1),
            },
            new[] { UpperLeft, UpperRight }, proven: true,
            source: "Intuos User's Manual v1.0 Rev J3113 (2013), p.7 figure (keys 1-2 upper left, 3-4 upper right), p.17");

    private static KeyLayout BambooFun2007(string source) =>
        new("Bamboo / Bamboo Fun 2007", 120, 110, new[]
            {
                new KeySpot(8, 8, KW, KH, 0), new KeySpot(72, 8, KW, KH, 0),
                new KeySpot(8, 80, KW, KH, 0), new KeySpot(72, 80, KW, KH, 0),
            },
            ring: (36, 32, 48), proven: true, source: source);

    private static KeyLayout Graphire4() =>
        new("Graphire4", 120, 46, new[] { new KeySpot(10, 12, KW, KH, 0), new KeySpot(70, 12, KW, KH, 0) },
            proven: true, source: "Graphire4 User's Manual (copy, manualsdir.com), site page 16: left key, right key");

    private static KeyLayout Intuos2018(bool wireless)
    {
        const double w = 320, top = 14, kw = 34, kh = 20, gap = 8;
        // Two keys each side of the middle of the top edge; the wireless model's power button sits in the middle.
        double mid = w / 2, inner = wireless ? 24 : 6;
        var spots = new List<KeySpot>
        {
            new(mid - inner - 2 * kw - gap, top, kw, kh, 0), new(mid - inner - kw, top, kw, kh, 0),
            new(mid + inner, top, kw, kh, 0), new(mid + inner + kw + gap, top, kw, kh, 0),
        };
        if (wireless) spots.Add(new KeySpot(mid - 12, top + 2, 24, 16, 1));
        return new KeyLayout(wireless ? "Intuos 2018 Bluetooth" : "Intuos 2018", w, 48, spots,
            wireless ? new[] { TopKeys, PowerButton } : null, proven: true,
            source: "Intuos (2018) user manual CTL-4100WL, p.6 figure: four ExpressKeys across the top"
                + (wireless ? "; item 3 = the power / Bluetooth button in the middle of the top edge" : ""));
    }

    private static KeyLayout Bamboo2009(string source)
    {
        var spots = Enumerable.Range(0, 4).Select(i => new KeySpot(15, 10 + i * (KH + 8), KW, KH, 0));
        return new KeyLayout("Bamboo 2009-2011", 70, 10 + 4 * (KH + 8) + 2, spots, proven: true, source: source);
    }

    private const string Intuos4Source = "Intuos4 manual p.22 (default ExpressKeys diagram), p.23 (Touch Ring)";
    private const string Intuos5Source = "Intuos5 manual p.26 (keys above and below the Touch Ring, top to bottom)";
    private const string IntuosPro2013Source = "Intuos Pro (2013) manual p.19 (pen & touch default settings, 8 keys)";
    private const string BambooFunSource = "Bamboo Fun User's Manual v1.0 (21 Mar 2007, FCC copy) p.17-18: upper left, upper right, lower left, lower right";
    private const string Bamboo07Source = "Bamboo User's Manual v1.1 (26 Apr 2007, copy) p.17-18: upper left, upper right, lower left, lower right";
    private const string Bamboo09Source = "Bamboo User's Manual v3.0 Rev E1410 (2010) p.28, p.41: four keys top to bottom";
    private const string Bamboo11Source = "Bamboo User's Manual (Bamboo3) p.37 diagram, Rev G1511 copy p.34: four keys top to bottom";

    private static readonly Dictionary<string, Func<KeyLayout>> Builders = new(StringComparer.Ordinal)
    {
        // Intuos4: 4 keys above the ring + 4 below (S: 3 + 3).
        ["PTK-640"] = () => RingColumn("Intuos4 M / L / XL", 4, 4, Intuos4Source),
        ["PTK-840"] = () => RingColumn("Intuos4 M / L / XL", 4, 4, Intuos4Source),
        ["PTK-1240"] = () => RingColumn("Intuos4 M / L / XL", 4, 4, Intuos4Source),
        ["PTK-540WL"] = () => RingColumn("Intuos4 Wireless", 4, 4, Intuos4Source),
        ["PTK-440"] = () => RingColumn("Intuos4 S", 3, 3, Intuos4Source + "; S keys in brackets"),
        // Intuos5 M / L and Intuos Pro 2013 M / L: 4 + 4 around the ring.
        ["PTH-650"] = () => RingColumn("Intuos5 M / L", 4, 4, Intuos5Source),
        ["PTH-850"] = () => RingColumn("Intuos5 M / L", 4, 4, Intuos5Source),
        ["PTK-650"] = () => RingColumn("Intuos5 M / L", 4, 4, Intuos5Source),
        ["PTH-651"] = () => RingColumn("Intuos Pro 2013 M / L", 4, 4, IntuosPro2013Source),
        ["PTH-851"] = () => RingColumn("Intuos Pro 2013 M / L", 4, 4, IntuosPro2013Source),
        // Intuos3: one group on the small tablets, a left and a right group on the others.
        ["PTZ-430"] = Intuos3One,
        ["PTZ-431W"] = Intuos3One,
        ["PTZ-630"] = Intuos3Two,
        ["PTZ-631W"] = Intuos3Two,
        ["PTZ-930"] = Intuos3Two,
        ["PTZ-1230"] = Intuos3Two,
        ["PTZ-1231W"] = Intuos3Two,
        // Intuos Pen / Pen & Touch 2013: keys 1-2 at the upper-left corner, 3-4 at the upper-right.
        ["CTL-480"] = IntuosPen2013,
        ["CTH-480"] = IntuosPen2013,
        ["CTH-680"] = IntuosPen2013,
        // Bamboo / Bamboo Fun 2007: upper left, upper right, lower left, lower right around the ring.
        ["CTE-450"] = () => BambooFun2007(BambooFunSource),
        ["CTE-650"] = () => BambooFun2007(BambooFunSource),
        ["MTE-450"] = () => BambooFun2007(Bamboo07Source),
        // Graphire4: a left key and a right key.
        ["CTE-440"] = Graphire4,
        ["CTE-640"] = Graphire4,
        // Intuos 2018: four keys across the top (the Bluetooth models add the power button in the middle).
        ["CTL-4100"] = () => Intuos2018(false),
        ["CTL-6100"] = () => Intuos2018(false),
        ["CTL-4100WL"] = () => Intuos2018(true),
        ["CTL-6100WL"] = () => Intuos2018(true),
        // Bamboo 2009-2011: four keys top to bottom.
        ["CTH-460"] = () => Bamboo2009(Bamboo09Source),
        ["CTH-461"] = () => Bamboo2009(Bamboo09Source),
        ["CTH-661"] = () => Bamboo2009(Bamboo09Source),
        ["CTH-470"] = () => Bamboo2009(Bamboo11Source),
        ["CTH-670"] = () => Bamboo2009(Bamboo11Source),
    };
}
