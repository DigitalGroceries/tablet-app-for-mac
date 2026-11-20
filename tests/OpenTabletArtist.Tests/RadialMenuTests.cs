using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenTabletArtist.Domain;
using PluginBinding = OpenTabletArtist.Dynamics.RadialMenuBinding;
using RadialMenuState = OpenTabletArtist.Dynamics.RadialMenuState;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OpenTabletDriver.Desktop.Binding;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Radial menu (TASKS #42): the menu model, where a tap lands, the binding store and filter, the
/// driver-side open/tap/close logic, the overlay's state file and the editor.</summary>
public class RadialMenuModelTests
{
    [Theory]
    [InlineData(0, 0, RadialMenuGeometry.Centre)]
    [InlineData(0, -100, 0)]      // straight up
    [InlineData(70, -70, 1)]      // up-right
    [InlineData(100, 0, 2)]       // right
    [InlineData(70, 70, 3)]
    [InlineData(0, 100, 4)]       // down
    [InlineData(-70, 70, 5)]
    [InlineData(-100, 0, 6)]      // left
    [InlineData(-70, -70, 7)]
    [InlineData(0, -1000, RadialMenuGeometry.Outside)]
    public void SliceAt_TopIsZero_ThenClockwise(double dx, double dy, int expected) =>
        Assert.Equal(expected, RadialMenuGeometry.SliceAt(dx, dy, RadialMenuDef.DefaultSize));

    [Fact]
    public void SliceAt_BoundaryBetweenTopAndTopRight()
    {
        // 22.5° clockwise from the top is the edge between slice 0 and slice 1.
        double r = 100, a = 22.0 * Math.PI / 180, b = 23.0 * Math.PI / 180;
        Assert.Equal(0, RadialMenuGeometry.SliceAt(r * Math.Sin(a), -r * Math.Cos(a), 50));
        Assert.Equal(1, RadialMenuGeometry.SliceAt(r * Math.Sin(b), -r * Math.Cos(b), 50));
    }

    [Fact]
    public void Size_ChangesRadius()
    {
        Assert.True(RadialMenuGeometry.OuterRadius(50) > RadialMenuGeometry.OuterRadius(1));
        Assert.Equal(160, RadialMenuGeometry.OuterRadius(50), 3);
        // TASKS #63 (Zak 21:01): nothing is bigger than the largest size (50 = 320 pt across), whatever number is stored.
        Assert.Equal(50, RadialMenuDef.MaxSize);
        Assert.Equal(RadialMenuDef.MaxDiameter, 2 * RadialMenuGeometry.OuterRadius(RadialMenuDef.MaxSize), 3);
        Assert.Equal(RadialMenuGeometry.OuterRadius(50), RadialMenuGeometry.OuterRadius(100), 3);
        Assert.Equal(RadialMenuGeometry.OuterRadius(50), RadialMenuGeometry.OuterRadius(int.MaxValue), 3);
        Assert.Equal(RadialMenuGeometry.Outside, RadialMenuGeometry.SliceAt(0, -200, 1));
        Assert.Equal(RadialMenuGeometry.Outside, RadialMenuGeometry.SliceAt(0, -200, 100));   // beyond the 160 pt radius
        Assert.Equal(0, RadialMenuGeometry.SliceAt(0, -150, 50));
    }

    [Fact]
    public void Default_HasEightSlices_UndoOnTop()
    {
        var m = RadialMenuDefaults.Create();
        Assert.Equal(RadialMenuDef.SliceCount, m.Slices.Count);
        Assert.Equal("Undo", m.Slices[0].Label);
        Assert.Equal("Application+Z", m.Slices[0].Keys);
        Assert.Equal("Application+Shift+Z", m.Slices[1].Keys);
        Assert.All(m.Slices, s => Assert.True(s.IsSet));
    }

    [Fact]
    public void Default_TranslatesNames()
    {
        var m = RadialMenuDefaults.Create((key, en) => key == "radial.default.undo" ? "Отмени" : en);
        Assert.Equal("Отмени", m.Slices[0].Label);
        Assert.Equal("Redo", m.Slices[1].Label);
    }

    [Fact]
    public void Json_RoundTrips()
    {
        var m = RadialMenuDefaults.Create();
        m.Size = 40;
        m.Slices[3] = new RadialSlice { Label = "Tools", Action = RadialActions.Submenu, Submenu = RadialMenuDefaults.Create() };
        m.Slices[4] = new RadialSlice { Label = "Safari", Action = RadialActions.Open, App = "com.apple.Safari" };
        m.Slices[5] = new RadialSlice { Label = "Right", Action = RadialActions.Click, Click = "Right" };
        var back = RadialMenuJson.Parse(RadialMenuJson.Serialize(m));
        Assert.Equal(40, back.Size);
        Assert.Equal(RadialActions.Submenu, back.Slices[3].Action);
        Assert.Equal("Application+Z", back.Slices[3].Submenu!.Slices[0].Keys);
        Assert.Equal("com.apple.Safari", back.Slices[4].App);
        Assert.Equal("Right", back.Slices[5].Click);
        Assert.Equal(RadialMenuJson.Serialize(m), RadialMenuJson.Serialize(back));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{not json")]
    [InlineData("[]")]
    public void Parse_BrokenOrEmpty_GivesDefault(string? json)
    {
        var m = RadialMenuJson.Parse(json);
        Assert.Equal(RadialMenuDef.SliceCount, m.Slices.Count);
        Assert.Equal("Undo", m.Slices[0].Label);
    }

    [Fact]
    public void Normalize_PadsSlices_DropsUnknownActions_ClampsSize()
    {
        var m = RadialMenuJson.Parse("{\"size\":500,\"slices\":[{\"label\":\"A\",\"action\":\"Launch rockets\"},{\"label\":\"B\",\"action\":\"keys\",\"keys\":\"Z\"}]}");
        Assert.Equal(RadialMenuDef.SliceCount, m.Slices.Count);
        Assert.Equal(RadialMenuDef.MaxSize, m.Size);
        Assert.Equal(RadialActions.None, m.Slices[0].Action);
        Assert.False(m.Slices[0].IsSet);
        Assert.Equal(RadialActions.Keys, m.Slices[1].Action);   // action names are not case sensitive
        Assert.All(m.Slices.Skip(2), s => Assert.Equal("", s.Label));
    }

    [Fact]
    public void Normalize_StopsSubmenusAtMaxDepth()
    {
        RadialMenuDef Nest(int depth) => depth == 0 ? new RadialMenuDef()
            : new RadialMenuDef { Slices = { new RadialSlice { Label = "deeper", Action = RadialActions.Submenu, Submenu = Nest(depth - 1) } } };
        var m = RadialMenuJson.Normalize(Nest(6));
        int depth = 1;
        var cur = m;
        while (cur.Slices[0].Action == RadialActions.Submenu) { cur = cur.Slices[0].Submenu!; depth++; }
        Assert.Equal(RadialMenuDef.MaxDepth, depth);
    }

    [Fact]
    public void EmptyLabelsAndSlices_NeverBreakSerialising()
    {
        // Wacom Center 6.4.15 crashed drawing a slice with no label/icon (9 Nov comparison): empty is normal here.
        var m = new RadialMenuDef();
        var json = RadialMenuJson.Serialize(m);
        var back = RadialMenuJson.Parse(json);
        Assert.All(back.Slices, s => { Assert.Equal("", s.Label); Assert.False(s.IsSet); });
    }

    [Fact]
    public void Level_FollowsSubmenus()
    {
        var m = RadialMenuDefaults.Create();
        var sub = new RadialMenuDef { Slices = { new RadialSlice { Label = "X", Action = RadialActions.Keys, Keys = "X" } } };
        m.Slices[2] = new RadialSlice { Label = "More", Action = RadialActions.Submenu, Submenu = sub };
        m = RadialMenuJson.Normalize(m);
        Assert.Same(m, RadialMenuJson.Level(m, Array.Empty<int>()));
        Assert.Equal("X", RadialMenuJson.Level(m, new[] { 2 })!.Slices[0].Label);
        Assert.Null(RadialMenuJson.Level(m, new[] { 1 }));   // not a submenu
        Assert.Null(RadialMenuJson.Level(m, new[] { 9 }));
    }
}

public class RadialMenuBindingTests
{
    [Fact]
    public void Store_RoundTripsThroughAuxBinding()
    {
        var menu = RadialMenuDefaults.Create();
        menu.Slices[7].Label = "Bigger";
        var store = RadialMenuBinding.Make(menu);
        Assert.Equal(RadialMenuBinding.BindingTypeName, store.Path);

        var read = AuxKeyBinding.ReadBinding(store);
        Assert.NotNull(read);
        Assert.Equal(AuxKind.RadialMenu, read!.Kind);
        Assert.True(read.IsBound);
        Assert.Equal("Bigger", RadialMenuJson.Parse(read.Menu).Slices[7].Label);

        var again = AuxKeyBinding.MakeBinding(read);
        Assert.Equal(RadialMenuBinding.BindingTypeName, again!.Path);
        Assert.Equal(RadialMenuBinding.ReadMenuJson(store), RadialMenuBinding.ReadMenuJson(again));
    }

    [Fact]
    public void Describe_NamesTheFirstSlices()
    {
        var d = AuxKeyBinding.Describe(AuxBinding.RadialMenu(RadialMenuJson.Serialize(RadialMenuDefaults.Create())));
        Assert.StartsWith("Radial menu: Undo, Redo, Copy", d);
    }

    [Fact]
    public void RadialMenu_WithNoJson_GetsTheDefaultMenu()
    {
        var b = AuxBinding.RadialMenu(null);
        Assert.Equal(RadialMenuDef.SliceCount, RadialMenuJson.Parse(b.Menu).Slices.Count(s => s.IsSet));
    }

    [Fact]
    public void SyncFilter_AddsAndRemovesTheFilter()
    {
        var profile = new Profile
        {
            Tablet = "Wacom PTK-440",
            OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)),
            Filters = new PluginSettingStoreCollection(),
            BindingSettings = new BindingSettings { AuxButtons = new PluginSettingStoreCollection { RadialMenuBinding.Make() } },
        };
        Assert.True(RadialMenuBinding.SyncFilter(profile));
        Assert.Single(profile.Filters.OfType<PluginSettingStore>().Where(f => f.Path == RadialMenuBinding.FilterTypeName));
        Assert.False(RadialMenuBinding.SyncFilter(profile));   // already right

        profile.BindingSettings.AuxButtons[0] = null;
        Assert.True(RadialMenuBinding.SyncFilter(profile));
        Assert.DoesNotContain(profile.Filters.OfType<PluginSettingStore>(), f => f.Path == RadialMenuBinding.FilterTypeName);
    }

    [Fact]
    public void PenButton_CanHoldARadialMenu()
    {
        var profile = new Profile
        {
            Tablet = "Wacom PTK-440",
            OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)),
            Filters = new PluginSettingStoreCollection(),
            BindingSettings = new BindingSettings { PenButtons = new PluginSettingStoreCollection { RadialMenuBinding.Make() } },
        };
        Assert.True(RadialMenuBinding.IsUsed(profile));
    }
}

