using System;
using System.Collections.Generic;

namespace OpenTabletArtist.Domain;

/// <summary>TASKS #65 (Zak 20:41): the short text shown near the radial menu when a slice has run, "Selection copied" for Copy. One plain
/// past-tense phrase per built-in action (found by its key combination, so a renamed or translated slice still gets its text); any other
/// slice says "&lt;name&gt; done"; a slice that could not run says so ("&lt;name&gt; did not work") instead of pretending it worked.</summary>
public static class RadialMenuConfirm
{
    /// <summary>Key combination of a built-in slice (as the default menu stores it) to the string key and English text.</summary>
    private static readonly Dictionary<string, (string Key, string English)> BuiltIn = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Application+Z"] = ("undo", "Undone"),
        ["Application+Shift+Z"] = ("redo", "Redone"),
        ["Application+C"] = ("copy", "Selection copied"),
        ["Application+V"] = ("paste", "Pasted"),
        ["Application+X"] = ("cut", "Selection cut"),
        ["Application+A"] = ("select_all", "All selected"),
        ["Application+Minus"] = ("zoom_out", "Zoomed out"),
        ["Application+Equal"] = ("zoom_in", "Zoomed in"),
    };

    /// <summary>The string-table key suffix (<c>radial.done.&lt;suffix&gt;</c>) of a built-in key combination, or null.</summary>
    public static string? BuiltInKey(string? keys) =>
        !string.IsNullOrWhiteSpace(keys) && BuiltIn.TryGetValue(keys.Trim(), out var b) ? b.Key : null;

    /// <summary>The text for a slice that ran (<paramref name="ok"/>) or failed. <paramref name="te"/> is (key, English) to translated text.</summary>
    public static string Text(Func<string, string, string> te, string? label, string? keys, bool ok)
    {
        var name = (label ?? "").Trim();
        if (!ok)
            return name.Length == 0 ? te("radial.done.failed_plain", "Did not work")
                : te("radial.done.failed", "{name} did not work").Replace("{name}", name, StringComparison.Ordinal);
        if (!string.IsNullOrWhiteSpace(keys) && BuiltIn.TryGetValue(keys.Trim(), out var b))
            return te("radial.done." + b.Key, b.English);
        return name.Length == 0 ? te("radial.done.plain", "Done")
            : te("radial.done.generic", "{name} done").Replace("{name}", name, StringComparison.Ordinal);
    }
}
