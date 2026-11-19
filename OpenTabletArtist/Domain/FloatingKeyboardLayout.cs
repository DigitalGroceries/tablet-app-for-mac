using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTabletArtist.Domain;

/// <summary>One key on the floating keyboard: what it shows, the OTD key name it presses (or the modifier it toggles), and its
/// width in key units.</summary>
public sealed record FloatingKey(string Display, string Value, double Units, bool IsModifier = false);

/// <summary>
/// V29 #141 (Zak 19 Nov 18:44: "perhaps you can use the in app keyboard code to create a new and more compact one?"): the floating
/// keyboard's keys. The main block of the Pen button window's keyboard (same key names, same Mac modifiers), without the function
/// row, the navigation block and the number pad, so it stays small next to the user's work: 5 rows of 15 key units each.
/// Ctrl, Option, Cmd and Shift are toggles that apply to the next key only, like a phone keyboard's Shift.
/// </summary>
public static class FloatingKeyboardLayout
{
    /// <summary>Every row is this many key units wide, so the rows line up.</summary>
    public const double RowUnits = 15;

    public const string Ctrl = "Ctrl", Shift = "Shift", Alt = "Alt", Cmd = "Cmd";

    public static IReadOnlyList<IReadOnlyList<FloatingKey>> Rows { get; } = Build();

    private static IReadOnlyList<IReadOnlyList<FloatingKey>> Build()
    {
        static FloatingKey K(string d, string v, double u = 1) => new(d, v, u);
        static FloatingKey M(string d, string v, double u) => new(d, v, u, IsModifier: true);
        return new List<IReadOnlyList<FloatingKey>>
        {
            new[] { K("Esc", "Escape"), K("1", "D1"), K("2", "D2"), K("3", "D3"), K("4", "D4"), K("5", "D5"), K("6", "D6"),
                    K("7", "D7"), K("8", "D8"), K("9", "D9"), K("0", "D0"), K("-", "Minus"), K("=", "Equal"), K("⌫", "Backspace", 2) },
            new[] { K("Tab", "Tab", 1.5), K("Q", "Q"), K("W", "W"), K("E", "E"), K("R", "R"), K("T", "T"), K("Y", "Y"), K("U", "U"),
                    K("I", "I"), K("O", "O"), K("P", "P"), K("[", "LeftBracket"), K("]", "RightBracket"), K("\\", "Backslash", 1.5) },
            new[] { K("`", "Grave", 1.75), K("A", "A"), K("S", "S"), K("D", "D"), K("F", "F"), K("G", "G"), K("H", "H"), K("J", "J"),
                    K("K", "K"), K("L", "L"), K(";", "Semicolon"), K("'", "Quote"), K("Enter", "Enter", 2.25) },
            new[] { M("Shift", Shift, 2.25), K("Z", "Z"), K("X", "X"), K("C", "C"), K("V", "V"), K("B", "B"), K("N", "N"), K("M", "M"),
                    K(",", "Comma"), K(".", "Period"), K("/", "Slash"), K("↑", "Up"), K("Del", "Delete", 1.75) },
            new[] { M("Ctrl", Ctrl, 1.25), M("Option", Alt, 1.5), M("Cmd", Cmd, 1.25), K("Space", "Space", 8), K("←", "Left"),
                    K("↓", "Down"), K("→", "Right") },
        };
    }

    /// <summary>The OTD multi-key string for <paramref name="key"/> with the modifiers that are on (Cmd first, as the rest of the
    /// app writes it: "Application+Shift+Z").</summary>
    public static string KeysFor(string key, bool ctrl, bool shift, bool alt, bool cmd) =>
        AuxKeyBinding.FormatKeys(new AuxCombo(ctrl, shift, alt, key, cmd));
}
