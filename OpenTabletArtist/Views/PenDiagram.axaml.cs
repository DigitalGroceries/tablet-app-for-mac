using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace OpenTabletArtist.Views;

/// <summary>Build R5: upright pen drawing with numbered side buttons (black numbers on light badges).
/// R16: <see cref="Flipped"/> rotates the drawing 180° for the Eraser tab; the number badges stay upright
/// and follow their physical buttons (a point at y on the barrel lands at 360 − y).</summary>
public partial class PenDiagram : UserControl
{
    public static readonly StyledProperty<bool> FlippedProperty =
        AvaloniaProperty.Register<PenDiagram, bool>(nameof(Flipped));

    public bool Flipped
    {
        get => GetValue(FlippedProperty);
        set => SetValue(FlippedProperty, value);
    }

    // Upright badge tops (button 3, 2, 1); the badges are 26 tall and 24 wide on a 64 × 360 canvas.
    private static readonly double[] UprightTops = { 140, 172, 204 };
    private const double CanvasHeight = 360, CanvasWidth = 64, BadgeHeight = 26, BadgeWidth = 24, UprightLeft = 20;

    public PenDiagram()
    {
        InitializeComponent();
        Apply();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FlippedProperty) Apply();
    }

    /// <summary>Badge top for a button (3, 2, 1) in the current orientation.</summary>
    public static double BadgeTop(int button, bool flipped)
    {
        var top = UprightTops[3 - button];
        return flipped ? CanvasHeight - top - BadgeHeight : top;
    }

    private void Apply()
    {
        if (Shapes is null) return;
        Shapes.RenderTransform = Flipped ? new RotateTransform(180) : null;
        var left = Flipped ? CanvasWidth - UprightLeft - BadgeWidth : UprightLeft;
        foreach (var (badge, n) in new[] { (Badge1, 1), (Badge2, 2), (Badge3, 3) })
        {
            Canvas.SetTop(badge, BadgeTop(n, Flipped));
            Canvas.SetLeft(badge, left);
        }
    }
}
