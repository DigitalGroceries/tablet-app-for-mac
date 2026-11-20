using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTabletArtist.Domain;

/// <summary>One key on the floating keyboard: what it shows (the US label, used when the Mac's layout can't be read), the OTD key
/// name it presses (or the modifier it toggles), and its width in key units.</summary>
public sealed record FloatingKey(string Display, string Value, double Units, bool IsModifier = false);

/// <summary>V29 #171: the two key shapes Mac keyboards come in. ISO (British and most of Europe): a § key left of 1, an extra key
/// left of Z and a tall Enter. ANSI (US): ` left of 1 and a wide Enter.</summary>
public enum KeyboardShape { Ansi, Iso }

/// <summary>V29 #171: what each key types in the Mac's selected keyboard layout (British, Bulgarian, US, …), read from the Mac.</summary>
public interface IKeyLabels
{
    /// <summary>The input source's id, e.g. "com.apple.keylayout.British" (to see when the user switches).</summary>
    string Id { get; }
    /// <summary>Its name as the Mac's menu shows it, e.g. "British".</summary>
    string Name { get; }
    KeyboardShape Shape { get; }
    /// <summary>What the key with this Mac key code types without and with Shift, or null when unknown.</summary>
    (string Normal, string Shifted)? For(int macKeyCode);
}

/// <summary>
/// V29 #141 (Zak 19 Nov 18:44: "perhaps you can use the in app keyboard code to create a new and more compact one?"): the floating
/// keyboard's keys: the main block of a Mac keyboard, without the function row, so it stays small next to the user's work: 5 rows of
/// 15 key units each. Ctrl, Option, Cmd and Shift are toggles that apply to the next key only, like a phone keyboard's Shift; Caps
/// Lock stays on until tapped again.
/// <para>V29 #171 (Zak 20 Nov, build 7: capitals shown but small letters typed, no Caps Lock, Del where Shift is; "The floating
/// keyboard should check what location keyboard the mac is using ... and show the same input"): the rows follow the shape of the
/// Mac's keyboard (ISO or ANSI) and every key shows what the selected layout types (lower case, and the Shift symbol when Shift or
/// Caps Lock is on), like the Mac's own Keyboard Viewer. The keys are sent as key positions, so the Mac types what the label shows.</para>
/// </summary>
public static class FloatingKeyboardLayout
{
    /// <summary>Every row is this many key units wide, so the rows line up.</summary>
    public const double RowUnits = 15;

    public const string Ctrl = "Ctrl", Shift = "Shift", Alt = "Alt", Cmd = "Cmd";

    /// <summary>Caps Lock, where a Mac keyboard has it. It stays on until tapped again and makes letters capitals (the keyboard's own
    /// lock: it never changes the Mac's Caps Lock light).</summary>
    public const string CapsLock = "Caps";

    /// <summary>The ISO § key's name for the driver (kVK_ISO_Section, added to the driver's macOS keys for V29 #171).</summary>
    public const string Section = "Section";

    /// <summary>The US-shaped rows (ANSI): what the keyboard showed before V29 #171, and its shape when the Mac can't be asked.</summary>
    public static IReadOnlyList<IReadOnlyList<FloatingKey>> Rows => RowsFor(KeyboardShape.Ansi);

    private static readonly IReadOnlyList<IReadOnlyList<FloatingKey>> AnsiRows = Build(KeyboardShape.Ansi);
    private static readonly IReadOnlyList<IReadOnlyList<FloatingKey>> IsoRows = Build(KeyboardShape.Iso);

    public static IReadOnlyList<IReadOnlyList<FloatingKey>> RowsFor(KeyboardShape shape) => shape == KeyboardShape.Iso ? IsoRows : AnsiRows;

    private static IReadOnlyList<IReadOnlyList<FloatingKey>> Build(KeyboardShape shape)
    {
        static FloatingKey K(string d, string v, double u = 1) => new(d, v, u);
        static FloatingKey M(string d, string v, double u) => new(d, v, u, IsModifier: true);
        bool iso = shape == KeyboardShape.Iso;
        var digits = new[] { K("1", "D1"), K("2", "D2"), K("3", "D3"), K("4", "D4"), K("5", "D5"), K("6", "D6"), K("7", "D7"),
                             K("8", "D8"), K("9", "D9"), K("0", "D0"), K("-", "Minus"), K("=", "Equal") };
        var top = new[] { K("Q", "Q"), K("W", "W"), K("E", "E"), K("R", "R"), K("T", "T"), K("Y", "Y"), K("U", "U"), K("I", "I"),
                          K("O", "O"), K("P", "P"), K("[", "LeftBracket"), K("]", "RightBracket") };
        var home = new[] { K("A", "A"), K("S", "S"), K("D", "D"), K("F", "F"), K("G", "G"), K("H", "H"), K("J", "J"), K("K", "K"),
                           K("L", "L"), K(";", "Semicolon"), K("'", "Quote") };
        var bottom = new[] { K("Z", "Z"), K("X", "X"), K("C", "C"), K("V", "V"), K("B", "B"), K("N", "N"), K("M", "M"),
                             K(",", "Comma"), K(".", "Period"), K("/", "Slash") };
        var rows = new List<IReadOnlyList<FloatingKey>>();
        // Row 1: ` (ANSI) or § (ISO) left of 1, then the digits and ⌫ (the Mac's delete key).
        rows.Add(new[] { iso ? K("§", Section) : K("`", "Grave") }.Concat(digits).Append(K("⌫", "Backspace", 2)).ToArray());
        // Row 2: ISO has the top of its tall Enter where ANSI has \.
        rows.Add(new[] { K("Tab", "Tab", 1.5) }.Concat(top).Append(iso ? K("Enter", "Enter", 1.5) : K("\\", "Backslash", 1.5)).ToArray());
        // Row 3: ISO has \ next to ' and the lower part of Enter.
        rows.Add(new[] { M("Caps", CapsLock, 1.75) }.Concat(home)
            .Concat(iso ? new[] { K("\\", "Backslash"), K("Enter", "Enter", 1.25) } : new[] { K("Enter", "Enter", 2.25) }).ToArray());
        // Row 4: ISO has an extra key (` on a British Mac) left of Z; a Shift at each end, as on a Mac.
        rows.Add((iso ? new[] { M("Shift", Shift, 1.25), K("`", "Grave") } : new[] { M("Shift", Shift, 2.25) })
            .Concat(bottom).Append(M("Shift", Shift, 2.75)).ToArray());
        // Row 5: Esc, the Mac's modifiers, Space and the arrows together, as on a Mac.
        rows.Add(new[] { K("Esc", "Escape"), M("Ctrl", Ctrl, 1.25), M("Option", Alt, 1.25), M("Cmd", Cmd, 1.5), K("Space", "Space", 6),
                         K("←", "Left"), K("↑", "Up"), K("↓", "Down"), K("→", "Right") });
        return rows;
    }

