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
        Assert.True(RadialMenuGeometry.OuterRadius(100) > RadialMenuGeometry.OuterRadius(1));
        Assert.Equal(160, RadialMenuGeometry.OuterRadius(50), 3);
        Assert.Equal(RadialMenuGeometry.Outside, RadialMenuGeometry.SliceAt(0, -200, 1));
        Assert.Equal(0, RadialMenuGeometry.SliceAt(0, -200, 100));
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
        m.Size = 80;
        m.Slices[3] = new RadialSlice { Label = "Tools", Action = RadialActions.Submenu, Submenu = RadialMenuDefaults.Create() };
        m.Slices[4] = new RadialSlice { Label = "Safari", Action = RadialActions.Open, App = "com.apple.Safari" };
        m.Slices[5] = new RadialSlice { Label = "Right", Action = RadialActions.Click, Click = "Right" };
        var back = RadialMenuJson.Parse(RadialMenuJson.Serialize(m));
        Assert.Equal(80, back.Size);
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
    public void SizeSlider_ChangesThePreviewAtOnce_AndResetPutsItBackTo50()
    {
        var vm = new RadialMenuEditorViewModel(null, "x");
        var seen = new System.Collections.Generic.List<string>();
        vm.PropertyChanged += (_, e) => seen.Add(e.PropertyName!);
        Assert.Equal(320, vm.PreviewDiameter, 1);
        Assert.Equal("50", vm.SizeText);
        vm.Size = 100;
        Assert.Equal(2 * RadialMenuGeometry.OuterRadius(100), vm.PreviewDiameter, 1);
        Assert.Equal("100", vm.SizeText);
        Assert.Contains(nameof(vm.PreviewDiameter), seen);
        Assert.Contains(nameof(vm.SizeText), seen);
        vm.Size = 1;
        Assert.True(vm.PreviewDiameter < 320);
        vm.ResetCommand.Execute(null);
        Assert.Equal(50, vm.Size);
        Assert.Equal(320, vm.PreviewDiameter, 1);
        Assert.Equal("50", vm.SizeText);
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
