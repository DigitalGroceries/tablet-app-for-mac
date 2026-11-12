using Avalonia.Input;

namespace OpenTabletArtist.Domain;

/// <summary>What a key shortcut means on the Scribble page (TASKS #53). The radial menu's slices send ordinary
/// key shortcuts (⌘Z, ⌘⇧Z, ⌘C, ⌘V, ⌘X, ⌘A, ⌘-, ⌘=) to the app in front, so when this app's Scribble page is in
/// front it just has to understand those keys. That also gives keyboard users undo and redo.</summary>
public enum ScribbleShortcut { None, Undo, Redo, Copy, Paste, Cut, SelectAll, ZoomIn, ZoomOut }

public static class ScribbleShortcuts
{
    /// <summary>The shortcut for <paramref name="key"/> + <paramref name="mods"/>. The main key is ⌘ on a Mac and Ctrl
    /// elsewhere (<paramref name="mac"/>); Alt (Option) or the other main key makes it something else, so None.</summary>
    public static ScribbleShortcut Classify(Key key, KeyModifiers mods, bool mac)
    {
        var main = mac ? KeyModifiers.Meta : KeyModifiers.Control;
        var other = mac ? KeyModifiers.Control : KeyModifiers.Meta;
        if ((mods & main) == 0 || (mods & (KeyModifiers.Alt | other)) != 0) return ScribbleShortcut.None;
        bool shift = (mods & KeyModifiers.Shift) != 0;
        switch (key)
        {
            case Key.Z: return shift ? ScribbleShortcut.Redo : ScribbleShortcut.Undo;
            case Key.Y: return shift ? ScribbleShortcut.None : ScribbleShortcut.Redo;   // Windows habit
            case Key.C: return shift ? ScribbleShortcut.None : ScribbleShortcut.Copy;
            case Key.V: return shift ? ScribbleShortcut.None : ScribbleShortcut.Paste;
            case Key.X: return shift ? ScribbleShortcut.None : ScribbleShortcut.Cut;
            case Key.A: return shift ? ScribbleShortcut.None : ScribbleShortcut.SelectAll;
            // Shift is how you type "+" on many keyboards, so it doesn't matter for zoom.
            case Key.OemPlus: case Key.Add: return ScribbleShortcut.ZoomIn;
            case Key.OemMinus: case Key.Subtract: return ScribbleShortcut.ZoomOut;
            default: return ScribbleShortcut.None;
        }
    }
}
