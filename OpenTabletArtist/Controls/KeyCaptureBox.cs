using System;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Controls;

/// <summary>
/// V29 #158 (Digital Groceries 20 Nov: keyboard shortcuts are set in place, with no extra window): a box that shows a shortcut and, while it
/// has the keyboard focus, takes the next key pressed with its modifiers (⌃ ⌥ ⇧ ⌘) as the new shortcut. Backspace or Delete
/// on its own empties it; Escape leaves it as it was. The new combo goes to <see cref="Command"/> (an <see cref="AuxCombo"/>,
/// <see cref="AuxCombo.Unbound"/> for empty). Keys the Mac keeps for itself (for example ⌘Q) never reach an app's window.
/// </summary>
public class KeyCaptureBox : TextBox
{
    public static readonly StyledProperty<ICommand?> CommandProperty =
        AvaloniaProperty.Register<KeyCaptureBox, ICommand?>(nameof(Command));

    public ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(TextBox);

    public KeyCaptureBox()
    {
        IsReadOnly = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab && e.KeyModifiers == KeyModifiers.None) return;   // moves to the next control as usual
        e.Handled = true;
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            TopLevel.GetTopLevel(this)?.Focus();   // leave the box; the shortcut stays as it was
            return;
        }
        if (Capture(e.Key, e.KeyModifiers) is { } combo && Command?.CanExecute(combo) == true) Command.Execute(combo);
    }

    /// <summary>The combo for a key press, <see cref="AuxCombo.Unbound"/> for Backspace / Delete alone, or null for a key that
    /// can't be sent (a modifier on its own, or a key the driver has no name for).</summary>
    public static AuxCombo? Capture(Key key, KeyModifiers modifiers)
    {
        if ((key is Key.Back or Key.Delete) && modifiers == KeyModifiers.None) return AuxCombo.Unbound;
        if (KeyName(key) is not { } name) return null;
        return new AuxCombo(modifiers.HasFlag(KeyModifiers.Control), modifiers.HasFlag(KeyModifiers.Shift),
            modifiers.HasFlag(KeyModifiers.Alt), name, modifiers.HasFlag(KeyModifiers.Meta));
    }

    /// <summary>The OpenTabletDriver (macOS) name of a key, or null for a modifier or a key it can't send.</summary>
    public static string? KeyName(Key key)
    {
        if (key >= Key.A && key <= Key.Z) return key.ToString();
        if (key >= Key.D0 && key <= Key.D9) return key.ToString();
        if (key >= Key.F1 && key <= Key.F15) return key.ToString();
        if (key >= Key.NumPad0 && key <= Key.NumPad9) return "Keypad" + (key - Key.NumPad0);
        return key switch
        {
            Key.OemMinus => "Minus",
            Key.OemPlus => "Equal",
            Key.OemOpenBrackets => "LeftBracket",
            Key.OemCloseBrackets => "RightBracket",
            Key.OemSemicolon => "Semicolon",
            Key.OemQuotes => "Quote",
            Key.OemComma => "Comma",
            Key.OemPeriod => "Period",
            Key.OemQuestion => "Slash",
            Key.OemPipe or Key.OemBackslash => "Backslash",
            Key.OemTilde => "Grave",
            Key.Up => "Up",
            Key.Down => "Down",
            Key.Left => "Left",
            Key.Right => "Right",
            Key.Space => "Space",
            Key.Enter => "Enter",
            Key.Tab => "Tab",
            Key.Escape => "Escape",
            Key.Back => "Backspace",
            Key.Delete => "Delete",
            Key.Home => "Home",
            Key.End => "End",
            Key.PageUp => "PageUp",
            Key.PageDown => "PageDown",
            Key.Insert => "Insert",
            Key.Multiply => "Multiply",
            Key.Add => "Add",
            Key.Divide => "Divide",
            Key.Decimal => "Decimal",
            _ => null,
        };
    }
}
