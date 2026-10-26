using System;
using System.Collections.Generic;
using System.Linq;
using OpenTabletDriver.Desktop.Profiles;

namespace OpenTabletArtist.Domain;

/// <summary>Version 14: one extra pen in the Tool row (Wacom-style per-pen settings). The first pen the app
/// sees on a tablet is that tablet's main pen and uses the ordinary Pen tile; every other pen that comes near
/// gets its own tile, starting as a copy of the main pen's settings.
/// Per pen: tip, eraser and side-button actions and their click thresholds. Shared by all pens (v1): Tip Feel,
/// manual pressure, tilt, mapping — the same split as Application-row profiles.</summary>
public sealed class PenProfile
{
    /// <summary><see cref="PenToolSignal.Key"/>: the pen's serial number (or tool id when it has none).</summary>
    public string Key { get; set; } = "";

    public ulong Serial { get; set; }

    /// <summary>The tool id of the pen's tip end, from the first time it was seen.</summary>
    public uint RawToolId { get; set; }

    /// <summary>Shown under the tile, e.g. "Art Pen" or "Grip Pen 2".</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>A copy of the binding table; only the pen parts (see <see cref="PenBindings"/>) are used.</summary>
    public BindingSettings Bindings { get; set; } = new();
}

/// <summary>The pen parts of OTD's binding table, and Wacom tool names.</summary>
public static class PenBindings
{
    /// <summary>Copies the pen parts (tip, eraser, side buttons, thresholds) from <paramref name="pen"/> onto
    /// <paramref name="target"/>; ExpressKeys, Touch Ring and mouse bindings stay as they are.</summary>
    public static void Overlay(BindingSettings target, BindingSettings pen)
    {
        target.TipButton = pen.TipButton;
        target.TipActivationThreshold = pen.TipActivationThreshold;
        target.EraserButton = pen.EraserButton;
        target.EraserActivationThreshold = pen.EraserActivationThreshold;
        target.PenButtons = pen.PenButtons;
    }

    /// <summary>Wacom's names for Intuos4-generation tools, from libwacom's data/wacom.stylus (linuxwacom
    /// project): tip-end and eraser-end tool ids. Higher bits some tablets add are ignored after an exact
    /// look-up fails.</summary>
    static readonly Dictionary<uint, string> Names = new()
    {
        [0x802] = "Grip Pen", [0x80A] = "Grip Pen",
        [0x804] = "Art Pen", [0x80C] = "Art Pen",
        [0x902] = "Airbrush Pen", [0x90A] = "Airbrush Pen",
        [0x4802] = "Classic Pen", [0x480A] = "Classic Pen",
        [0x2802] = "Inking Pen",
    };

    /// <summary>Pucks are not pens: libwacom's Intuos4 Five Button Mouse (0x806) and Lens Cursor (0x006).</summary>
    static readonly HashSet<uint> Pucks = new() { 0x806, 0x006 };

    public static bool IsPuck(uint rawToolId) => Pucks.Contains(rawToolId) || Pucks.Contains(rawToolId & 0xFFFF);

    /// <summary>"Art Pen", or "Pen" when the tool id isn't known.</summary>
    public static string ToolName(uint rawToolId)
    {
        if (Names.TryGetValue(rawToolId, out var n)) return n;
        if (Names.TryGetValue(rawToolId & 0xFFFF, out n)) return n;
        return "Pen";
    }

    /// <summary>The tool name, numbered when another listed pen already has it ("Grip Pen 2").</summary>
    public static string UniqueName(uint rawToolId, IEnumerable<string> taken)
    {
        var name = ToolName(rawToolId);
        var used = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(name)) return name;
        for (int i = 2; ; i++)
            if (!used.Contains($"{name} {i}")) return $"{name} {i}";
    }
}
