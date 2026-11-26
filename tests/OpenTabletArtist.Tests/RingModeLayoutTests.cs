using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Input;
using OpenTabletArtist.Controls;
using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>V29 #158 (Digital Groceries 20 Nov): one Touch Ring switch and one menu. No "Touch Ring modes" switch or plain rows: every ring is
/// its modes, the centre button has its own menu (Next mode first), "Do nothing" is in every menu, shortcuts are typed in place,
/// and a ring that used the plain rows moves into mode 1 doing exactly what it did.</summary>
public class RingModeLayoutTests
{
    private static AuxCombo K(string key, bool cmd = false) => new(false, false, false, key, cmd);
    private static RingMode Mode(string name, string cw, string ccw) => new(name, K(cw), K(ccw));

    private static WheelBindingSettings Wheel() => new()
    {
        WheelButtons = new PluginSettingStoreCollection(new[] { (PluginSettingStore)null! }),
        ClockwiseActivationThreshold = 5, CounterClockwiseActivationThreshold = 5,
    };

    private static bool ModesSwitch(PluginSettingStore? turn) =>
        turn?.Settings.FirstOrDefault(s => s.Property == RingModeBinding.ModesSwitchProperty)?.GetValue<bool>() ?? true;

    private static readonly RingCentre Key1 = new(false, new AuxBinding(AuxKind.Keyboard, K("D1"), AuxKeyBinding.None, AuxKeyBinding.None));

    [Fact]
    public void NextMode_WritesTheModes_AndTheCentreSwitchesThem()
    {
        var w = Wheel();
        var modes = new[] { Mode("A", "RightBracket", "LeftBracket"), Mode("B", "Equal", "Minus") };
        Assert.True(RingModeBinding.ApplyLayout(w, modes, RingCentre.Next));
        Assert.True(RingModeBinding.CentreIsNext(w));
        Assert.Equal(2, RingModeBinding.Read(w).Count);
        Assert.True(ModesSwitch(w.ClockwiseRotation));
        Assert.Equal(RingCentre.Next, RingModeBinding.ReadCentre(w));
    }

    [Fact]
    public void AnotherCentreChoice_UsesOnlyModeOne_AndTheCentreGetsThatChoice()
    {
        var w = Wheel();
        var modes = new[] { Mode("A", "RightBracket", "LeftBracket"), Mode("B", "Equal", "Minus") };
        Assert.True(RingModeBinding.ApplyLayout(w, modes, Key1));
        Assert.Single(RingModeBinding.Read(w));
        Assert.Equal("RightBracket", RingModeBinding.Read(w)[0].Clockwise.Key);
        Assert.False(ModesSwitch(w.ClockwiseRotation));
        Assert.False(ModesSwitch(w.CounterClockwiseRotation));
        Assert.Equal(Key1, RingModeBinding.ReadCentre(w));
        Assert.True(RingModeBinding.IsModesLayout(w));

        // the list keeps modes 2-4 for when the centre switches modes again
        var back = RingModeBinding.LoadLayout(w, RingModeBinding.Serialize(modes));
        Assert.Equal("B", back[1].Name);
        Assert.Equal("Equal", back[1].Clockwise.Key);
    }

    [Fact]
    public void DoNothing_Everywhere_LeavesTheRingAndTheCentreEmpty()
    {
        var w = Wheel();
        var modes = new[] { Mode("A", "RightBracket", "LeftBracket") with { Enabled = false } };
        Assert.True(RingModeBinding.ApplyLayout(w, modes, RingCentre.Nothing));
        Assert.Null(w.ClockwiseRotation);
        Assert.Null(w.CounterClockwiseRotation);
        Assert.Null(w.WheelButtons[0]);
        Assert.True(RingModeBinding.IsModesLayout(w));
        var shown = RingModeBinding.LoadLayout(w, RingModeBinding.Serialize(new[] { Mode("A", "RightBracket", "LeftBracket") }));
        Assert.False(shown[0].Enabled);   // mode 1 shows Do nothing, as the ring does
    }