/// <summary>The driver side: opening, tapping, holding, submenus and closing (fake pointer, fake actions).</summary>
public class RadialMenuStateTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ota-radial-" + Guid.NewGuid().ToString("N"));
    private (double X, double Y)? _cursor = (500, 400);
    private readonly List<string> _ran = new();

    public RadialMenuStateTests()
    {
        Directory.CreateDirectory(_dir);
        RadialMenuState.ResetForTests();
        RadialMenuState.StateFilePath = Path.Combine(_dir, "radial-menu");
        RadialMenuState.OffFlagPath = Path.Combine(_dir, "radial-menu-off");
        RadialMenuState.CursorProvider = () => _cursor;
        RadialMenuState.ActionRunner = (_, s) => _ran.Add(s.Label);
        RadialMenuState.CommandFilePath = Path.Combine(_dir, "radial-menu-cmd");
        RadialMenuState.PaletteStateFilePath = Path.Combine(_dir, "radial-palette");      // V29 #154
        RadialMenuState.PaletteCommandFilePath = Path.Combine(_dir, "radial-palette-cmd");
        RadialMenuState.ScreensProvider = () => new List<(double, double, double, double)> { (0, 0, 3000, 2000) };
        RadialMenuState.CommandPollMs = 40;
        RadialMenuState.DoublePressMs = -1;   // presses in a test come fast: only the palette tests count double presses
    }

    public void Dispose()
    {
        RadialMenuState.DoublePressMs = 400;
        RadialMenuState.ResetForTests();
        RadialMenuState.ActionRunner = null;
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private static PluginBinding Binding(RadialMenuDef? menu = null) =>
        new() { Menu = RadialMenuJson.Serialize(menu ?? RadialMenuDefaults.Create()) };

    private JsonElement State() => JsonDocument.Parse(File.ReadAllText(RadialMenuState.StateFilePath)).RootElement;
    /// <summary>V29 #154: the colour palette's own state file.</summary>
    private JsonElement PState() => JsonDocument.Parse(File.ReadAllText(RadialMenuState.PaletteStateFilePath)).RootElement;

    private void TapAt(double dx, double dy)
    {
        _cursor = (500 + dx, 400 + dy);
        RadialMenuState.TipDown();
    }

    [Fact]
    public void Press_OpensAtThePointer_AndWritesTheMenu()
    {
        var b = Binding();
        b.Press(null!, null!);
        Assert.True(RadialMenuState.IsOpen);
        var st = State();
        Assert.Equal("open", st.GetProperty("state").GetString());
        Assert.Equal(500, st.GetProperty("x").GetDouble());
        Assert.Equal(400, st.GetProperty("y").GetDouble());
        Assert.Equal("Undo", st.GetProperty("slices")[0].GetProperty("label").GetString());
        Assert.Equal(8, st.GetProperty("slices").GetArrayLength());
    }

    [Fact]
    public void Tap_RunsTheSlice_AndTheMenuStaysOpen_WithTheButtonUp()
    {
        // V29 #131 (Zak 19 Nov): "The radial menu should never close if not closed by the user by clicking the X".
        var b = Binding();
        b.Press(null!, null!);
        b.Release(null!, null!);
        TapAt(0, -100);   // top = Undo
        TapAt(0, -100);
        Assert.Equal(new[] { "Undo", "Undo" }, _ran);
        Assert.True(RadialMenuState.IsOpen);
        Assert.Equal("open", State().GetProperty("state").GetString());
    }

    [Fact]
    public void HoldingTheButton_KeepsTheMenuOpen()
    {
        var b = Binding();
        b.Press(null!, null!);          // held
        TapAt(0, -100);
        TapAt(0, -100);
        Assert.Equal(new[] { "Undo", "Undo" }, _ran);
        Assert.True(RadialMenuState.IsOpen);
        b.Release(null!, null!);
        TapAt(100, 0);                  // right = Copy; the menu still stays (V29 #131: only the X closes it)
        Assert.Equal("Copy", _ran.Last());
        Assert.True(RadialMenuState.IsOpen);
    }

    [Fact]
    public void TheCentreX_Closes_WithoutAnAction()
    {
        var b = Binding();
        b.Press(null!, null!);
        TapAt(0, 0);
        Assert.Empty(_ran);
        Assert.False(RadialMenuState.IsOpen);
    }

    [Fact]
    public void OutsideTheRing_TheTapGoesToTheApp_AndTheMenuStaysOpen()
    {
        // V29 #131: like a mouse click outside the menu.
        var b = Binding();
        b.Press(null!, null!);
        b.Release(null!, null!);
        _cursor = (500, 400 - 900);
        Assert.False(RadialMenuState.TipDown());   // false = not the menu's: the stroke goes to the app
        Assert.Empty(_ran);
        Assert.True(RadialMenuState.IsOpen);
        _cursor = (500, 300);                      // and the menu still works afterwards
        Assert.True(RadialMenuState.TipDown());
        Assert.Equal(new[] { "Undo" }, _ran);
    }

    [Fact]
    public void Filter_AStrokeOutsideTheMenu_KeepsItsPressure_AStrokeOnTheMenuDoesNot()
    {
        var b = Binding();
        b.Press(null!, null!);
        b.Release(null!, null!);
        var filter = new OpenTabletArtist.Dynamics.RadialMenuFilter();
        uint seen = 999;
        filter.Emit += r => seen = ((OpenTabletDriver.Plugin.Tablet.ITabletReport)r).Pressure;

        _cursor = (500, 400 - 900);                         // outside: draws in the app, menu stays
        filter.Consume(new PenReport { Pressure = 300 });
        Assert.Equal(300u, seen);
        filter.Consume(new PenReport { Pressure = 500 });   // the rest of that stroke too
        Assert.Equal(500u, seen);
        filter.Consume(new PenReport { Pressure = 0 });
        Assert.True(RadialMenuState.IsOpen);
        Assert.Empty(_ran);

        _cursor = (500, 300);                                // on Undo: the menu takes the stroke
        filter.Consume(new PenReport { Pressure = 300 });
        Assert.Equal(0u, seen);
        Assert.Equal(new[] { "Undo" }, _ran);
        filter.Consume(new PenReport { Pressure = 0 });
        Assert.True(RadialMenuState.IsOpen);
    }

    private sealed class PenReport : OpenTabletDriver.Plugin.Tablet.ITabletReport
    {
        public byte[] Raw { get; set; } = Array.Empty<byte>();
        public System.Numerics.Vector2 Position { get; set; }
        public uint Pressure { get; set; }
        public bool[] PenButtons { get; set; } = new bool[2];
    }

    [Fact]
    public void EmptySlice_DoesNothing_AndTheMenuStaysOpen()
    {
        var m = RadialMenuDefaults.Create();
        m.Slices[2] = new RadialSlice();
        var b = Binding(m);
        b.Press(null!, null!);
        b.Release(null!, null!);
        TapAt(100, 0);
        Assert.Empty(_ran);
        Assert.True(RadialMenuState.IsOpen);
    }

    [Fact]
    public void AClickSlice_TheMenuStepsAsideForTheClick_AndStaysOpen()
    {
        // V29 #131 (Zak 19 Nov: "Just dont close it when non of this is engaged"): the app lets clicks through the menu while the
        // Click slice clicks, then the menu takes clicks again.
        var m = RadialMenuDefaults.Create();
        m.Slices[2] = new RadialSlice { Label = "Right click", Action = RadialActions.Click, Click = "Right" };
        var b = Binding(m);
        b.Press(null!, null!);
        b.Release(null!, null!);
        bool asideWhenClicked = false;
        RadialMenuState.ActionRunner = (_, s) =>
        {
            _ran.Add(s.Label);
            asideWhenClicked = State().GetProperty("stepAside").GetBoolean();
        };
        RadialMenuState.StepAsideMs = 0;   // no waiting in a test
        try { TapAt(100, 0); }
        finally { RadialMenuState.StepAsideMs = 150; }
        Assert.Equal(new[] { "Right click" }, _ran);
        Assert.True(asideWhenClicked);
        Assert.True(RadialMenuState.IsOpen);
        Assert.False(State().GetProperty("stepAside").GetBoolean());
        Assert.True(RadialMenuOverlay.Parse(File.ReadAllText(RadialMenuState.StateFilePath))!.StepAside == false);
    }

    [Fact]
    public void PressingAgain_Closes()
    {
        var b = Binding();
        b.Press(null!, null!);
        b.Release(null!, null!);
        b.Press(null!, null!);
        Assert.False(RadialMenuState.IsOpen);
    }

    // ── V29 #142 (Zak 19 Nov 19:35): a double press on the same pen button opens the colour palette or swaps ──

    [Fact]
    public void DoublePress_OpensTheColourPalette_AndAnotherDoublePressClosesIt()
    {
        // V29 #154 (Zak 20 Nov, Y): two quick presses open or close the palette; the menu is left as it was.
        RadialMenuState.DoublePressMs = 400;
        var b = Binding();
        b.Press(null!, null!); b.Release(null!, null!);
        b.Press(null!, null!); b.Release(null!, null!);          // quick second press: the palette, not the menu
        Assert.True(RadialMenuState.PaletteOpen);
        Assert.False(RadialMenuState.MenuOpen);
        Assert.Equal("palette", PState().GetProperty("kind").GetString());
        Assert.Equal("closed", State().GetProperty("state").GetString());
        b.Press(null!, null!); b.Release(null!, null!);          // a new double press while it is open...
        b.Press(null!, null!); b.Release(null!, null!);          // ...closes the palette
        Assert.False(RadialMenuState.PaletteOpen);
        Assert.False(RadialMenuState.MenuOpen);
    }

    [Fact]
    public void OnePress_OpensTheMenu_WhileThePaletteStaysOpen_BesideIt()
    {
        // V29 #154: both can be open; the one opened second goes beside the other when they would overlap.
        RadialMenuState.DoublePressMs = 400;
        var b = Binding();
        b.Press(null!, null!); b.Release(null!, null!);
        b.Press(null!, null!); b.Release(null!, null!);
        Assert.True(RadialMenuState.PaletteOpen);
        System.Threading.Thread.Sleep(450);
        b.Press(null!, null!); b.Release(null!, null!);          // a single press: the menu opens, the palette stays
        Assert.True(RadialMenuState.MenuOpen);
        Assert.True(RadialMenuState.PaletteOpen);
        double r = RadialMenuGeometry.OuterRadius(RadialMenuDef.DefaultSize);
        double mx = State().GetProperty("x").GetDouble(), px = PState().GetProperty("x").GetDouble();
        Assert.True(Math.Abs(mx - px) >= 2 * r, $"menu at {mx}, palette at {px}");
        System.Threading.Thread.Sleep(450);
        b.Press(null!, null!);                                   // another single press closes the menu only
        Assert.False(RadialMenuState.MenuOpen);
        Assert.True(RadialMenuState.PaletteOpen);
    }

    [Fact]
    public void ADoublePressWhileTheMenuIsOpen_KeepsTheMenu_AndAddsThePalette()
    {
        RadialMenuState.DoublePressMs = 400;
        var b = Binding();
        b.Press(null!, null!); b.Release(null!, null!);          // the menu
        System.Threading.Thread.Sleep(450);
        b.Press(null!, null!); b.Release(null!, null!);          // first press closes it for a moment...
        b.Press(null!, null!); b.Release(null!, null!);          // ...the second puts it back and opens the palette
        Assert.True(RadialMenuState.MenuOpen);
        Assert.True(RadialMenuState.PaletteOpen);
    }

    [Fact]
    public void QuickPressesOfTwoDifferentButtons_AreTwoSinglePresses()
    {
        // Grok's review: Zak's double press is the SAME button pressed twice quickly.
        RadialMenuState.DoublePressMs = 400;
        var a = Binding(); var b = Binding();
        a.Press(null!, null!); a.Release(null!, null!);
        b.Press(null!, null!); b.Release(null!, null!);
        Assert.False(RadialMenuState.PaletteOpen);
        Assert.False(RadialMenuState.MenuOpen);   // the second button closed the menu the first one opened
    }

    [Fact]
    public void Beside_StaysOnThePensScreen_WhenItFits()
    {
        RadialMenuState.ScreensProvider = () => new List<(double, double, double, double)> { (1000, 0, 3000, 2000), (0, 0, 1000, 1000) };
        Assert.Equal((678.0, 500.0), RadialMenuState.Beside(890, 500, 100, 890, 500, 100));   // not onto the big screen on the right
    }

    [Fact]
    public void Beside_GoesToTheOtherSide_OrBelow_WhenThePointersSideIsOffTheScreen()
    {
        RadialMenuState.ScreensProvider = () => new List<(double, double, double, double)> { (0, 0, 1000, 1000) };
        Assert.Equal((500.0, 500.0), RadialMenuState.Beside(500, 500, 100, 100, 100, 100));          // far apart: unchanged
        Assert.Equal((382.0, 170.0), RadialMenuState.Beside(200, 170, 100, 170, 170, 100));         // pointer side (right) fits
        var (x, y) = RadialMenuState.Beside(890, 500, 100, 890, 500, 100);                            // right side is off the screen
        Assert.Equal((678.0, 500.0), (x, y));
        RadialMenuState.ScreensProvider = () => new List<(double, double, double, double)> { (0, 0, 300, 1000) };
        Assert.Equal((150.0, 362.0), RadialMenuState.Beside(150, 150, 100, 150, 150, 100));          // neither side: below
    }

    [Fact]
    public void PaletteSwatch_ReportsItsColour_AndThePaletteStaysOpen()
    {
        RadialMenuState.DoublePressMs = 400;
        var b = Binding();
        b.Press(null!, null!); b.Release(null!, null!);
        b.Press(null!, null!); b.Release(null!, null!);
        double outer = RadialMenuGeometry.OuterRadius(RadialMenuDef.DefaultSize), inner = RadialMenuGeometry.InnerRadius(RadialMenuDef.DefaultSize);
        double greyEdge = outer - (outer - inner) * ColourPalette.GreyBand;
        _cursor = (500, 400 - (greyEdge - 3));                      // just inside the greys ring at the top: yellow, the pure ring
        Assert.True(RadialMenuState.TipDown());
        var used = PState().GetProperty("used");
        Assert.Equal(ColourPalette.Action, used.GetProperty("action").GetString());
        Assert.Equal(ColourPalette.Hex(0, 0), used.GetProperty("keys").GetString());
        Assert.Equal("Yellow, pure", used.GetProperty("label").GetString());
        Assert.True(RadialMenuState.PaletteOpen);
        Assert.Empty(_ran);                                         // nothing pressed in the app: the app sets the colour itself
        _cursor = (500, 400 - 900);
        Assert.False(RadialMenuState.TipDown());                    // outside: goes to the work (so an eyedropper can drag in)
        _cursor = (480, 380);
        RadialMenuState.TipDown();                                  // the X (clear of the middle dot)
        Assert.False(RadialMenuState.IsOpen);
    }

    [Fact]
    public void PaletteGeometry_GreysOutside_12Hues4Shades_PureOutsideDarkInside()
    {
        int size = RadialMenuDef.DefaultSize;
        double outer = RadialMenuGeometry.OuterRadius(size), inner = RadialMenuGeometry.InnerRadius(size);
        double greyEdge = outer - (outer - inner) * ColourPalette.GreyBand;
        Assert.Equal(ColourPalette.FirstGrey, ColourPalette.SwatchAt(0, -(outer - 1), size));          // top of the greys ring: white
        Assert.Equal(ColourPalette.FirstGrey + 6, ColourPalette.SwatchAt(0, outer - 1, size));         // bottom: a dark grey
        Assert.Equal(ColourPalette.Swatch(0, 0), ColourPalette.SwatchAt(0, -(greyEdge - 1), size));
        Assert.Equal(ColourPalette.Swatch(0, 3), ColourPalette.SwatchAt(0, -(inner + 1), size));
        Assert.Equal(ColourPalette.Swatch(3, 0), ColourPalette.SwatchAt(greyEdge - 1, 0, size));      // 90° = red-orange
        Assert.Equal(RadialMenuGeometry.Outside, ColourPalette.SwatchAt(0, -(outer + 5), size));
        double q = inner * 0.4;                                     // inside the centre circle, outside the middle dot
        Assert.Equal(RadialMenuGeometry.Centre, ColourPalette.SwatchAt(-q, -q, size));
        Assert.Equal(RadialMenuGeometry.Edit, ColourPalette.SwatchAt(q, -q, size));
        Assert.Equal(RadialMenuGeometry.Restore, ColourPalette.SwatchAt(-q, q, size));
        Assert.Equal(RadialMenuGeometry.Hand, ColourPalette.SwatchAt(q, q, size));
        Assert.Equal(ColourPalette.Dot, ColourPalette.SwatchAt(0, 0, size));
        Assert.Equal(ColourPalette.Dot, ColourPalette.SwatchAt(-1, -1, size));
        double d = ColourPalette.DotRadius(size);
        Assert.Equal(ColourPalette.Dot, ColourPalette.SwatchAt(0, -(d - 0.5), size));
        Assert.Equal(RadialMenuGeometry.Centre, ColourPalette.SwatchAt(0, -(d + 0.5), size));
        var all = Enumerable.Range(0, ColourPalette.Count).Select(i => ColourPalette.HexFor(null, i)).ToList();
        Assert.Equal(ColourPalette.Count, all.Distinct().Count());
        Assert.Equal("#F4E500", ColourPalette.Hex(0, 0));
        Assert.Equal("#FFFFFF", ColourPalette.HexFor(null, ColourPalette.FirstGrey));
        Assert.Equal("#000000", ColourPalette.HexFor(null, ColourPalette.Count - 1));
    }

    [Fact]
    public void PaletteMiddleDot_ATapDoesNothing_AndThePaletteStaysOpen()
    {
        // Zak 19 Nov 20:21: the small middle circle only shows the last colour picked; tapping it does nothing.
        RadialMenuState.DoublePressMs = 400;
        var b = Binding();
        b.Press(null!, null!); b.Release(null!, null!);
        b.Press(null!, null!); b.Release(null!, null!);
        Assert.True(RadialMenuState.PaletteOpen);
        _cursor = (501, 399);
        Assert.True(RadialMenuState.TipDown());                     // taken by the palette, not passed to the app
        Assert.True(RadialMenuState.IsOpen);
        Assert.True(RadialMenuState.PaletteOpen);
        Assert.False(RadialMenuState.Editing);
        Assert.False(RadialMenuState.Confirming);
        Assert.False(PState().TryGetProperty("used", out var used) && used.ValueKind == System.Text.Json.JsonValueKind.Object);
        Assert.Empty(_ran);
    }

    [Fact]
    public void PalettePencil_ThenASwatch_AsksTheAppToChangeIt_AndTheUsersColourIsUsed()
    {
        RadialMenuState.DoublePressMs = 400;
        var m = RadialMenuDefaults.Create();
        m.PaletteColours = new Dictionary<string, string> { ["0"] = "#123456", ["99"] = "#FFFFFF", ["5"] = "red" };
        var b = Binding(m);
        b.Press(null!, null!); b.Release(null!, null!);
        b.Press(null!, null!); b.Release(null!, null!);
        Assert.True(RadialMenuState.PaletteOpen);
        var menu = RadialMenuJson.Parse(PState().GetProperty("menu").GetString());
        Assert.Equal(new Dictionary<string, string> { ["0"] = "#123456" }, menu.PaletteColours);   // bad entries dropped
        double outer = RadialMenuGeometry.OuterRadius(RadialMenuDef.DefaultSize), inner = RadialMenuGeometry.InnerRadius(RadialMenuDef.DefaultSize);
        double greyEdge = outer - (outer - inner) * ColourPalette.GreyBand;
        _cursor = (500, 400 - (greyEdge - 3));
        RadialMenuState.TipDown();
        Assert.Equal("#123456", PState().GetProperty("used").GetProperty("keys").GetString());   // the user's own colour
        _cursor = (520, 380);                                       // the pencil
        RadialMenuState.TipDown();
        Assert.True(RadialMenuState.Editing);
        _cursor = (500, 400 - (greyEdge - 3));
        RadialMenuState.TipDown();
        var req = PState().GetProperty("request");
        Assert.Equal("palette-edit", req.GetProperty("kind").GetString());
        Assert.Equal(0, req.GetProperty("slice").GetInt32());
        Assert.True(RadialMenuState.PaletteOpen);
    }

    [Fact]
    public void PaletteCircularArrow_AsksYesNo_YesRestoresTheColours()
    {
        RadialMenuState.DoublePressMs = 400;
        var b = Binding();
        b.Press(null!, null!); b.Release(null!, null!);
        b.Press(null!, null!); b.Release(null!, null!);
        _cursor = (480, 420);                                       // the circular arrow
        RadialMenuState.TipDown();
        Assert.True(RadialMenuState.Confirming);
        Assert.True(PState().GetProperty("confirm").GetBoolean());
        _cursor = (600, 400);                                       // right half: Yes
        RadialMenuState.TipDown();
        Assert.Equal("palette-restore", PState().GetProperty("request").GetProperty("kind").GetString());
        Assert.False(RadialMenuState.Confirming);
        Assert.True(RadialMenuState.PaletteOpen);
    }

    [Fact]
    public void Submenu_OpensItsRing_ThenRunsFromIt()
    {
        var m = RadialMenuDefaults.Create();
        var sub = new RadialMenuDef { Slices = { new RadialSlice { Label = "Brush", Action = RadialActions.Keys, Keys = "B" } } };
        m.Slices[2] = new RadialSlice { Label = "Tools", Action = RadialActions.Submenu, Submenu = sub };
        var b = Binding(m);
        b.Press(null!, null!);
        b.Release(null!, null!);
        TapAt(100, 0);                                  // Tools
        Assert.True(RadialMenuState.IsOpen);
        Assert.Equal(new[] { 2 }, RadialMenuState.OpenPath);
        Assert.Equal(1, State().GetProperty("depth").GetInt32());
        Assert.Equal("Brush", State().GetProperty("slices")[0].GetProperty("label").GetString());
        TapAt(0, -100);                                 // Brush
        Assert.Equal(new[] { "Brush" }, _ran);
        Assert.True(RadialMenuState.IsOpen);            // V29 #131: stays open
    }

    [Fact]
    public void SwitchedOff_DoesNothing()
    {
        File.WriteAllText(RadialMenuState.OffFlagPath, "off");
        Binding().Press(null!, null!);
        Assert.False(RadialMenuState.IsOpen);
    }

    [Fact]
    public void NoPointer_DoesNothing()
    {
        _cursor = null;
        Binding().Press(null!, null!);
        Assert.False(RadialMenuState.IsOpen);
    }

    [Fact]
    public void Overlay_ReadsWhatTheDriverWrites()
    {
        var b = Binding();
        b.Press(null!, null!);
        var st = RadialMenuOverlay.Parse(File.ReadAllText(RadialMenuState.StateFilePath));
        Assert.NotNull(st);
        Assert.Equal("open", st!.State);
        Assert.Equal(500, st.X);
        Assert.Equal(8, st.Labels.Count);
        Assert.Equal(RadialMenuDef.DefaultTransparency, st.Transparency);   // TASKS #58: the overlay gets it from the driver's state
        Assert.All(st.Set, Assert.True);
        RadialMenuState.Close("test");
        var closed = RadialMenuOverlay.Parse(File.ReadAllText(RadialMenuState.StateFilePath));
        Assert.Equal("closed", closed!.State);
        Assert.True(closed.Seq > st.Seq);
    }

    // ── The Mac mouse (TASKS #54): the app passes clicks to the driver through a command file ──

    private void Mouse(string op, int slice = -1, double x = 0, double y = 0) =>
        RadialMenuState.HandleCommand(new RadialMenuCommand { Seq = 1, Op = op, Slice = slice, X = x, Y = y });

    [Fact]
    public void MouseTap_RunsTheSlice_AndTheMenuStaysOpen_EvenWithTheButtonUp()
    {
        var b = Binding();
        b.Press(null!, null!);
        b.Release(null!, null!);
        Mouse(RadialMenuCommand.Tap, 0);   // Undo
        Mouse(RadialMenuCommand.Tap, 1);   // Redo
        Mouse(RadialMenuCommand.Tap, 0);
        Assert.Equal(new[] { "Undo", "Redo", "Undo" }, _ran);
        Assert.True(RadialMenuState.IsOpen);
    }

    [Theory]
    [InlineData(RadialMenuCommand.Centre)]   // the X
    [InlineData(RadialMenuCommand.Close)]    // Esc
    public void MouseCentreOrEsc_CloseTheMenu_WithoutAnAction(string op)
    {
        Binding().Press(null!, null!);
        Mouse(op);
        Assert.Empty(_ran);
        Assert.False(RadialMenuState.IsOpen);
        Assert.Equal("closed", State().GetProperty("state").GetString());
    }

    [Fact]
    public void MouseTap_OnAnEmptySlice_DoesNothing_AndStaysOpen()
    {
        var m = RadialMenuDefaults.Create();
        m.Slices[2] = new RadialSlice();
        Binding(m).Press(null!, null!);
        Mouse(RadialMenuCommand.Tap, 2);
        Assert.Empty(_ran);
        Assert.True(RadialMenuState.IsOpen);
    }

    [Fact]
    public void MouseTap_OnASubmenu_OpensIt_ThenRunsFromIt_AndStaysOpen()
    {
        var m = RadialMenuDefaults.Create();
        var sub = new RadialMenuDef { Slices = { new RadialSlice { Label = "Brush", Action = RadialActions.Keys, Keys = "B" } } };
        m.Slices[2] = new RadialSlice { Label = "Tools", Action = RadialActions.Submenu, Submenu = sub };
        Binding(m).Press(null!, null!);
        Mouse(RadialMenuCommand.Tap, 2);
        Assert.Equal(new[] { 2 }, RadialMenuState.OpenPath);
        Assert.Equal(1, State().GetProperty("depth").GetInt32());
        Mouse(RadialMenuCommand.Tap, 0);
        Assert.Equal(new[] { "Brush" }, _ran);
        Assert.True(RadialMenuState.IsOpen);
    }

    [Fact]
    public void MouseTap_OnAClickSlice_DoesNotClickUnderTheMenu()
    {
        var m = RadialMenuDefaults.Create();
        m.Slices[3] = new RadialSlice { Label = "Right click", Action = RadialActions.Click, Click = "Right" };
        Binding(m).Press(null!, null!);
        Mouse(RadialMenuCommand.Tap, 3);
        Assert.Empty(_ran);
        Assert.True(RadialMenuState.IsOpen);
    }

    [Fact]
    public void MouseMove_ShiftsTheCentre_ForThePen_AndParksTheMenu()
    {
        Binding().Press(null!, null!);
        Mouse(RadialMenuCommand.Move, x: 900, y: 700);
        TapAt(0, -100);   // the pen taps relative to the cursor provider's frame; centre is now (900,700)
        // (500,300) is far from (900,700): outside the ring, so nothing runs and the menu stays (V29 #131)
        Assert.Empty(_ran);
        Assert.True(RadialMenuState.IsOpen);

        _cursor = (900, 600);              // 100 points above the new centre: Undo
        RadialMenuState.TipDown();
        Assert.Equal(new[] { "Undo" }, _ran);
    }

    [Fact]
    public void Commands_WhenNoMenuIsOpen_AreIgnored()
    {
        Mouse(RadialMenuCommand.Tap, 0);
        Mouse(RadialMenuCommand.Centre);
        Assert.Empty(_ran);
        Assert.False(RadialMenuState.IsOpen);
    }

    [Fact]
    public void CommandFile_IsActedOnOnce_AndOldOnesAreIgnored()
    {
        RadialMenuState.CommandFilePath = Path.Combine(_dir, "radial-menu-cmd");
        File.WriteAllText(RadialMenuState.CommandFilePath, new RadialMenuCommand { Seq = 5, Op = RadialMenuCommand.Tap, Slice = 1 }.ToJson());
        Binding().Press(null!, null!);            // opening notes the old command as already seen
        RadialMenuState.PollCommands();
        Assert.Empty(_ran);

        File.WriteAllText(RadialMenuState.CommandFilePath, new RadialMenuCommand { Seq = 6, Op = RadialMenuCommand.Tap, Slice = 0 }.ToJson());
        RadialMenuState.PollCommands();
        RadialMenuState.PollCommands();           // the same command again: not run twice
        Assert.Equal(new[] { "Undo" }, _ran);

        File.WriteAllText(RadialMenuState.CommandFilePath, new RadialMenuCommand { Seq = 7, Op = RadialMenuCommand.Close }.ToJson());
        RadialMenuState.PollCommands();
        Assert.False(RadialMenuState.IsOpen);
    }

    [Fact]
    public void CommandFile_IsPickedUpByTheTimerWhileOpen()
    {
        RadialMenuState.CommandFilePath = Path.Combine(_dir, "radial-menu-cmd");
        RadialMenuState.CommandPollMs = 10;
        Binding().Press(null!, null!);
        File.WriteAllText(RadialMenuState.CommandFilePath, new RadialMenuCommand { Seq = 1, Op = RadialMenuCommand.Tap, Slice = 0 }.ToJson());
        for (int i = 0; i < 500 && _ran.Count == 0; i++) System.Threading.Thread.Sleep(10);
        Assert.Equal(new[] { "Undo" }, _ran);
        Assert.True(RadialMenuState.IsOpen);
    }

    [Fact]
    public void RadialMenuCommand_RoundTrips_AndBadJsonIsNull()
    {
        var c = RadialMenuCommand.Parse(new RadialMenuCommand { Seq = 9, Op = RadialMenuCommand.Move, X = 1.5, Y = 2.5 }.ToJson())!;
        Assert.Equal((9L, "move", 1.5, 2.5), (c.Seq, c.Op, c.X, c.Y));
        Assert.Null(RadialMenuCommand.Parse("{not json"));
        Assert.Null(RadialMenuCommand.Parse(""));
    }

    [Fact]
    public void TheAppWritesWhatThePluginReads()
    {
        var path = Path.Combine(_dir, "radial-menu-cmd");
        var before = RadialMenuOverlay.CommandFilePath;
        RadialMenuOverlay.CommandFilePath = path;
        try
        {
            RadialMenuOverlay.SendCommand(RadialMenuCommand.Tap, 3);
            var a = RadialMenuCommand.Parse(File.ReadAllText(path))!;
            RadialMenuOverlay.SendCommand(RadialMenuCommand.Centre);
            var b = RadialMenuCommand.Parse(File.ReadAllText(path))!;
            Assert.Equal((RadialMenuCommand.Tap, 3), (a.Op, a.Slice));
            Assert.Equal(RadialMenuCommand.Centre, b.Op);
            Assert.True(b.Seq > a.Seq);
        }
        finally { RadialMenuOverlay.CommandFilePath = before; }
    }

    // ── The centre's four buttons (TASKS #54), with the pen ──

    private JsonElement Req() => State().GetProperty("request");

    [Theory]
    [InlineData(-30, -30)]   // the X
    [InlineData(0, 0)]
    public void Pen_TheX_Closes(double dx, double dy)
    {
        Binding().Press(null!, null!);
        TapAt(dx, dy);
        Assert.False(RadialMenuState.IsOpen);
        Assert.Empty(_ran);
    }

    [Fact]
    public void Pen_TheHand_NeverCloses_AndATapWithoutMovingDoesNothing()
    {
        Binding().Press(null!, null!);
        double cx = 500, cy = 400;
        TapAt(30, 30);                       // bottom right: the hand
        RadialMenuState.PenMoved();          // no movement
        RadialMenuState.TipUp();
        Assert.True(RadialMenuState.IsOpen);
        Assert.Equal(cx, State().GetProperty("x").GetDouble());
        Assert.Equal(cy, State().GetProperty("y").GetDouble());
        Assert.Empty(_ran);
    }

    [Fact]
    public void Pen_DraggingTheHand_MovesTheMenu_AfterAFewPoints_AndParksIt()
    {
        Binding().Press(null!, null!);
        TapAt(30, 30);                       // pen down on the hand at (530,430)
        _cursor = (532, 431);                // 2 pt: still not a drag
        RadialMenuState.PenMoved();
        Assert.Equal(500, State().GetProperty("x").GetDouble());
        _cursor = (630, 380);                // moved by (+100, -50)
        RadialMenuState.PenMoved();
        RadialMenuState.TipUp();             // writes the final place
        Assert.Equal(600, State().GetProperty("x").GetDouble());
        Assert.Equal(350, State().GetProperty("y").GetDouble());
        Assert.True(RadialMenuState.IsOpen);
        // and the pen's taps are now measured from the new centre
        _cursor = (600, 250);
        RadialMenuState.TipDown();
        Assert.Equal(new[] { "Undo" }, _ran);
    }

    [Fact]
    public void Pen_Pencil_ThenASlice_AsksTheAppToEditIt_AndTheMenuStaysOpen()
    {
        var b = Binding();
        b.Press(null!, null!);
        b.Release(null!, null!);
        TapAt(30, -30);                      // the pencil
        Assert.True(RadialMenuState.Editing);
        Assert.True(State().GetProperty("editing").GetBoolean());
        TapAt(110, 0);                       // slice 2 (Copy): edited, not run
        Assert.Empty(_ran);
        Assert.True(RadialMenuState.IsOpen);
        Assert.False(RadialMenuState.Editing);
        Assert.Equal("edit", Req().GetProperty("kind").GetString());
        Assert.Equal(2, Req().GetProperty("slice").GetInt32());
        Assert.True(RadialMenuState.Busy);
        Assert.False(RadialMenuState.SwallowsPen);   // the pen can click in the app's editor meanwhile
    }

    [Fact]
    public void PencilTappedTwice_PutsItDown()
    {
        Binding().Press(null!, null!);
        TapAt(30, -30);
        TapAt(30, -30);
        Assert.False(RadialMenuState.Editing);
    }

    [Fact]
    public void TheAppsAnswer_ChangesTheOpenMenu_AndLetsThePenBackIn()
    {
        Binding().Press(null!, null!);
        TapAt(30, -30);
        TapAt(0, -110);                      // edit slice 0 (Undo)
        var m = RadialMenuDefaults.Create();
        m.Slices[0] = new RadialSlice { Label = "Save", Action = RadialActions.Keys, Keys = "Application+S" };
        Mouse(RadialMenuCommand.MenuChanged);   // unchanged (cancelled): no menu given
        Assert.True(RadialMenuState.SwallowsPen);
        TapAt(30, -30);
        TapAt(0, -110);
        RadialMenuState.HandleCommand(new RadialMenuCommand { Seq = 2, Op = RadialMenuCommand.MenuChanged, Menu = RadialMenuJson.Serialize(m) });
        Assert.Equal("Save", State().GetProperty("slices")[0].GetProperty("label").GetString());
        Assert.Equal(RadialMenuJson.Serialize(m), State().GetProperty("menu").GetString());
        TapAt(0, -110);
        Assert.Equal(new[] { "Save" }, _ran);
    }

    [Fact]
    public void Pen_CircularArrow_AsksYesNo_YesRestores_AnythingElseCancels()
    {
        var b = Binding();
        b.Press(null!, null!);
        b.Release(null!, null!);
        TapAt(-30, 30);                      // the circular arrow
        Assert.True(RadialMenuState.Confirming);
        Assert.True(State().GetProperty("confirm").GetBoolean());
        // only Yes (right, slice 2) and No (left, slice 6) are live on the ring
        var slices = State().GetProperty("slices");
        for (int i = 0; i < 8; i++) Assert.Equal(i == 2 || i == 6, slices[i].GetProperty("set").GetBoolean());
        TapAt(0, -110);                      // somewhere else: cancel
        Assert.False(RadialMenuState.Confirming);
        Assert.False(State().TryGetProperty("request", out var none) && none.ValueKind == JsonValueKind.Object);
        Assert.Empty(_ran);

        TapAt(-30, 30);
        TapAt(110, 0);                       // Yes
        Assert.Equal("restore", Req().GetProperty("kind").GetString());
        Assert.False(RadialMenuState.Confirming);
        Assert.True(RadialMenuState.IsOpen);
    }

    [Fact]
    public void Mouse_PencilAndArrow_DoWhatThePenDoes()
    {
        Binding().Press(null!, null!);
        Mouse(RadialMenuCommand.Edit);
        Assert.True(RadialMenuState.Editing);
        Mouse(RadialMenuCommand.Tap, 3);
        Assert.Equal("edit", Req().GetProperty("kind").GetString());
        Mouse(RadialMenuCommand.MenuChanged);
        Mouse(RadialMenuCommand.Restore);
        Assert.True(RadialMenuState.Confirming);
        Mouse(RadialMenuCommand.Tap, 6);     // No
        Assert.False(RadialMenuState.Confirming);
        Assert.True(RadialMenuState.IsOpen);
        Assert.Empty(_ran);
    }

    // ── TASKS #65: the driver reports which slice ran and whether it worked ──

    [Fact]
    public void APenTap_ReportsTheSlice_AndTheMenuStaysOpen()
    {
        var b = Binding(); b.Press(null!, null!); b.Release(null!, null!);
        TapAt(110, 0);                                        // Copy
        var st = State();
        Assert.Equal("open", st.GetProperty("state").GetString());   // V29 #131
        var used = st.GetProperty("used");
        Assert.Equal("Copy", used.GetProperty("label").GetString());
        Assert.Equal("Application+C", used.GetProperty("keys").GetString());
        Assert.True(used.GetProperty("ok").GetBoolean());
        Assert.True(used.GetProperty("seq").GetInt64() >= 1);
        // the app's reader sees the same
        var parsed = RadialMenuOverlay.Parse(File.ReadAllText(RadialMenuState.StateFilePath))!;
        Assert.Equal("Copy", parsed.Used!.Label); Assert.True(parsed.Used.Ok);
        Assert.Equal("Selection copied", RadialMenuConfirm.Text((k, e) => e, parsed.Used.Label, parsed.Used.Keys, parsed.Used.Ok));
    }

    [Fact]
    public void WhileTheMenuStaysOpen_EachRunHasItsOwnSeq_AndTheMouseReportsToo()
    {
        var b = Binding(); b.Press(null!, null!);              // button held: stays open
        TapAt(0, -100);                                         // Undo
        var first = State().GetProperty("used").GetProperty("seq").GetInt64();
        Assert.Equal("open", State().GetProperty("state").GetString());
        TapAt(0, -100);
        var second = State().GetProperty("used").GetProperty("seq").GetInt64();
        Assert.True(second > first);
        Mouse(RadialMenuCommand.Tap, 3);                        // the Mac mouse: Paste
        var used = State().GetProperty("used");
        Assert.Equal("Paste", used.GetProperty("label").GetString());
        Assert.True(used.GetProperty("seq").GetInt64() > second);
    }

    [Fact]
    public void ASliceThatCannotRun_IsReportedAsFailed_NotAsDone()
    {
        RadialMenuState.ActionRunner = null;                    // the real runner: this binding has no keyboard to press keys with
        var menu = RadialMenuDefaults.Create();
        menu.Slices[0] = new RadialSlice { Label = "Brush", Action = RadialActions.Keys, Keys = "B" };
        var b = Binding(menu); b.Press(null!, null!); b.Release(null!, null!);
        TapAt(0, -100);
        var used = State().GetProperty("used");
        Assert.Equal("Brush", used.GetProperty("label").GetString());
        Assert.False(used.GetProperty("ok").GetBoolean());
        Assert.Equal("Brush did not work", RadialMenuConfirm.Text((k, e) => e, "Brush", "B", false));
    }

    [Fact]
    public void ANewSession_StartsWithoutTheLastRun()
    {
        var b = Binding(); b.Press(null!, null!); b.Release(null!, null!);
        TapAt(0, -100);                                         // runs Undo
        TapAt(0, 0);                                            // the X closes
        b.Press(null!, null!);                                  // opens again
        Assert.Equal("open", State().GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, State().GetProperty("used").ValueKind);
    }

    [Fact]
    public void ASubmenuOrEmptySlice_ReportsNothing()
    {
        var b = Binding(); b.Press(null!, null!);
        var menu = RadialMenuDefaults.Create();
        var sub = new RadialMenuDef { Slices = { new RadialSlice { Label = "Brush", Action = RadialActions.Keys, Keys = "B" } } };
        menu.Slices[2] = new RadialSlice { Label = "Tools", Action = RadialActions.Submenu, Submenu = sub };
        RadialMenuState.ResetForTests();
        RadialMenuState.StateFilePath = Path.Combine(_dir, "radial-menu");
        RadialMenuState.CursorProvider = () => _cursor; RadialMenuState.ActionRunner = (_, s) => _ran.Add(s.Label);
        var b2 = Binding(menu); b2.Press(null!, null!);
        TapAt(100, 0);                                          // Tools: only opens the submenu
        Assert.Equal(JsonValueKind.Null, State().GetProperty("used").ValueKind);
        Assert.Empty(_ran);
    }

    [Fact]
    public void EditMode_DoesNotReportASliceRun()
    {
        var b = Binding(); b.Press(null!, null!); b.Release(null!, null!);
        Mouse(RadialMenuCommand.Edit);
        Mouse(RadialMenuCommand.Tap, 2);                        // edits Copy, does not run it
        Assert.Empty(_ran);
        Assert.Equal(JsonValueKind.Null, State().GetProperty("used").ValueKind);
    }
}

