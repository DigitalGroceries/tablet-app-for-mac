using System;
using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>v27 (Zak 13 Nov 16:02-16:03): a radial menu that is already open on screen follows the Menu transparency and Menu size
/// sliders at once (it used to keep the old look until it was closed and opened again). The app tells the driver to use the
/// changed menu, so the driver's own state (and the pen's tap ring) matches what the window shows.</summary>
public class RadialLiveLookTests
{
    private static RadialMenuDef Menu(int size, int transparency)
    {
        var m = RadialMenuDefaults.Create(Localizer.Te);
        m.Size = size; m.Transparency = transparency;
        return m;
    }

    private static RadialMenuOverlay.MenuState Open(RadialMenuDef menu, long seq = 1, bool editing = false, bool busy = false) =>
        new("open", seq, 500, 400, menu.Size, 0, menu.Slices.Select(s => s.Label ?? "").ToList(),
            Enumerable.Repeat(true, 8).ToList(), Enumerable.Repeat(false, 8).ToList(),
            Editing: editing, Menu: RadialMenuJson.Serialize(menu), Transparency: menu.Transparency, Busy: busy);

    private static (RadialMenuOverlay Overlay, string Dir, string OldCmd) Start()
    {
        _ = new ThemeViewModel();
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ota-radial-live-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        var old = RadialMenuOverlay.CommandFilePath;
        RadialMenuOverlay.CommandFilePath = System.IO.Path.Combine(dir, "radial-menu-cmd");
        return (new RadialMenuOverlay(), dir, old);
    }

    private static RadialMenuCommand? Sent() =>
        System.IO.File.Exists(RadialMenuOverlay.CommandFilePath) ? RadialMenuCommand.Parse(System.IO.File.ReadAllText(RadialMenuOverlay.CommandFilePath)) : null;

