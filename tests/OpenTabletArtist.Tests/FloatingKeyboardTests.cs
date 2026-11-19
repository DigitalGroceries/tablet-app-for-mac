using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using Xunit;
using DriverKeys = OpenTabletArtist.Dynamics.FloatingKeyboardKeys;

namespace OpenTabletArtist.Tests;

/// <summary>V29 #141 (Zak 19 Nov 2026): the floating keyboard the radial menu's Keyboard slice shows. The app writes each tapped key
/// to a small file; the driver presses it in the app in front, once, in order, and only while the keyboard is on screen.</summary>
public class FloatingKeyboardTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ota-kb-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _pressed = new();

    public FloatingKeyboardTests()
    {
        Directory.CreateDirectory(_dir);
        FloatingKeyboard.OpenFlagPath = DriverKeys.OpenFlagPath = Path.Combine(_dir, "keyboard-open");
        FloatingKeyboard.CommandFilePath = DriverKeys.CommandFilePath = Path.Combine(_dir, "keyboard-cmd");
        DriverKeys.ResetForTests();
        DriverKeys.KeyRunner = k => { lock (_pressed) _pressed.Add(k); };
        DriverKeys.PollMs = 3_600_000;   // the tests call Poll themselves
    }

    public void Dispose()
    {
        DriverKeys.ResetForTests();
        DriverKeys.KeyRunner = null;
        DriverKeys.PollMs = 30;
        FloatingKeyboard.OpenFlagPath = FloatingKeyboardFiles.OpenFlag;
        FloatingKeyboard.CommandFilePath = FloatingKeyboardFiles.CommandFile;
        DriverKeys.OpenFlagPath = FloatingKeyboardFiles.OpenFlag;
        DriverKeys.CommandFilePath = FloatingKeyboardFiles.CommandFile;
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Fact]
    public void Layout_IsCompact_FiveRowsThatLineUp()
    {
        Assert.Equal(5, FloatingKeyboardLayout.Rows.Count);
        foreach (var row in FloatingKeyboardLayout.Rows)
            Assert.Equal(FloatingKeyboardLayout.RowUnits, row.Sum(k => k.Units), 3);
        var all = FloatingKeyboardLayout.Rows.SelectMany(r => r).ToList();
        foreach (var letter in "ABCDEFGHIJKLMNOPQRSTUVWXYZ") Assert.Contains(all, k => k.Value == letter.ToString());
        foreach (var v in new[] { "Space", "Enter", "Backspace", "Escape", "Tab", "Up", "Down", "Left", "Right", "D0", "D9" })
            Assert.Contains(all, k => k.Value == v);
        Assert.Equal(new[] { "Shift", "Ctrl", "Alt", "Cmd" }, all.Where(k => k.IsModifier).Select(k => k.Value));
        // Same key names as the Pen button window's keyboard (no new names the driver doesn't know).
        var known = AuxKeyBinding.Options.Select(o => o.Value).ToHashSet();
        foreach (var k in all.Where(k => !k.IsModifier)) Assert.Contains(k.Value, known);
    }

    [Fact]
    public void Modifiers_ApplyToTheNextKeyOnly_CmdFirst()
    {
        var kb = new FloatingKeyboard();
        var key = (string v) => FloatingKeyboardLayout.Rows.SelectMany(r => r).First(k => k.Value == v);
        Assert.Equal("Z", kb.Tap(key("Z")));
        Assert.Null(kb.Tap(key("Cmd")));
        Assert.Null(kb.Tap(key("Shift")));
        Assert.True(kb.IsOn("Cmd"));
        Assert.Equal("Application+Shift+Z", kb.Tap(key("Z")));
        Assert.False(kb.IsOn("Cmd"));
        Assert.False(kb.IsOn("Shift"));
        Assert.Equal("Z", kb.Tap(key("Z")));
        kb.Tap(key("Ctrl")); kb.Tap(key("Ctrl"));      // tapped twice: off again
        Assert.Equal("A", kb.Tap(key("A")));
    }

    [Fact]
    public void TheDriver_PressesEachNewKeyOnce_InOrder_OnlyWhileTheKeyboardIsShown()
    {
        FloatingKeyboard.SendKeys("Q");                      // before the keyboard was opened: old, never pressed
        DriverKeys.Start();
        FloatingKeyboard.SendKeys("A");                      // no open flag yet: waits
        DriverKeys.Poll();
        Assert.Empty(_pressed);
        FloatingKeyboard.WriteOpenFlag(true);
        FloatingKeyboard.SendKeys("Application+Shift+B");
        DriverKeys.Poll();
        Assert.Equal(new[] { "A", "Application+Shift+B" }, _pressed);
        DriverKeys.Poll();                                   // nothing new: nothing pressed again
        Assert.Equal(2, _pressed.Count);
        FloatingKeyboard.WriteOpenFlag(false);               // the keyboard was hidden
        FloatingKeyboard.SendKeys("C");
        DriverKeys.Poll();
        Assert.Equal(2, _pressed.Count);
    }

    [Fact]
    public void QuickTaps_AreNeverLost_TheFileKeepsTheLast20()
    {
        FloatingKeyboard.WriteOpenFlag(true);
        DriverKeys.Start();
        for (int i = 0; i < 5; i++) FloatingKeyboard.SendKeys("D" + i);   // five taps between two looks
        DriverKeys.Poll();
        Assert.Equal(new[] { "D0", "D1", "D2", "D3", "D4" }, _pressed);
        for (int i = 0; i < 30; i++) FloatingKeyboard.SendKeys("A");
        Assert.Equal(FloatingKeyboardFiles.KeepLast, FloatingKeyboardFiles.ParseLines(File.ReadAllText(FloatingKeyboard.CommandFilePath)).Count);
    }

    [Fact]
    public void AfterADriverRestart_TheFilterFindsTheOpenKeyboard()
    {
        FloatingKeyboard.WriteOpenFlag(true);
        Assert.False(DriverKeys.Polling);
        DriverKeys.LookNow();
        Assert.True(DriverKeys.Polling);
    }

    [Fact]
    public void TheKeyboardSlice_IsAnAction_ThatSurvivesSaving_AndTheEditorOffersIt()
    {
        var m = RadialMenuDefaults.Create();
        m.Slices[7] = new RadialSlice { Label = "Keyboard", Action = RadialActions.Keyboard };
        var back = RadialMenuJson.Parse(RadialMenuJson.Serialize(m));
        Assert.Equal(RadialActions.Keyboard, back.Slices[7].Action);
        Assert.True(back.Slices[7].IsSet);

        var vm = new RadialMenuEditorViewModel(RadialMenuJson.Serialize(RadialMenuDefaults.Create()), "t");
        Assert.Equal(6, vm.ActionNames.Count);
        vm.SelectedSlice = 7;
        vm.SliceLabel = "";
        vm.ActionIndex = RadialMenuEditorViewModel.ActKeyboard;
        Assert.Equal("Keyboard", vm.SliceLabel);
        Assert.Contains("\"Keyboard\"", vm.CurrentJson());
    }

    [Fact]
    public void Transparency_DefaultsTo20_IsClamped_AndTheBoxReadsTypedValues()
    {
        var look = new FloatingKeyboardLookViewModel();
        try
        {
            AppSettings.Set(FloatingKeyboard.TransparencyKey, "");
            Assert.Equal(FloatingKeyboard.DefaultTransparency, look.Transparency);
            look.TransparencyInput = "35%";
            Assert.Equal(35, look.Transparency);
            Assert.Equal(35, FloatingKeyboard.Transparency);
            look.TransparencyInput = "500";
            Assert.Equal(60, look.Transparency);
            look.TransparencyInput = "abc";
            Assert.Equal("60", look.TransparencyInput);
            look.Transparency = -5;
            Assert.Equal(0, look.Transparency);
        }
        finally { AppSettings.Set(FloatingKeyboard.TransparencyKey, ""); }
    }
}
