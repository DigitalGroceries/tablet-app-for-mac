using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace OpenTabletArtist.Services;

/// <summary>
/// A small text bubble next to the radial menu (TASKS #64 / #65): the pencil's hint "Edit the slice you tap next" while editing is on, and the
/// short "Selection copied" after a slice runs. It sits just under the ring (above it when the ring is near the bottom of the screen), never takes
/// focus or clicks, and the hint stays until the edit is done or cancelled while a confirmation fades after about a second.
/// </summary>
internal sealed class RadialMenuToast
{
    private Window? _window;
    private Border? _box;
    private TextBlock? _text;
    private DispatcherTimer? _timer;

    /// <summary>True while the sticky hint (not a confirmation) is what is shown.</summary>
    public bool IsHint { get; private set; }
    public string? Text => _text?.Text;
    public bool IsVisible => _window?.IsVisible == true;
    internal Window? ToastWindow => _window;
    public static TimeSpan ConfirmationTime { get; set; } = TimeSpan.FromMilliseconds(1300);

    /// <summary>Show <paramref name="text"/> under the ring centred at (<paramref name="cx"/>, <paramref name="cy"/>) with outer radius
    /// <paramref name="radius"/>. <paramref name="hold"/> keeps it until <see cref="HideHint"/>; otherwise it hides itself.</summary>
    public void Show(string text, double cx, double cy, double radius, bool hold)
    {
        try
        {
            Ensure();
            _text!.Text = text;
            _box!.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = _box.DesiredSize;
            double w = Math.Ceiling(size.Width), h = Math.Ceiling(size.Height);
            _window!.Width = w; _window.Height = h;
            double x = cx - w / 2, y = cy + radius + 10;
            var screen = _window.Screens?.ScreenFromPoint(new PixelPoint((int)cx, (int)cy));
            if (screen != null)
            {
                var wa = screen.WorkingArea;
                if (y + h > wa.Bottom) y = cy - radius - 10 - h;           // no room under the ring: above it
                x = Math.Clamp(x, wa.X + 4, Math.Max(wa.X + 4, wa.Right - w - 4));
            }
            _window.Position = new PixelPoint((int)Math.Round(x), (int)Math.Round(y));
            IsHint = hold;
            _timer!.Stop();
            if (!hold) { _timer.Interval = ConfirmationTime; _timer.Start(); }
            if (!_window.IsVisible) _window.Show();
            if (_window.TryGetPlatformHandle() is Avalonia.Platform.IMacOSTopLevelPlatformHandle mac && mac.NSWindow != IntPtr.Zero)
                MacDesktop.MakeOverlay(mac.NSWindow, clickThrough: true);
        }
        catch (Exception e)
        {
            AppLog.Warn("Radial menu text bubble failed", e);
        }
    }

    /// <summary>Hide the sticky hint (editing finished or cancelled, or the menu closed); a confirmation that is fading is left alone.</summary>
    public void HideHint() { if (IsHint) Hide(); }

    public void Hide()
    {
        IsHint = false;
        _timer?.Stop();
        if (_window?.IsVisible == true) _window.Hide();
    }

    public void Dispose() { _timer?.Stop(); _window?.Close(); }

    private void Ensure()
    {
        if (_window != null) return;
        _text = new TextBlock
        {
            FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = UiColors.SoftWhiteBrush, TextAlignment = TextAlignment.Center,
            MaxWidth = 320, TextWrapping = TextWrapping.Wrap,
        };
        _box = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x1E, 0x1E, 0x1E)), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 8), Child = _text,
        };
        _window = new Window
        {
            WindowDecorations = WindowDecorations.None, SizeToContent = SizeToContent.Manual, Topmost = true, ShowActivated = false,
            ShowInTaskbar = false, CanResize = false, Background = Brushes.Transparent, Focusable = false, IsHitTestVisible = false,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent }, Content = _box, Title = "Radial menu message",
        };
        _timer = new DispatcherTimer();
        _timer.Tick += (_, _) => Hide();
    }
}