    [AvaloniaFact]
    public void AnOpenMenu_IsToldTheNewSizeAndTransparency_AtOnce()
    {
        var (overlay, dir, old) = Start();
        try
        {
            overlay.ShowNowForTest(Open(Menu(50, 40)));
            Dispatcher.UIThread.RunJobs();
            Assert.True(overlay.TryLook(RadialMenuJson.Serialize(Menu(30, 55))));
            var cmd = Assert.IsType<RadialMenuCommand>(Sent());
            Assert.Equal(RadialMenuCommand.MenuChanged, cmd.Op);
            var sent = RadialMenuJson.Parse(cmd.Menu);
            Assert.Equal(30, sent.Size); Assert.Equal(55, sent.Transparency);

            // The driver answers with its new state: the window follows (size and fading).
            overlay.ShowNowForTest(Open(Menu(30, 55), seq: 2));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2 * RadialMenuGeometry.OuterRadius(30) + 4, overlay.MenuWindow!.Width, 1);
            Assert.Equal(RadialMenuDef.OpacityFor(55), overlay.MenuWindow.Content is Avalonia.Visual v ? v.Opacity : -1);
        }
        finally { overlay.Dispose(); RadialMenuOverlay.CommandFilePath = old; try { System.IO.Directory.Delete(dir, true); } catch (System.IO.IOException) { } }
    }

    [AvaloniaFact]
    public void AnOpenColourPalette_FollowsItsSizeSlider_AtOnce()
    {
        // V29 #155 (Zak 20 Nov): the palette's size used to change only the next time it opened.
        var (overlay, dir, old) = Start();
        try
        {
            var menu = Menu(50, 40); menu.PaletteSize = 40;
            overlay.ShowNowForTest(Open(menu) with { Size = 40, Kind = "palette", Transparency = 0 });
            Dispatcher.UIThread.RunJobs();
            var smaller = Menu(50, 40); smaller.PaletteSize = 30;
            Assert.True(overlay.TryLook(RadialMenuJson.Serialize(smaller)));
            var cmd = Assert.IsType<RadialMenuCommand>(Sent());
            Assert.Equal(RadialMenuCommand.MenuChanged, cmd.Op);
            Assert.Equal(30, RadialMenuJson.Parse(cmd.Menu).PaletteSize);
        }
        finally { overlay.Dispose(); RadialMenuOverlay.CommandFilePath = old; try { System.IO.Directory.Delete(dir, true); } catch (System.IO.IOException) { } }
    }

    [AvaloniaFact]
    public void ADifferentMenu_OrAnEditInProgress_OrNoOpenMenu_IsLeftAlone()
    {
        var (overlay, dir, old) = Start();
        try
        {
            Assert.False(overlay.TryLook(RadialMenuJson.Serialize(Menu(30, 55))));   // nothing open
            var other = Menu(50, 40); other.Slices[0].Label = "Something else";
            overlay.ShowNowForTest(Open(other));
            Dispatcher.UIThread.RunJobs();
            Assert.False(overlay.TryLook(RadialMenuJson.Serialize(Menu(30, 55))));   // another menu (other slices) is open
            overlay.ShowNowForTest(Open(Menu(50, 40), seq: 2, editing: true));
            Assert.False(overlay.TryLook(RadialMenuJson.Serialize(Menu(30, 55))));   // the pencil is on
            overlay.ShowNowForTest(Open(Menu(50, 40), seq: 3, busy: true));
            Assert.False(overlay.TryLook(RadialMenuJson.Serialize(Menu(30, 55))));   // the app is editing a slice
            Assert.Null(Sent());
        }
        finally { overlay.Dispose(); RadialMenuOverlay.CommandFilePath = old; try { System.IO.Directory.Delete(dir, true); } catch (System.IO.IOException) { } }
    }

    [Fact]
    public void TheEditorsSliders_TellTheOpenMenu()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir, "OpenTabletArtist.slnx"))) dir = System.IO.Path.GetDirectoryName(dir);
        var vm = System.IO.File.ReadAllText(System.IO.Path.Combine(dir!, "OpenTabletArtist", "ViewModels", "RadialMenuEditorViewModel.cs"));
        Assert.Equal(3, vm.Split("RadialMenuOverlay.ShowLookLive(CurrentJson())").Length - 1);   // size, transparency and (V29 #155) palette size
    }

    // ── V29 #161 (Zak 20 Nov ~09:10: "The update to keyboard did not update on the already opened radial menu") ──

    private static RadialMenuDef WithKeyboardOn(int slice)
    {
        var m = Menu(10, 40);
        m.Slices[slice] = new RadialSlice { Label = "Keyboard", Action = RadialActions.Keyboard };
        return m;
    }

    private static AuxBinding Radial(RadialMenuDef m) =>
        AuxBinding.RadialMenu(RadialMenuJson.Serialize(m));

    [AvaloniaFact]
    public void AMenuSavedInChange_ReachesTheSameMenuOpenOnScreen()
    {
        var (overlay, dir, old) = Start();
        try
        {
            overlay.ShowNowForTest(Open(Menu(10, 40)));
            Dispatcher.UIThread.RunJobs();
            var after = WithKeyboardOn(7);
            Assert.True(overlay.TryReplace(RadialMenuJson.Serialize(Menu(10, 40)), RadialMenuJson.Serialize(after)));
            var cmd = Assert.IsType<RadialMenuCommand>(Sent());
            Assert.Equal(RadialMenuCommand.MenuChanged, cmd.Op);
            Assert.Equal(RadialActions.Keyboard, RadialMenuJson.Parse(cmd.Menu).Slices[7].Action);
        }
        finally { overlay.Dispose(); RadialMenuOverlay.CommandFilePath = old; try { System.IO.Directory.Delete(dir, true); } catch (System.IO.IOException) { } }
    }

    [AvaloniaFact]
    public void AMenuOpenFromAnotherButton_IsLeftAlone_AndSoIsOneBeingEdited()
    {
        var (overlay, dir, old) = Start();
        try
        {
            overlay.ShowNowForTest(Open(WithKeyboardOn(2)));   // other slices: another button's menu
            Dispatcher.UIThread.RunJobs();
            Assert.False(overlay.TryReplace(RadialMenuJson.Serialize(Menu(10, 40)), RadialMenuJson.Serialize(WithKeyboardOn(7))));
            Assert.Null(Sent());

            overlay.ShowNowForTest(Open(Menu(10, 40), seq: 2, editing: true));
            Dispatcher.UIThread.RunJobs();
            Assert.False(overlay.TryReplace(RadialMenuJson.Serialize(Menu(10, 40)), RadialMenuJson.Serialize(WithKeyboardOn(7))));
            Assert.Null(Sent());
        }
        finally { overlay.Dispose(); RadialMenuOverlay.CommandFilePath = old; try { System.IO.Directory.Delete(dir, true); } catch (System.IO.IOException) { } }
    }

    [Fact]
    public void ReplaceLive_OnlyActsWhenARadialMenuReallyChanged()
    {
        var a = Radial(Menu(10, 40));
        Assert.False(RadialMenuOverlay.ReplaceLive(a, a));
        Assert.False(RadialMenuOverlay.ReplaceLive(AuxBinding.Unbound, a));
        Assert.False(RadialMenuOverlay.ReplaceLive(a, AuxBinding.Unbound));
    }
}
