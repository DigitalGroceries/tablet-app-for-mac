using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.SweepTests;

/// <summary>
/// The action-confirmation overlay ("Settings saved") is the ring-mode popup. This renders both through the
/// very same window code, side by side, so their size, style and position can be compared by eye, and checks
/// they really are the same size.
/// </summary>
public class ConfirmationOverlayShots
{
    static string Dir => System.Environment.GetEnvironmentVariable("OTA_SHOTS") ?? "/workspace/ota-confirm-shots";

    private static (string Path, double W, double H) Shoot(string file, System.Action<RingModeHud> show)
    {
        var hud = new RingModeHud();
        show(hud);
        Dispatcher.UIThread.RunJobs();
        var popup = hud.PopupWindow!;
        popup.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var path = $"{Dir}/{file}";
        popup.CaptureRenderedFrame()!.Save(path);
        var size = (popup.Width, popup.Height);
        hud.Dispose();
        return (path, size.Item1, size.Item2);
    }

    [AvaloniaFact]
    public void SettingsSaved_LooksLikeTheRingModePopup()
    {
        System.IO.Directory.CreateDirectory(Dir);
        var ring = Shoot("overlay-ring-mode-zoom.png", h => h.ShowNowForTest(2, 3, "Zoom"));
        var saved = Shoot("overlay-settings-saved.png",
            h => h.ShowNowForTest(0, 0, "Settings saved", ActionConfirmation.HeaderText(), "✓"));
        var factory = Shoot("overlay-factory-restored.png",
            h => h.ShowNowForTest(0, 0, "Factory settings restored", ActionConfirmation.HeaderText(), "✓"));

        Assert.True(System.IO.File.Exists(ring.Path));
        Assert.True(System.IO.File.Exists(saved.Path));
        Assert.True(System.IO.File.Exists(factory.Path));
        // Same window, same size: only the words differ.
        Assert.Equal(ring.W, saved.W);
        Assert.Equal(ring.H, saved.H);
        Assert.Equal(ring.W, factory.W);
        Assert.Equal(ring.H, factory.H);
    }
}