public class RadialMenuEditorTests
{
    [Fact]
    public void Edits_NameAndKeys_AndSaves()
    {
        var vm = new RadialMenuEditorViewModel(null, "Pen button 1");
        vm.SelectSlice(4);
        vm.SliceLabel = "Brush";
        vm.ActionIndex = RadialMenuEditorViewModel.ActClick;
        vm.ClickIndex = 1;   // Right
        vm.SaveCommand.Execute(null);
        var m = RadialMenuJson.Parse(vm.Result);
        Assert.Equal("Brush", m.Slices[4].Label);
        Assert.Equal(RadialActions.Click, m.Slices[4].Action);
        Assert.Equal("Right", m.Slices[4].Click);
        Assert.Equal("Undo", m.Slices[0].Label);
    }

    [Fact]
    public void Submenus_EditOneRingAtATime_UpToTheLimit()
    {
        var vm = new RadialMenuEditorViewModel(null, "ExpressKey 1");
        vm.SelectSlice(2);
        vm.ActionIndex = RadialMenuEditorViewModel.ActSubmenu;
        vm.EditSubmenuCommand.Execute(null);
        Assert.True(vm.InSubmenu);
        vm.SelectSlice(0);
        vm.SliceLabel = "Inner";
        vm.ActionIndex = RadialMenuEditorViewModel.ActSubmenu;
        vm.EditSubmenuCommand.Execute(null);              // level 3
        Assert.False(vm.CanAddSubmenu);
        vm.ActionIndex = RadialMenuEditorViewModel.ActSubmenu;   // refused at the deepest level
        Assert.NotEqual(RadialMenuEditorViewModel.ActSubmenu, vm.ActionIndex);
        vm.BackCommand.Execute(null);
        vm.BackCommand.Execute(null);
        Assert.False(vm.InSubmenu);
        Assert.Equal(2, vm.SelectedSlice);
        Assert.Equal("Inner", vm.Menu.Slices[2].Submenu!.Slices[0].Label);
    }

