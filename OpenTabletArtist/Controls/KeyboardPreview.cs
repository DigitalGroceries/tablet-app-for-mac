using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.Controls;

/// <summary>
/// V29 #163 (Zak 20 Nov, build 7: "The keyboard should be shown in that menu the way the radial menu is shown on the top part of the
/// app page. I gave you a screenshot on where it needs to be"): a picture of the floating keyboard in its section on the Pen page,
/// like the radial menu's picture: its real size (the Keyboard size slider; shrunk only to fit the page), its transparency, and the
/// keys of the Mac's selected layout (#171), read again every second while the page shows it, so switching British / Bulgarian
/// redraws it. Drawn, not built from controls, so it can't be tapped by mistake.
/// </summary>
public sealed class KeyboardPreview : Control
{
    public static readonly StyledProperty<double> SizePercentProperty =
        AvaloniaProperty.Register<KeyboardPreview, double>(nameof(SizePercent), FloatingKeyboard.DefaultSize);
    public static readonly StyledProperty<double> TransparencyProperty =
        AvaloniaProperty.Register<KeyboardPreview, double>(nameof(Transparency), FloatingKeyboard.DefaultTransparency);

    public double SizePercent { get => GetValue(SizePercentProperty); set => SetValue(SizePercentProperty, value); }
    public double Transparency { get => GetValue(TransparencyProperty); set => SetValue(TransparencyProperty, value); }

    static KeyboardPreview()
    {
        AffectsMeasure<KeyboardPreview>(SizePercentProperty);
        AffectsRender<KeyboardPreview>(SizePercentProperty, TransparencyProperty);
    }

    private IKeyLabels? _labels;
    private (string? Id, KeyboardShape Shape)? _shown;
    private DispatcherTimer? _timer;

    /// <summary>The layout the picture shows (tests).</summary>
    internal IKeyLabels? Layout => _labels;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        RefreshLayout();
        _timer ??= new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => { if (IsEffectivelyVisible) RefreshLayout(); });
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer?.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    internal void RefreshLayout()
    {
        IKeyLabels? labels;
        try { labels = FloatingKeyboard.LayoutReader(); }
        catch (Exception) { labels = null; }
        var now = (labels?.Id, labels?.Shape ?? KeyboardShape.Ansi);
        if (_shown == now) return;
        _shown = now;
        _labels = labels;
        InvalidateVisual();
    }

    private double Scale(Size available)
    {
        var (w, _) = FloatingKeyboard.BaseSize();
        double k = Math.Clamp(SizePercent, FloatingKeyboard.MinSize, FloatingKeyboard.MaxSize) / 100.0;
        if (!double.IsInfinity(available.Width) && available.Width > 0 && w * k > available.Width) k = available.Width / w;
        return k;
    }

    private double _k = 1;

    protected override Size MeasureOverride(Size availableSize)
    {
        _k = Scale(availableSize);
        var (w, h) = FloatingKeyboard.BaseSize();
        return new Size(Math.Round(w * _k), Math.Round(h * _k));
    }

    private static readonly IBrush PanelBrush = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.Parse("#1E1E1E"));
    private static readonly IPen PanelPen = new Avalonia.Media.Immutable.ImmutablePen(new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.Parse("#4A4A4A")), 1);
    private static readonly IBrush KeyBrush = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.Parse("#3A3A3A"));
    private static readonly IBrush KeyText = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.Parse("#E4E4E4"));

    public override void Render(DrawingContext context)
    {
        var (w, h) = FloatingKeyboard.BaseSize();
        var shape = _labels?.Shape ?? KeyboardShape.Ansi;
        using (context.PushOpacity(RadialMenuDef.OpacityFor((int)Math.Round(Transparency))))
        using (context.PushTransform(Matrix.CreateScale(_k, _k)))
        {
            context.DrawRectangle(PanelBrush, PanelPen, new RoundedRect(new Rect(0.5, 0.5, w - 1, h - 1), 10));
            double pad = FloatingKeyboard.Pad, gap = FloatingKeyboard.Gap;
            var title = _labels == null ? Localizer.Te("keyboard.title", "Keyboard")
                : $"{Localizer.Te("keyboard.title", "Keyboard")} · {_labels.Name}";
            DrawText(context, "⠿  " + title, 12, Brushes.Gainsboro, new Rect(pad + 2, pad, w - 2 * pad - 30, FloatingKeyboard.BarHeight), center: false);
            var close = new Rect(w - pad - FloatingKeyboard.BarHeight, pad, FloatingKeyboard.BarHeight, FloatingKeyboard.BarHeight);
            context.DrawRectangle(KeyBrush, null, new RoundedRect(close, 4));
            DrawText(context, "×", 15, KeyText, close, center: true);
            double y = pad + FloatingKeyboard.BarHeight + gap;
            foreach (var row in FloatingKeyboardLayout.RowsFor(shape))
            {
                double x = pad;
                foreach (var key in row)
                {
                    var r = new Rect(x, y, key.Units * FloatingKeyboard.Unit - gap, FloatingKeyboard.KeyHeight - gap);
                    context.DrawRectangle(KeyBrush, null, new RoundedRect(r, 5));
                    var label = FloatingKeyboardLayout.LabelFor(key, shift: false, capsLock: false, _labels);
                    DrawText(context, label, label.Length > 2 ? 11 : 13, KeyText, r, center: true);
                    x += key.Units * FloatingKeyboard.Unit;
                }
                y += FloatingKeyboard.KeyHeight;
            }
        }
    }

    private static void DrawText(DrawingContext context, string text, double size, IBrush brush, Rect box, bool center)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, size, brush);
        double x = center ? box.X + (box.Width - ft.Width) / 2 : box.X;
        double y = box.Y + (box.Height - ft.Height) / 2;
        context.DrawText(ft, new Point(x, y));
    }
}
