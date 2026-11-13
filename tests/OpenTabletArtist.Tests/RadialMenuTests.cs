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
        RadialMenuState.IdleCloseMs = 60_000;
        RadialMenuState.CommandFilePath = Path.Combine(_dir, "radial-menu-cmd");
        RadialMenuState.CommandPollMs = 40;
    }

    public void Dispose()
    {
        RadialMenuState.ResetForTests();
        RadialMenuState.ActionRunner = null;
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private static PluginBinding Binding(RadialMenuDef? menu = null) =>
        new() { Menu = RadialMenuJson.Serialize(menu ?? RadialMenuDefaults.Create()) };

    private JsonElement State() => JsonDocument.Parse(File.ReadAllText(RadialMenuState.StateFilePath)).RootElement;

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
    public void Tap_RunsTheSlice_AndClosesWhenTheButtonIsUp()
    {
        var b = Binding();
        b.Press(null!, null!);
        b.Release(null!, null!);
        TapAt(0, -100);   // top = Undo
        Assert.Equal(new[] { "Undo" }, _ran);
        Assert.False(RadialMenuState.IsOpen);
        Assert.Equal("closed", State().GetProperty("state").GetString());
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
        TapAt(100, 0);                  // right = Copy, now it closes
        Assert.Equal("Copy", _ran.Last());
        Assert.False(RadialMenuState.IsOpen);
    }

    [Theory]
    [InlineData(0, 0)]        // the centre X
    [InlineData(0, -900)]     // outside the ring
    public void CentreOrOutside_Closes_WithoutAnAction(double dx, double dy)
    {
        var b = Binding();
        b.Press(null!, null!);
        TapAt(dx, dy);
        Assert.Empty(_ran);
        Assert.False(RadialMenuState.IsOpen);
    }

    [Fact]
    public void EmptySlice_Closes()
    {
        var m = RadialMenuDefaults.Create();
        m.Slices[2] = new RadialSlice();
        var b = Binding(m);
        b.Press(null!, null!);
        TapAt(100, 0);
        Assert.Empty(_ran);
        Assert.False(RadialMenuState.IsOpen);
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
        Assert.False(RadialMenuState.IsOpen);
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
    public void ClosesByItself_WhenLeftOpen()
    {
        RadialMenuState.IdleCloseMs = 0;
        Binding().Press(null!, null!);
        System.Threading.Thread.Sleep(5);
        RadialMenuState.CloseIfIdle();
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
        // (500,300) is far from (900,700): outside the ring, so it closes without running anything
        Assert.Empty(_ran);
        Assert.False(RadialMenuState.IsOpen);

        Binding().Press(null!, null!);
        Mouse(RadialMenuCommand.Move, x: 900, y: 700);
        _cursor = (900, 600);              // 100 points above the new centre: Undo
        RadialMenuState.TipDown();
        Assert.Equal(new[] { "Undo" }, _ran);
    }

    [Fact]
    public void ParkedMenu_DoesNotCloseByItself()
    {
        RadialMenuState.IdleCloseMs = 1;
        Binding().Press(null!, null!);
        Mouse(RadialMenuCommand.Move, x: 500, y: 400);
        System.Threading.Thread.Sleep(20);
        RadialMenuState.CloseIfIdle();
        Assert.True(RadialMenuState.IsOpen);
    }

    [Fact]
    public void UnparkedMenu_StillClosesWhenIdle()
    {
        RadialMenuState.IdleCloseMs = 1;
        Binding().Press(null!, null!);
        System.Threading.Thread.Sleep(20);
        RadialMenuState.CloseIfIdle();
        Assert.False(RadialMenuState.IsOpen);
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
        RadialMenuState.IdleCloseMs = 1;
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
        System.Threading.Thread.Sleep(20);
        RadialMenuState.CloseIfIdle();
        Assert.True(RadialMenuState.IsOpen);  // parked
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

    [Fact]
    public void WhileTheAppEdits_TheMenuDoesNotCloseByItself()
    {
        RadialMenuState.IdleCloseMs = 1;
        Binding().Press(null!, null!);
        Mouse(RadialMenuCommand.Edit);
        Mouse(RadialMenuCommand.Tap, 0);
        System.Threading.Thread.Sleep(20);
        RadialMenuState.CloseIfIdle();
        Assert.True(RadialMenuState.IsOpen);
    }

    // ── TASKS #65: the driver reports which slice ran and whether it worked ──

    [Fact]
    public void APenTapThatClosesTheMenu_ReportsTheSliceInTheClosedState()
    {
        var b = Binding(); b.Press(null!, null!); b.Release(null!, null!);
        TapAt(110, 0);                                        // Copy
        var st = State();
        Assert.Equal("closed", st.GetProperty("state").GetString());
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
        TapAt(0, -100);                                         // closes
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