    [Fact]
    public void ClearAndReset()
    {
        var vm = new RadialMenuEditorViewModel(null, "x");
        vm.ClearSliceCommand.Execute(null);
        Assert.False(vm.Menu.Slices[0].IsSet);
        vm.ResetCommand.Execute(null);
        Assert.True(vm.Menu.Slices[0].IsSet);
    }

    [Fact]
    public void SavedSizeAbove50_IsUsedAs50_NeverBiggerThan320()
    {
        var m = RadialMenuDefaults.Create(); m.Size = 50;
        var json = RadialMenuJson.Serialize(m).Replace("\"size\":50", "\"size\":100");
        Assert.Contains("\"size\":100", json);
        Assert.Equal(50, RadialMenuJson.Parse(json).Size);
        var vm = new RadialMenuEditorViewModel(json, "x");
        Assert.Equal(50, vm.Size);
        Assert.Equal(320, vm.PreviewDiameter, 1);
        vm.Size = 100;                                    // even if something sets more, the menu and the number stay at the largest
        Assert.Equal(320, vm.PreviewDiameter, 1);
        Assert.Equal("10", vm.SizeText);                  // v27 (Zak 16:07): shown as a step 1-10; 10 is the largest
        Assert.Equal("10", vm.SizeInput);
        Assert.Equal(50, RadialMenuJson.Parse(vm.CurrentJson()).Size);
    }

