using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;

namespace OpenTabletArtist.Helpers;

/// <summary>Version 19 BETA 3: code-built dialogs take their colours from the app theme (Colors.axaml), so they
/// follow Light, Dark and System like the rest of the app, whatever the macOS appearance.</summary>
public static class ThemeRes
{
    public static T Use<T>(this T c, AvaloniaProperty property, string key) where T : Control
    {
        c[!property] = new DynamicResourceExtension(key);
        return c;
    }

    public static T Fg<T>(this T c, string key = "TextPrimaryBrush") where T : Control
        => c is TextBlock ? c.Use(TextBlock.ForegroundProperty, key) : c.Use(Avalonia.Controls.Primitives.TemplatedControl.ForegroundProperty, key);
}