    [Fact]
    public void NextMode_WithEveryModeOff_TheRingDoesNothing()
    {
        var w = Wheel();
        Assert.True(RingModeBinding.ApplyLayout(w, new[] { Mode("A", "Q", "W") with { Enabled = false } }, RingCentre.Next));
        Assert.Null(w.ClockwiseRotation);
        Assert.True(RingModeBinding.CentreIsNext(w));
        Assert.All(RingModeBinding.LoadLayout(w, null), m => Assert.False(m.Enabled));
    }

    [Fact]
    public void APlainRing_MovesIntoModeOne_WithItsKeysAndItsCentre()
    {
        var w = Wheel();
        w.ClockwiseRotation = AuxKeyBinding.MakeBinding(K("Equal", cmd: true));
        w.CounterClockwiseRotation = AuxKeyBinding.MakeBinding(K("Minus", cmd: true));
        w.WheelButtons[0] = AuxKeyBinding.MakeBinding(K("D1"))!;
        Assert.False(RingModeBinding.IsModesLayout(w));
        Assert.True(RingModeBinding.TryPlainAsMode(w, out var one));
        Assert.Equal("Zoom", one.Name);                 // the same keys as the factory Zoom mode
        Assert.Equal(0f, one.StepDegrees);              // one press per ring step, as the plain rows did
        var centre = RingModeBinding.ReadCentre(w);
        var modes = RingModeBinding.WithModeOne(null, one);
        Assert.True(RingModeBinding.ApplyLayout(w, modes, centre));
        Assert.True(RingModeBinding.IsModesLayout(w));
        Assert.Equal("Equal", RingModeBinding.Read(w)[0].Clockwise.Key);
        Assert.Equal(Key1, RingModeBinding.ReadCentre(w));   // the centre button still sends 1
        Assert.Equal("Brush size", modes[2].Name);           // modes 2-4 are there for later
    }

    [Fact]
    public void APlainRingWithAClickOnATurn_KeepsItsRows()
    {
        var w = Wheel();
        w.ClockwiseRotation = AuxKeyBinding.MakeBinding(new AuxBinding(AuxKind.Mouse, AuxCombo.Unbound, "Left", AuxKeyBinding.None));
        Assert.False(RingModeBinding.TryPlainAsMode(w, out _));
    }

