using System;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>V29 #141 (Zak 19 Nov 18:46: "It will be good if the floating keyboard can be transparency adjusted"): the Keyboard
/// transparency slider and its number box in the radial menu settings. One setting for the whole app (the keyboard is the same for
/// every tablet); an open keyboard follows at once.</summary>
public partial class FloatingKeyboardLookViewModel : ObservableObject
{
    public string TransparencyCaption => Localizer.Te("keyboard.transparency", "Keyboard transparency");

    public int Transparency
    {
        get => FloatingKeyboard.Transparency;
        set
        {
            var v = Math.Clamp(value, FloatingKeyboard.MinTransparency, FloatingKeyboard.MaxTransparency);
            if (v != FloatingKeyboard.Transparency) FloatingKeyboard.Transparency = v;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TransparencyInput));
        }
    }

    /// <summary>The number box: "20", "20%" or "20,5" is read, clamped to 0-60 and applied; anything else puts the real value back.</summary>
    public string TransparencyInput
    {
        get => Transparency.ToString(System.Globalization.CultureInfo.InvariantCulture);
        set
        {
            if (RadialMenuEditorViewModel.TryParsePercent(value, out var v)) Transparency = v;
            OnPropertyChanged();
        }
    }
}