    [Fact]
    public void SizeSlider_ChangesThePreviewAtOnce_AndResetPutsItBackTo50()
    {
        var vm = new RadialMenuEditorViewModel(null, "x");
        var seen = new System.Collections.Generic.List<string>();
        vm.PropertyChanged += (_, e) => seen.Add(e.PropertyName!);
        Assert.Equal(320, vm.PreviewDiameter, 1);
        Assert.Equal("10", vm.SizeText);
        Assert.Equal("10", vm.SizeInput);
        vm.Size = 25;
        Assert.Equal(2 * RadialMenuGeometry.OuterRadius(25), vm.PreviewDiameter, 1);
        Assert.Equal("5", vm.SizeText);                   // size 25 is shown as step 5
        Assert.Equal("5", vm.SizeInput);
        Assert.Contains(nameof(vm.PreviewDiameter), seen);
        Assert.Contains(nameof(vm.SizeText), seen);
        Assert.Contains(nameof(vm.SizeInput), seen);
        vm.Size = 1;
        Assert.True(vm.PreviewDiameter < 320);
        Assert.Equal("1", vm.SizeInput);
        vm.ResetCommand.Execute(null);
        Assert.Equal(50, vm.Size);
        Assert.Equal(320, vm.PreviewDiameter, 1);
        Assert.Equal("10", vm.SizeText);
        Assert.Equal("10", vm.SizeInput);
    }