    [Fact]
    public void TheFunctionMenu_EndsWithDoNothing_WhichIsTheModesOff()
    {
        var changes = 0;
        var row = new RingModeRow(1, Mode("A", "RightBracket", "LeftBracket"), canEdit: true, editBinding: null,
            changed: () => { changes++; return Task.CompletedTask; });
        Assert.Equal(RingModeRow.ActionNames.Count - 1, RingModeRow.DoNothingIndex);
        Assert.Equal("Do nothing", RingModeRow.ActionNames[^1]);
        row.ActionIndex = RingModeRow.DoNothingIndex;
        Assert.False(row.Enabled);
        Assert.Equal("Do nothing", row.ActionShort);
        Assert.False(row.HasSpeed);
        Assert.False(row.UsesKeys);
        Assert.Equal(1, changes);
        row.ActionIndex = (int)RingAction.Keys;     // back: on again, keys kept
        Assert.True(row.Enabled);
        Assert.Equal("RightBracket", row.ToMode().Clockwise.Key);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void TheCentreMenu_StartsWithNextMode_AndAChoiceIsWrittenWithTheModes()
    {
        RingCentre? written = null;
        var editor = new RingModesEditor(0, new[] { Mode("A", "Q", "W"), Mode("B", "E", "R") }, isOn: true, hasRingButton: true,
            canEdit: true, editBinding: null, setOn: null, changed: null, centre: RingCentre.Next,
            setCentre: (c, _) => { written = c; return Task.FromResult(true); });
        Assert.Equal("Next mode", editor.CentreChoices[0]);
        Assert.Equal("Do nothing", editor.CentreChoices[1]);
        Assert.Equal(RingModesEditor.CentreNextIndex, editor.CentreIndex);
        Assert.All(editor.Rows, r => Assert.True(r.IsReachable));

        editor.CentreIndex = RingModesEditor.CentreNothingIndex;
        Assert.Equal(RingCentre.Nothing, written);
        Assert.False(editor.SwitchesModes);
        Assert.True(editor.Rows[0].IsReachable);
        Assert.False(editor.Rows[1].IsReachable);             // dimmed: only mode 1 is used
        Assert.Contains("mode 1", editor.Summary);

        // Keyboard shortcut…: the box shows and nothing is written until keys are pressed in it
        written = null;
        editor.CentreIndex = RingModesEditor.CentreKeysIndex;
        Assert.True(editor.ShowCentreKeys);
        Assert.Null(written);
        editor.SetCentreKeysCommand.Execute(K("Z", cmd: true));
        Assert.Equal(AuxKind.Keyboard, written!.Binding.Kind);
        Assert.Equal("Z", written.Binding.Combo.Key);
        Assert.Equal(RingModesEditor.CentreKeysIndex, editor.CentreIndex);
        Assert.NotEqual("", editor.CentreKeysText);
    }

    [Fact]
    public void ACentreChoiceTheDriverRefused_ShowsTheOldChoiceAgain()
    {
        var editor = new RingModesEditor(0, new[] { Mode("A", "Q", "W") }, isOn: true, hasRingButton: true, canEdit: true,
            editBinding: null, setOn: null, changed: null, centre: RingCentre.Next, setCentre: (_, _) => Task.FromResult(false));
        editor.CentreIndex = RingModesEditor.CentreNothingIndex;
        Assert.Equal(RingModesEditor.CentreNextIndex, editor.CentreIndex);
    }

    [Theory]
    [InlineData(Key.Z, KeyModifiers.Meta, "Z", false, false, false, true)]
    [InlineData(Key.OemCloseBrackets, KeyModifiers.None, "RightBracket", false, false, false, false)]
    [InlineData(Key.OemPlus, KeyModifiers.Meta | KeyModifiers.Shift, "Equal", false, true, false, true)]
    [InlineData(Key.F13, KeyModifiers.Alt, "F13", false, false, true, false)]
    [InlineData(Key.D5, KeyModifiers.Control, "D5", true, false, false, false)]
    public void TheShortcutBox_TakesTheKeysPressed(Key key, KeyModifiers mods, string name, bool ctrl, bool shift, bool alt, bool cmd) =>
        Assert.Equal(new AuxCombo(ctrl, shift, alt, name, cmd), KeyCaptureBox.Capture(key, mods));

    [Fact]
    public void TheShortcutBox_EmptiesOnBackspace_AndIgnoresAModifierAlone()
    {
        Assert.Equal(AuxCombo.Unbound, KeyCaptureBox.Capture(Key.Back, KeyModifiers.None));
        Assert.Null(KeyCaptureBox.Capture(Key.LeftShift, KeyModifiers.Shift));
        Assert.Equal("Backspace", KeyCaptureBox.Capture(Key.Back, KeyModifiers.Meta)!.Key);
    }

    [Fact]
    public void AShortcutTypedIntoAModesBox_IsTheModesKeys()
    {
        var row = new RingModeRow(1, new RingMode("A", AuxCombo.Unbound, AuxCombo.Unbound), canEdit: true, editBinding: null,
            changed: () => Task.CompletedTask);
        row.Clockwise.SetKeysCommand.Execute(K("RightBracket"));
        Assert.Equal("RightBracket", row.ToMode().Clockwise.Key);
        Assert.NotEqual("", row.Clockwise.KeysText);
        row.Clockwise.SetKeysCommand.Execute(AuxCombo.Unbound);
        Assert.False(row.ToMode().Clockwise.IsBound);
    }
}
