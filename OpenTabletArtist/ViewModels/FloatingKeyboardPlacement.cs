using System;
using System.Collections.Generic;
using System.Linq;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// Which radial menu slice holds the floating keyboard, and putting it on one. V29 #143 had this in the pen button's Change… window
/// (Radial menu tab); V29 #163 (Zak 20 Nov ~09:10: "Move the floating keyboard menu to where it is in the screenshot", "Put the
/// keyboard settings menu and how it is triggered here") moved it to the Floating keyboard section of the Pen page.
/// </summary>
public static class FloatingKeyboardPlacement
{
    /// <summary>The 8 top slices by name ("3 · Copy", "8 · (empty)").</summary>
    public static IReadOnlyList<string> Slots(string? menuJson)
    {
        var m = RadialMenuJson.Parse(menuJson);
        var empty = Localizer.Te("radial.slot_empty", "(empty)");
        return m.Slices.Select((s, i) => $"{i + 1} · {(s.IsSet ? (string.IsNullOrWhiteSpace(s.Label) ? s.Action : s.Label) : empty)}").ToList();
    }

    /// <summary>The slice (0-based) the keyboard is on, or -1.</summary>
    public static int OnSlice(string? menuJson) =>
        RadialMenuJson.Parse(menuJson).Slices.FindIndex(s => s.Action == RadialActions.Keyboard);

    /// <summary>The slice offered first: where the keyboard is, else the first empty slice, else the last one.</summary>
    public static int SuggestedSlot(string? menuJson)
    {
        var m = RadialMenuJson.Parse(menuJson);
        int on = m.Slices.FindIndex(s => s.Action == RadialActions.Keyboard);
        int firstEmpty = m.Slices.FindIndex(s => !s.IsSet);
        return on >= 0 ? on : firstEmpty >= 0 ? firstEmpty : m.Slices.Count - 1;
    }

    /// <summary>"The keyboard is on slice 7." when it is on one, else "".</summary>
    public static string Status(string? menuJson)
    {
        int on = OnSlice(menuJson);
        return on >= 0
            ? Localizer.Te("radial.keyboard_on", "The keyboard is on slice {n}.")
                .Replace("{n}", (on + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            : "";
    }

    /// <summary>
    /// V29 (Zak 20 Nov, build 7: "The button 'Put the keyboard there' doesnt do anything and is confusing"): the slice menu is the
    /// control itself. Its choices: "Not on the radial menu", the slice the keyboard is on, and the empty slices (a slice that does
    /// something else is never overwritten from here). Each choice's slice, 0-based, or -1 for "Not on the radial menu".
    /// </summary>
    public static IReadOnlyList<(string Label, int Slot)> Choices(string? menuJson)
    {
        var m = RadialMenuJson.Parse(menuJson);
        var list = new List<(string, int)> { (Localizer.Te("keyboard.not_on_menu", "Not on the radial menu"), -1) };
        var empty = Localizer.Te("radial.slot_empty", "(empty)");
        for (int i = 0; i < m.Slices.Count; i++)
        {
            var s = m.Slices[i];
            if (s.Action == RadialActions.Keyboard)
                list.Add(($"{i + 1} · {(string.IsNullOrWhiteSpace(s.Label) ? Localizer.Te("keyboard.title", "Keyboard") : s.Label)}", i));
            else if (!s.IsSet) list.Add(($"{i + 1} · {empty}", i));
        }
        return list;
    }

    /// <summary>The choice that shows where the keyboard is now (0 = not on the menu).</summary>
    public static int ChoiceNow(string? menuJson)
    {
        int on = OnSlice(menuJson);
        if (on < 0) return 0;
        var choices = Choices(menuJson);
        for (int i = 0; i < choices.Count; i++) if (choices[i].Slot == on) return i;
        return 0;
    }

    /// <summary>The line under the menu: where the keyboard is, or that every slice is taken.</summary>
    public static string Note(string? menuJson)
    {
        var status = Status(menuJson);
        if (status.Length > 0) return status;
        return Choices(menuJson).Count <= 1
            ? Localizer.Te("keyboard.no_free_slice", "Every slice of the radial menu is in use. To put the keyboard on one, first empty a slice: click the radial menu picture at the top of the page.")
            : "";
    }

    /// <summary>The menu with the keyboard on no slice (its slice is emptied).</summary>
    public static string Remove(string? menuJson)
    {
        var m = RadialMenuJson.Parse(menuJson);
        foreach (var s in m.Slices.Where(s => s.Action == RadialActions.Keyboard)) { s.Action = RadialActions.None; s.Label = ""; }
        return RadialMenuJson.Serialize(m);
    }

    /// <summary>The menu with the keyboard on <paramref name="slot"/> (0-based) and on no other slice.</summary>
    public static string Put(string? menuJson, int slot, out string replaced)
    {
        var m = RadialMenuJson.Parse(menuJson);
        int i = Math.Clamp(slot, 0, m.Slices.Count - 1);
        foreach (var s in m.Slices.Where(s => s.Action == RadialActions.Keyboard)) { s.Action = RadialActions.None; s.Label = ""; }
        replaced = m.Slices[i].IsSet ? m.Slices[i].Label ?? "" : "";
        m.Slices[i] = new RadialSlice { Label = Localizer.Te("keyboard.title", "Keyboard"), Action = RadialActions.Keyboard };
        return RadialMenuJson.Serialize(m);
    }
}
