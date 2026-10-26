using System.Collections.Generic;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

// Moved out of TabletDetailViewModel (#751). Nothing changed but the file it lives in: these
// are top-level types that never touched the editor's fields, so the move is mechanical and
// the editor is that much smaller to read.
//
// The ABOUT tab's spec rows, and the three pure functions that build them from a

// TabletAboutInfo. Static and side-effect free, which is why they could move without ceremony.

/// <summary>One label/value row in the tablet ABOUT tab's spec list.</summary>
public sealed record TabletFact(string Label, string Value);

/// <summary>Builds the ABOUT tab's rows. Pure: every input arrives as a parameter.</summary>
public static class TabletAboutFacts
{
    static string L(string key, string english) => Localizer.Te(key, english);

    // Core dimensions — the SPECIFICATIONS card (up to and including the active-area aspect ratio).
    public static IReadOnlyList<TabletFact> BuildFacts(TabletAboutInfo a)
    {
        var facts = new System.Collections.Generic.List<TabletFact>();
        if (!string.IsNullOrEmpty(a.Name)) facts.Add(new(L("tablet.about.name", "Name"), a.Name));
        return facts;
    }

    // The active area's own measurements, split out of the identity card so the ACTIVE AREA section can
    // hold them next to the drawing of the same thing. Labels drop the "Active area" prefix — the section
    // heading carries it, and repeating it on every row was only there when they sat under "BASICS".
    public static IReadOnlyList<TabletFact> BuildActiveAreaFacts(TabletAboutInfo a)
    {
        var facts = new System.Collections.Generic.List<TabletFact>();
        if (a.WidthMm > 0 && a.HeightMm > 0)
        {
            facts.Add(new(L("tablet.mapping.size", "Size"), TabletAboutInfo.FormatSize(a.WidthMm, a.HeightMm)));
            double diag = System.Math.Sqrt(a.WidthMm * a.WidthMm + a.HeightMm * a.HeightMm);
            facts.Add(new(L("tablet.mapping.diagonal", "Diagonal"), TabletAboutInfo.FormatLength(diag)));
            facts.Add(new(L("tablet.mapping.aspect", "Aspect ratio"), TabletAboutInfo.FormatAspectRatio(a.WidthMm, a.HeightMm)));
        }
        return facts;
    }

    // Capabilities — the FEATURES card (everything after the active-area aspect ratio).
    public static IReadOnlyList<TabletFact> BuildFeatures(TabletAboutInfo a)
    {
        var facts = new System.Collections.Generic.List<TabletFact>();
        if (a.LpMm is > 0 && a.Lpi is > 0)
            facts.Add(new(L("tablet.about.digitizer_resolution", "Digitizer resolution"), $"{a.LpMm:N0} LPmm ({a.Lpi:N0} LPI)"));
        if (a.MaxPressure is > 0) facts.Add(new(L("tablet.about.pressure_levels", "Pressure levels"), $"{a.MaxPressure:N0}"));
        if (a.PenButtons is { } pb) facts.Add(new(L("tablet.about.pen_buttons", "Pen buttons"), pb.ToString()));
        if (a.ExpressKeys is > 0) facts.Add(new(L("tablet.about.tablet_buttons", "Tablet buttons"), a.ExpressKeys!.Value.ToString()));
        if (a.WheelCount > 0) facts.Add(new(L("tablet.about.wheels", "Wheels"), a.WheelCount == 1 ? L("tablet.about.yes", "Yes") : a.WheelCount.ToString()));
        if (a.StripCount > 0) facts.Add(new(L("tablet.about.touch_strips", "Touch strips"), a.StripCount.ToString()));
        if (a.HasTouch) facts.Add(new(L("tablet.about.touch_input", "Touch input"), L("tablet.about.supported", "Supported")));
        return facts;
    }
}