    [Theory]
    // v27 (Zak 16:07): typed as a step 1-10; the saved size is 1-50 (step 5 = 23, 7 = 34, 3 = 12, 10 = 50).
    [InlineData("5", 23, "5")]
    [InlineData(" 7 ", 34, "7")]
    [InlineData("2,6", 12, "3")]
    [InlineData("1", 1, "1")]
    [InlineData("10", 50, "10")]
    [InlineData("87", 50, "10")]       // clamped to 1-10 (10 = size 50 = 320 pt)
    [InlineData("0", 1, "1")]
    [InlineData("-9", 1, "1")]
    [InlineData("abc", 50, "10")]      // not a number: the old value comes back
    [InlineData("", 50, "10")]
    public void Size_TypedInTheNumberBox_IsClamped_AndTheSliderValueFollows(string typed, int expected, string shown)
    {
        var vm = new RadialMenuEditorViewModel(null, "x");   // starts at the default, 50
        vm.SizeInput = typed;
        Assert.Equal(expected, vm.Size);                    // the slider is bound to this value
        Assert.Equal(shown, vm.SizeInput);
        Assert.Equal(shown, vm.SizeText);
        vm.SaveCommand.Execute(null);
        Assert.Equal(expected, RadialMenuJson.Parse(vm.Result!).Size);
    }