    /// <summary>The Mac key code (kVK_*) of each key that types a character, so its label can be read from the selected layout.</summary>
    public static readonly IReadOnlyDictionary<string, int> MacKeyCodes = new Dictionary<string, int>
    {
        ["A"] = 0x00, ["S"] = 0x01, ["D"] = 0x02, ["F"] = 0x03, ["H"] = 0x04, ["G"] = 0x05, ["Z"] = 0x06, ["X"] = 0x07,
        ["C"] = 0x08, ["V"] = 0x09, [Section] = 0x0A, ["B"] = 0x0B, ["Q"] = 0x0C, ["W"] = 0x0D, ["E"] = 0x0E, ["R"] = 0x0F,
        ["Y"] = 0x10, ["T"] = 0x11, ["D1"] = 0x12, ["D2"] = 0x13, ["D3"] = 0x14, ["D4"] = 0x15, ["D6"] = 0x16, ["D5"] = 0x17,
        ["Equal"] = 0x18, ["D9"] = 0x19, ["D7"] = 0x1A, ["Minus"] = 0x1B, ["D8"] = 0x1C, ["D0"] = 0x1D, ["RightBracket"] = 0x1E,
        ["O"] = 0x1F, ["U"] = 0x20, ["LeftBracket"] = 0x21, ["I"] = 0x22, ["P"] = 0x23, ["L"] = 0x25, ["J"] = 0x26,
        ["Quote"] = 0x27, ["K"] = 0x28, ["Semicolon"] = 0x29, ["Backslash"] = 0x2A, ["Comma"] = 0x2B, ["Slash"] = 0x2C,
        ["N"] = 0x2D, ["M"] = 0x2E, ["Period"] = 0x2F, ["Grave"] = 0x32,
    };

    /// <summary>What the key types in the selected layout (null when the layout is unknown or the key types no character).</summary>
    public static (string Normal, string Shifted)? Typed(FloatingKey key, IKeyLabels? labels) =>
        labels != null && !key.IsModifier && MacKeyCodes.TryGetValue(key.Value, out var code) && labels.For(code) is { } l
        && l.Normal.Length > 0 ? l : null;

    /// <summary>A letter key in the selected layout (Latin, Cyrillic, …): Caps Lock makes it a capital. Without a layout: A-Z.</summary>
    public static bool IsLetter(FloatingKey key, IKeyLabels? labels = null)
    {
        if (key.IsModifier) return false;
        // Grok's review 20 Nov (2): a letter key whose Shift gives something else (German ß, French é/è/ç/à on the digit row) is
        // not a letter for Caps Lock: Caps would type that other thing, not a capital.
        if (Typed(key, labels) is { } t)
            return t.Normal.Length == 1 && char.IsLetter(t.Normal[0])
                && string.Equals(t.Shifted, t.Normal.ToUpperInvariant(), StringComparison.Ordinal);
        return key.Value.Length == 1 && key.Value[0] is >= 'A' and <= 'Z';
    }

    /// <summary>What the key shows: what the selected layout types (Zak 20 Nov: "show the same input"), small letters unless Shift
    /// or Caps Lock is on, the Shift symbol while Shift is on. Without a layout: the US label, letters following Shift / Caps Lock.</summary>
    public static string LabelFor(FloatingKey key, bool shift, bool capsLock, IKeyLabels? labels = null)
    {
        if (key.IsModifier) return key.Display;
        if (Typed(key, labels) is { } t)
        {
            if (shift && t.Shifted.Length > 0) return t.Shifted;
            return capsLock && IsLetter(key, labels) ? t.Shifted : t.Normal;      // Caps sends Shift: show what Shift types
        }
        if (!IsLetter(key)) return key.Display;
        return shift || capsLock ? key.Display.ToUpperInvariant() : key.Display.ToLowerInvariant();
    }

    /// <summary>The OTD multi-key string for <paramref name="key"/> with the modifiers that are on (Cmd first, as the rest of the
    /// app writes it: "Application+Shift+Z").</summary>
    public static string KeysFor(string key, bool ctrl, bool shift, bool alt, bool cmd) =>
        AuxKeyBinding.FormatKeys(new AuxCombo(ctrl, shift, alt, key, cmd));
}
