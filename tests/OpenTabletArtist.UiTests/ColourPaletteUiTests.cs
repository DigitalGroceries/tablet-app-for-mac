using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenTabletArtist.Controls;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>V29 #142 (Digital Groceries 19 Nov 18:50-19:35): the colour palette - a second radial menu on the same pen button (double press), an
/// artist's colour wheel; a tap sets the colour in Photoshop and copies its code everywhere.</summary>
public class ColourPaletteUiTests
{
    private static RadialMenuOverlay.MenuState Palette(long seq, RadialMenuOverlay.UsedSlice? used = null) =>
        new("open", seq, 600, 400, 50, 0, Enumerable.Repeat("", 8).ToList(), Enumerable.Repeat(false, 8).ToList(),
            Enumerable.Repeat(false, 8).ToList(), Used: used, Kind: "palette");

    private static void Pump() { for (int i = 0; i < 4; i++) Dispatcher.UIThread.RunJobs(); }

    [AvaloniaFact]
    public void ThePaletteTakesTheMenusPlace_AndATappedColourGoesToPhotoshop()
    {
        _ = new ThemeViewModel();
        var scripts = new List<string>();
        ColourPick.FrontmostBundleId = () => "com.adobe.Photoshop";
        ColourPick.RunAppleScript = scripts.Add;
        var overlay = new RadialMenuOverlay();
        try
        {
            overlay.ShowNowForTest(Palette(1));
            Pump();
            Assert.IsType<ColourPaletteWheel>(overlay.MenuWindow!.Content);
            overlay.ShowUsedIfNew(Palette(2, new RadialMenuOverlay.UsedSlice(1, "Red, pure", "#E32322", ColourPalette.Action, true)));
            Pump();
            var script = Assert.Single(scripts);
            Assert.Contains("tell application id \"com.adobe.Photoshop\"", script);
            Assert.Contains("c.rgb.hexValue = 'E32322'", script);
            Assert.True(overlay.Toast.IsVisible);
            Assert.Equal("#E32322 set in Photoshop", overlay.Toast.Text);

            ColourPick.FrontmostBundleId = () => "com.seriflabs.affinityphoto2";
            overlay.ShowUsedIfNew(Palette(3, new RadialMenuOverlay.UsedSlice(2, "Blue, dark", "#174E7A", ColourPalette.Action, true)));
            Pump();
            Assert.Single(scripts);                                       // no script for other apps
            Assert.Equal("#174E7A copied", overlay.Toast.Text);

            // Digital Groceries 19 Nov 20:21: the middle dot shows the last colour picked, and still does after the palette is opened again.
            var wheel = Assert.IsType<ColourPaletteWheel>(overlay.MenuWindow!.Content);
            Assert.Equal("#174E7A", wheel.LastColour);
            Assert.Equal("#174E7A", AppSettings.Get(ColourPalette.LastColourKey));
            overlay.ShowNowForTest(Palette(4));
            Pump();
            Assert.Equal("#174E7A", wheel.LastColour);
        }
        finally
        {
            overlay.Dispose();
            ColourPick.FrontmostBundleId = () => null;
        }
    }

    [AvaloniaFact]
    public void PhotoshopScript_SetsTheForegroundColour()
    {
        var s = ColourPick.PhotoshopScript("com.adobe.Photoshop", "#a1b2c3");
        Assert.Equal("tell application id \"com.adobe.Photoshop\" to do javascript \"var c = new SolidColor(); c.rgb.hexValue = 'A1B2C3'; app.foregroundColor = c;\"", s);
    }

    [AvaloniaFact]
    public void TheWheel_Draws48Swatches_Solid()
    {
        var wheel = new ColourPaletteWheel { Width = 324, Height = 324 };
        var w = new Window { Content = wheel, Width = 400, Height = 400 };
        w.Show();
        Pump();
        Assert.True(wheel.Bounds.Width > 300);
        Assert.Equal(1.0, wheel.Opacity);
        w.Close();
    }
}