    [Fact]
    public void Size_SliderMovesTheNumberBox()
    {
        var vm = new RadialMenuEditorViewModel(null, "x");
        vm.Size = 12;
        Assert.Equal("3", vm.SizeInput);
        Assert.Equal("3", vm.SizeText);
        vm.SizeStep = 7;                                   // the sliders are bound to the step
        Assert.Equal(34, vm.Size);
        Assert.Equal("7", vm.SizeInput);
    }

    /// <summary>v27 test (Zak 13 Nov 16:07): "the menu size should go from 1 to 10 within the same values of 1 to 50".</summary>
    [Fact]
    public void SizeSteps_1To10_SpanTheSameSizes_1To50_AndRoundTrip()
    {
        Assert.Equal(1, RadialMenuDef.SizeForStep(1));
        Assert.Equal(50, RadialMenuDef.SizeForStep(10));
        int previous = 0;
        for (int step = 1; step <= 10; step++)
        {
            int size = RadialMenuDef.SizeForStep(step);
            Assert.True(size > previous, $"step {step} gives {size}, not bigger than {previous}");
            Assert.Equal(step, RadialMenuDef.StepForSize(size));
            previous = size;
        }
        Assert.Equal(1, RadialMenuDef.SizeForStep(-3));
        Assert.Equal(50, RadialMenuDef.SizeForStep(99));
        Assert.Equal(10, RadialMenuDef.StepForSize(50));
        Assert.Equal(1, RadialMenuDef.StepForSize(1));
    }

