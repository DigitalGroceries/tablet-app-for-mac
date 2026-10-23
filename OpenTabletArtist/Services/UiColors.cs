using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace OpenTabletArtist.Services;

/// <summary>Build R2: the app never paints pure white. Light marks on dark/coloured fills use this soft grey (#D8D8D8 since R6, the selection fill).</summary>
public static class UiColors
{
    public static readonly Color SoftWhite = Color.FromRgb(0xD8, 0xD8, 0xD8);
    public static readonly IBrush SoftWhiteBrush = new ImmutableSolidColorBrush(SoftWhite);
}
