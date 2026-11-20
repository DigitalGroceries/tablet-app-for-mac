using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>V29 #141 (Zak 19 Nov 2026): the radial menu's Keyboard slice shows and hides a compact floating keyboard under the menu;
/// its transparency follows the setting at once.</summary>
public class FloatingKeyboardUiTests
{
    private static RadialMenuOverlay.MenuState Open(long seq, RadialMenuOverlay.UsedSlice? used = null) =>
        new("open", seq, 600, 400, 50, 0,
            new[] { "Undo", "Redo", "Copy", "Paste", "Cut", "Select all", "Zoom out", "Keyboard" }.ToList(),
            Enumerable.Repeat(true, 8).ToList(), Enumerable.Repeat(false, 8).ToList(), Used: used);

    private static void Pump() { for (int i = 0; i < 4; i++) Dispatcher.UIThread.RunJobs(); }

    [AvaloniaFact]
    public void TheKeyboardSlice_ShowsTheKeyboardUnderTheMenu_ThenHidesIt_WithoutATextBubble()
    {
        _ = new ThemeViewModel();
        var dir = Path.Combine(Path.GetTempPath(), "ota-kbui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        FloatingKeyboard.OpenFlagPath = Path.Combine(dir, "keyboard-open");
        FloatingKeyboard.CommandFilePath = Path.Combine(dir, "keyboard-cmd");
        AppSettings.Set(FloatingKeyboard.PositionKey, "");
        var overlay = new RadialMenuOverlay();
        try
        {
            overlay.ShowNowForTest(Open(1));
            overlay.ShowUsedIfNew(Open(2, new RadialMenuOverlay.UsedSlice(1, "Keyboard", "", RadialActions.Keyboard, true)));
            Pump();
            var kb = FloatingKeyboard.Current!;
            Assert.True(kb.IsShown);
            Assert.True(File.Exists(FloatingKeyboard.OpenFlagPath));          // the driver may now press its keys
            Assert.False(overlay.Toast.IsVisible);                             // the keyboard itself is the confirmation
            var menu = overlay.MenuWindow!; var w = kb.Window!;
            Assert.True(w.Position.Y >= menu.Position.Y + menu.Height - 6, "the keyboard is not under the menu");
            Assert.True(Math.Abs(w.Position.X + w.Width / 2 - 600) <= 2, "the keyboard is not centred under the menu");
            var (kw, kh) = FloatingKeyboard.BaseSize();
            Assert.True(kw <= 480 && kh <= 200, $"the keyboard is not compact at 100%: {kw}x{kh}");
            // V29 #162: shown at its Keyboard size (150% unless changed)
            Assert.Equal(FloatingKeyboard.Size().W, w.Width, 1);
            Assert.Equal(FloatingKeyboard.Size().H, w.Height, 1);
            Assert.False(w.ShowActivated);                                     // never takes the focus from the user's app
            Assert.True(w.Topmost);

            overlay.ShowUsedIfNew(Open(3, new RadialMenuOverlay.UsedSlice(2, "Keyboard", "", RadialActions.Keyboard, true)));
            Pump();
            Assert.False(kb.IsShown);
            Assert.False(File.Exists(FloatingKeyboard.OpenFlagPath));         // hidden: it can't type any more
        }
        finally
        {
            FloatingKeyboard.Current?.Dispose();
            overlay.Dispose();
            FloatingKeyboard.OpenFlagPath = FloatingKeyboardFiles.OpenFlag;
            FloatingKeyboard.CommandFilePath = FloatingKeyboardFiles.CommandFile;
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    [AvaloniaFact]
    public void Transparency_FollowsTheSetting_AtOnce()
    {
        _ = new ThemeViewModel();
        var dir = Path.Combine(Path.GetTempPath(), "ota-kbui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        FloatingKeyboard.OpenFlagPath = Path.Combine(dir, "keyboard-open");
        var look = new FloatingKeyboardLookViewModel();
        try
        {
            look.Transparency = 0;
            FloatingKeyboard.Instance.Show(600, 400);
            Pump();
            var panel = FloatingKeyboard.Current!.Panel!;
            Assert.Equal(1.0, panel.Opacity, 3);
            look.TransparencyInput = "40";
            Assert.Equal(RadialMenuDef.OpacityFor(40), panel.Opacity, 3);
        }
        finally
        {
            FloatingKeyboard.Current?.Dispose();
            AppSettings.Set(FloatingKeyboard.TransparencyKey, "");
            FloatingKeyboard.OpenFlagPath = FloatingKeyboardFiles.OpenFlag;
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    /// <summary>V29 #162 (Zak 20 Nov ~09:10: "the keyboard is microscopic"): an open keyboard follows the Keyboard size at once.</summary>
    [AvaloniaFact]
    public void Size_FollowsTheSetting_AtOnce()
    {
        _ = new ThemeViewModel();
        var dir = Path.Combine(Path.GetTempPath(), "ota-kbui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        FloatingKeyboard.OpenFlagPath = Path.Combine(dir, "keyboard-open");
        var look = new FloatingKeyboardLookViewModel();
        try
        {
            look.Size = 100;
            FloatingKeyboard.Instance.Show(600, 400);
            Pump();
            var w = FloatingKeyboard.Current!.Window!;
            var (bw, bh) = FloatingKeyboard.BaseSize();
            Assert.Equal(bw, w.Width, 1);
            look.SizeInput = "200";
            Assert.Equal(Math.Round(bw * 2), w.Width, 1);
            Assert.Equal(Math.Round(bh * 2), w.Height, 1);
        }
        finally
        {
            FloatingKeyboard.Current?.Dispose();
            FloatingKeyboard.ResetToFactory();
            FloatingKeyboard.OpenFlagPath = FloatingKeyboardFiles.OpenFlag;
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}