    [Fact]
    public void OpacityFor_IsSolidAt0_AndKeepsAUsableFloorAt100()
    {
        // v27 test (Zak 13 Nov 16:01): "60% should be the maximum" - the fading scale is unchanged, the limit is 60.
        Assert.Equal(0.10, RadialMenuDef.MinVisibleOpacity);
        Assert.Equal(60, RadialMenuDef.MaxTransparency);
        Assert.Equal(1.0, RadialMenuDef.OpacityFor(0));
        Assert.Equal(0.46, RadialMenuDef.OpacityFor(60), 2);   // what Zak saw at 60% on the V27 test
        Assert.Equal(0.46, RadialMenuDef.OpacityFor(100), 2);  // stops at the limit
        Assert.Equal(0.64, RadialMenuDef.OpacityFor(40), 2);   // default 40% → 64% opaque (36% see-through)
        Assert.Equal(1.0, RadialMenuDef.OpacityFor(-5));
        Assert.Equal(0.46, RadialMenuDef.OpacityFor(200), 2);
        Assert.True(RadialMenuDef.OpacityFor(100) < RadialMenuDef.OpacityFor(40));
        Assert.True(RadialMenuDef.OpacityFor(40) < RadialMenuDef.OpacityFor(0));
    }

    [Fact]
    public void CentreHintFor_MatchesWhatTheRealButtonsDo()
    {
        Assert.Equal("Closes the menu.", RadialMenuEditorViewModel.CentreHintFor(RadialMenuGeometry.Centre));
        Assert.Equal("Turns on edit mode: then tap a slice to choose what it does.", RadialMenuEditorViewModel.CentreHintFor(RadialMenuGeometry.Edit));
        Assert.Equal("Asks whether to restore the 8 default slices.", RadialMenuEditorViewModel.CentreHintFor(RadialMenuGeometry.Restore));
        Assert.Equal("Drag to move the menu.", RadialMenuEditorViewModel.CentreHintFor(RadialMenuGeometry.Hand));
        Assert.Equal("", RadialMenuEditorViewModel.CentreHintFor(0));   // a slice is not a centre button
        Assert.Equal("X", RadialMenuEditorViewModel.CentreHintLogName(RadialMenuGeometry.Centre));
        Assert.Equal("pencil", RadialMenuEditorViewModel.CentreHintLogName(RadialMenuGeometry.Edit));
        Assert.Equal("circular arrow", RadialMenuEditorViewModel.CentreHintLogName(RadialMenuGeometry.Restore));
        Assert.Equal("hand", RadialMenuEditorViewModel.CentreHintLogName(RadialMenuGeometry.Hand));
    }

    [Fact]
    public void ExplainCentre_SetsTheHint_Logs_AndDoesNotChangeASlice()
    {
        var vm = new RadialMenuEditorViewModel(null, "x");
        int slice = vm.SelectedSlice;
        var label = vm.SliceLabel;
        var lines = new System.Collections.Generic.List<string>();
        void H(string l) => lines.Add(l);
        AppLog.LineWritten += H;
        try
        {
            Assert.False(vm.HasCentreHint);
            vm.ExplainCentre(RadialMenuGeometry.Edit);
            Assert.Equal("Turns on edit mode: then tap a slice to choose what it does.", vm.CentreHint);
            Assert.True(vm.HasCentreHint);
            Assert.Equal(slice, vm.SelectedSlice);
            Assert.Equal(label, vm.SliceLabel);
            Assert.Contains(lines, l => l.Contains("the pencil in the picture was clicked") && l.Contains("Turns on edit mode: then tap a slice to choose what it does."));
        }
        finally { AppLog.LineWritten -= H; }
    }

    [Fact]
    public void Transparency_DefaultsTo40_IsSavedWithTheMenu_AndResetBringsItBack()
    {
        Assert.Equal(40, RadialMenuDef.DefaultTransparency);   // TASKS #59 (Zak 18:21): was 70
        Assert.Equal(40, RadialMenuDefaults.Create().Transparency);
        Assert.Equal(40, new RadialMenuDef().Transparency);
        var vm = new RadialMenuEditorViewModel(null, "x");
        Assert.Equal(40, vm.Transparency);
        Assert.Equal("40%", vm.TransparencyText);
        Assert.Equal("40", vm.TransparencyInput);
        vm.Transparency = 20;
        Assert.Equal("20%", vm.TransparencyText);
        Assert.Equal("20", vm.TransparencyInput);
        vm.SaveCommand.Execute(null);
        Assert.Equal(20, RadialMenuJson.Parse(vm.Result).Transparency);
        Assert.Contains("\"transparency\":20", vm.Result);

        var vm2 = new RadialMenuEditorViewModel(vm.Result, "x");
        Assert.Equal(20, vm2.Transparency);
        vm2.ResetCommand.Execute(null);
        Assert.Equal(40, vm2.Transparency);
        Assert.Equal("40", vm2.TransparencyInput);
    }

    [Theory]
    [InlineData("55", 55, "55")]
    [InlineData(" 7 ", 7, "7")]
    [InlineData("33%", 33, "33")]
    [InlineData("12,6", 13, "13")]
    [InlineData("0", 0, "0")]
    [InlineData("100", 60, "60")]
    [InlineData("250", 60, "60")]       // clamped to 0-60 (v27: 60% is the maximum)
    [InlineData("-9", 0, "0")]
    [InlineData("abc", 40, "40")]       // not a number: the old value comes back
    [InlineData("", 40, "40")]
    public void Transparency_TypedInTheNumberBox_IsClamped_AndTheSliderValueFollows(string typed, int expected, string shown)
    {
        var vm = new RadialMenuEditorViewModel(null, "x");   // starts at the default, 40
        vm.TransparencyInput = typed;
        Assert.Equal(expected, vm.Transparency);              // the slider is bound to this value
        Assert.Equal(shown, vm.TransparencyInput);
        Assert.Equal(shown + "%", vm.TransparencyText);
        vm.SaveCommand.Execute(null);
        Assert.Equal(expected, RadialMenuJson.Parse(vm.Result).Transparency);
    }

    [Fact]
    public void Transparency_SliderMovesTheNumberBox()
    {
        var vm = new RadialMenuEditorViewModel(null, "x");
        vm.Transparency = 53;
        Assert.Equal("53", vm.TransparencyInput);
        vm.Transparency = 83;                                  // above the 60% limit
        Assert.Equal(60, vm.Transparency);
        Assert.Equal("60", vm.TransparencyInput);
    }

    [Fact]
    public void Transparency_OfAnOldMenuWithoutIt_IsTheDefault_AndIsClamped()
    {
        Assert.Equal(40, RadialMenuJson.Parse("{\"slices\":[],\"size\":50}").Transparency);
        Assert.Equal(60, RadialMenuJson.Parse("{\"slices\":[],\"transparency\":900}").Transparency);   // v27: limit 60
        Assert.Equal(0, RadialMenuJson.Parse("{\"slices\":[],\"transparency\":-5}").Transparency);
        var menu = RadialMenuDefaults.Create(); menu.Transparency = 33;
        Assert.Equal(33, menu.Clone().Transparency);
    }

    [Fact]
    public void Cancel_ReturnsNothing()
    {
        var vm = new RadialMenuEditorViewModel(null, "x");
        vm.SliceLabel = "changed";
        vm.CancelCommand.Execute(null);
        Assert.Null(vm.Result);
    }

    [Fact]
    public void BindingEditor_OffersTheRadialTab_ExceptForKeysOnly()
    {
        var vm = new BindingEditorViewModel(AuxBinding.Unbound, "ExpressKey 1");
        Assert.True(vm.ShowRadialTab);
        vm.IsRadialTab = true;
        vm.SaveCommand.Execute(null);
        Assert.Equal(AuxKind.RadialMenu, vm.Result!.Kind);

        using (BindingEditorContext.KeysOnlyScope())
            Assert.False(new BindingEditorViewModel(AuxBinding.Unbound, "Touch Ring mode").ShowRadialTab);
        Assert.True(new BindingEditorViewModel(AuxBinding.Unbound, "again").ShowRadialTab);
    }

    [Fact]
    public void BindingEditor_OpensOnTheRadialTab_ForARadialBinding()
    {
        var vm = new BindingEditorViewModel(AuxBinding.RadialMenu(null), "Pen button 2");
        Assert.True(vm.IsRadialTab);
        Assert.Contains("Undo", vm.RadialSummary);
    }
}
