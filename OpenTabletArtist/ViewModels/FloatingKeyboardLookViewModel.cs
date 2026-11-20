using System;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>V29 #141 (Zak 19 Nov 18:46: "It will be good if the floating keyboard can be transparency adjusted"): the Keyboard
/// transparency slider and its number box in the radial menu settings. One setting for the whole app (the keyboard is the same for
/// every tablet); an open keyboard follows at once.</summary>
public partial class FloatingKeyboardLookViewModel : ObservableObject
{
    public FloatingKeyboardLookViewModel()
    {
        // V29 #162: a corner drag on the keyboard (or a factory restore) moves the slider too.
        FloatingKeyboard.SizeChanged += () => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(Size)); OnPropertyChanged(nameof(SizeInput));
            OnPropertyChanged(nameof(Transparency)); OnPropertyChanged(nameof(TransparencyInput));
        });
    }

    // ── V29 #163 (Zak 20 Nov ~09:10): the Floating keyboard section of the Pen page ──
    public string Heading => Localizer.Te("keyboard.section", "Floating keyboard");
    public string Intro => Localizer.Te("keyboard.section_intro",
        "A small keyboard that floats over your work, so you can type with the pen. Drag its top bar to move it, and its bottom-right corner to make it bigger or smaller.");
    public string SliceCaption => Localizer.Te("keyboard.slice", "Radial menu slice");
    public string PutLabel => Localizer.Te("radial.keyboard_put", "Put the keyboard there");
    public string SizeCaption => Localizer.Te("keyboard.size", "Keyboard size");
    public double MinSize => FloatingKeyboard.MinSize;
    public double MaxSize => FloatingKeyboard.MaxSize;

    /// <summary>V29 #162: the keyboard's size, 60 to 300%; an open keyboard follows at once.</summary>
    public int Size
    {
        get => FloatingKeyboard.SizePercent;
        set
        {
            var v = Math.Clamp(value, FloatingKeyboard.MinSize, FloatingKeyboard.MaxSize);
            if (v != FloatingKeyboard.SizePercent) FloatingKeyboard.SizePercent = v;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SizeInput));
        }
    }

    /// <summary>The size box: "150", "150%" or "150,5" is read, clamped to 60-300 and applied; anything else puts the real value back.</summary>
    public string SizeInput
    {
        get => Size.ToString(System.Globalization.CultureInfo.InvariantCulture);
        set
        {
            if (TypedValue.TryParse(value?.TrimEnd('%', ' '), FloatingKeyboard.MinSize, FloatingKeyboard.MaxSize, 0, out var v)) Size = (int)v;
            OnPropertyChanged();
        }
    }

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
