using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>v27 (Zak 13 Nov 15:52): pressing Save showed "Settings saved" centred over the Claude window (the last other
/// app in front). A confirmation for one of our own buttons is centred on our own window; ring-mode popups still follow
/// the frontmost app (Photoshop).</summary>
public class ConfirmationOwnWindowTests
{
    [AvaloniaFact]
    public void ButtonConfirmation_IsCentredOnTheAppsOwnWindow_AndSaysSoInTheLog()
    {
        var lines = new List<string>();
        void H(string l) { lock (lines) lines.Add(l); }
        var hud = new RingModeHud();
        RingModeHud.OwnWindowBounds = () => new Rect(300, 200, 1100, 720);
        AppLog.LineWritten += H;
        try
        {
            hud.ShowNowForTest(0, 0, "Settings saved", ActionConfirmation.HeaderText(), "✓", overOwnWindow: true);
            Dispatcher.UIThread.RunJobs();
            var w = hud.PopupWindow!;
            Assert.InRange(w.Position.X + w.Width / 2, 300 + 550 - 2, 300 + 550 + 2);
            Assert.InRange(w.Position.Y + w.Height / 2, 200 + 360 - 2, 200 + 360 + 2);
            lock (lines) Assert.Contains(lines, l => l.Contains("Confirmation popup") && l.Contains("the app's own window"));
        }
        finally { AppLog.LineWritten -= H; RingModeHud.OwnWindowBounds = null; hud.Dispose(); }
    }

    [AvaloniaFact]
    public void Popups_are_45_percent_and_a_confirmation_stays_4_5_seconds()
    {
        // V30 build 3 (Zak 24 Nov, L6.4): "Make all see through 5% more opaque"; the 'Saved' overlay was too fast to read.
        Assert.Equal(0.45, RingModeHud.ContentOpacity, 3);
        var hud = new RingModeHud();
        RingModeHud.OwnWindowBounds = () => new Rect(300, 200, 1100, 720);
        try
        {
            hud.ShowNowForTest(0, 0, "Settings saved", ActionConfirmation.HeaderText(), "✓", overOwnWindow: true);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(TimeSpan.FromMilliseconds(4500), hud.LastShownFor);
            Assert.Equal(0.45, ((Avalonia.Controls.Control)hud.PopupWindow!.Content!).Opacity, 3);
            hud.ShowNowForTest(2, 4, "Brush size");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(TimeSpan.FromMilliseconds(2500), hud.LastShownFor);   // the touch ring popup keeps 2.5 s
        }
        finally { RingModeHud.OwnWindowBounds = null; hud.Dispose(); }
    }

    [AvaloniaFact]
    public void ButtonConfirmation_WithNoOwnWindowUp_StillShows_OnTheDisplay()
    {
        var hud = new RingModeHud();
        RingModeHud.OwnWindowBounds = () => null;
        try
        {
            hud.ShowNowForTest(0, 0, "Settings saved", ActionConfirmation.HeaderText(), "✓", overOwnWindow: true);
            Dispatcher.UIThread.RunJobs();
            Assert.True(hud.PopupWindow!.IsVisible);
        }
        finally { RingModeHud.OwnWindowBounds = null; hud.Dispose(); }
    }

    [Fact]
    public void TheApp_WiresItsOwnWindowForConfirmations()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir, "OpenTabletArtist.slnx"))) dir = System.IO.Path.GetDirectoryName(dir);
        var app = System.IO.File.ReadAllText(System.IO.Path.Combine(dir!, "OpenTabletArtist", "App.axaml.cs"));
        Assert.Contains("RingModeHud.OwnWindowBounds =", app);
        Assert.Contains("candidate.IsActive", app);   // the active dialog first, then the main window
    }
}
