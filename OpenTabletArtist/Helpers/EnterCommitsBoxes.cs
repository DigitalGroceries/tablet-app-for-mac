using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace OpenTabletArtist.Helpers;

/// <summary>
/// V29 #168 (Digital Groceries 20 Nov ~09:50: "when I add a value and press enter the slider does not go to the type value until I click save").
/// The number boxes beside the sliders commit when focus leaves them (so a half-typed number isn't applied), and the manual says
/// "press Enter or click elsewhere"; but only a few boxes handled Enter themselves. Every single-line text box now commits its text
/// on Enter (Return), app-wide: the bound value is applied at once and the box shows what was applied, selected for the next try.
/// Boxes that handle Enter themselves and mark it Handled first, and multi-line boxes, are left alone.
/// </summary>
public static class EnterCommitsBoxes
{
    private static bool _installed;

    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        InputElement.KeyDownEvent.AddClassHandler<TextBox>(OnKeyDown, RoutingStrategies.Bubble);
    }

    internal static void OnKeyDown(TextBox box, KeyEventArgs e)
    {
        if (e.Handled || e.Key is not (Key.Enter or Key.Return) || box.AcceptsReturn) return;
        if (e.KeyModifiers != KeyModifiers.None) return;   // ⌘Enter etc. keep their own meaning
        var binding = BindingOperations.GetBindingExpressionBase(box, TextBox.TextProperty);
        if (binding == null) return;
        binding.UpdateSource();
        // The view model may have clamped or refused the text: show what it holds now.
        binding.UpdateTarget();
        box.SelectAll();
        // Not marked Handled: a box's own Enter handler (search, default button of a sheet) still runs as before.
    }
}
