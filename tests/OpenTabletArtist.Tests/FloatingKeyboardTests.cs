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
[Collection("FloatingKeyboardFiles")]
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

    [Theory]
    [InlineData(KeyboardShape.Ansi)]
    [InlineData(KeyboardShape.Iso)]
    public void Layout_IsCompact_FiveRowsThatLineUp(KeyboardShape shape)
    {
        var rows = FloatingKeyboardLayout.RowsFor(shape);
        Assert.Equal(5, rows.Count);
        foreach (var row in rows)
            Assert.Equal(FloatingKeyboardLayout.RowUnits, row.Sum(k => k.Units), 3);
        var all = rows.SelectMany(r => r).ToList();
        foreach (var letter in "ABCDEFGHIJKLMNOPQRSTUVWXYZ") Assert.Contains(all, k => k.Value == letter.ToString());
        foreach (var v in new[] { "Space", "Enter", "Backspace", "Escape", "Tab", "Up", "Down", "Left", "Right", "D0", "D9", "Grave", "Backslash" })
            Assert.Contains(all, k => k.Value == v);
        // Zak 20 Nov (build 7): Caps Lock where a Mac has it, and a Shift at each end of the Z row (Del is gone).
        Assert.Equal(new[] { "Caps", "Shift", "Shift", "Ctrl", "Alt", "Cmd" }, all.Where(k => k.IsModifier).Select(k => k.Value));
        Assert.Equal("Caps", rows[2][0].Value);
        Assert.Equal("Shift", rows[3][^1].Value);
        Assert.DoesNotContain(all, k => k.Value == "Delete");
        // Same key names as the Pen button window's keyboard (no new names the driver doesn't know); ISO's § key is a macOS key
        // (added to the driver for #171), not on this test machine's list.
        var known = AuxKeyBinding.Options.Select(o => o.Value).ToHashSet();
        foreach (var k in all.Where(k => !k.IsModifier && k.Value != FloatingKeyboardLayout.Section)) Assert.Contains(k.Value, known);
        // every key that types a character has a Mac key code, so its label can come from the Mac's layout
        foreach (var k in all.Where(k => !k.IsModifier && k.Value.Length == 1)) Assert.True(FloatingKeyboardLayout.MacKeyCodes.ContainsKey(k.Value), k.Value);
    }

    [Fact]
    public void IsoShape_HasTheSectionKey_TheKeyLeftOfZ_AndATallEnter()
    {
        var rows = FloatingKeyboardLayout.RowsFor(KeyboardShape.Iso);
        Assert.Equal("Section", rows[0][0].Value);
        Assert.Equal("Grave", rows[3][1].Value);
        Assert.Equal("Enter", rows[1][^1].Value);
        Assert.Equal("Enter", rows[2][^1].Value);
        Assert.Equal("Grave", FloatingKeyboardLayout.RowsFor(KeyboardShape.Ansi)[0][0].Value);
    }

    /// <summary>A layout read from the Mac, for tests: British-like, or Bulgarian letters on the letter keys.</summary>
    internal sealed class FakeLabels(string id, string name, KeyboardShape shape, bool cyrillic = false) : IKeyLabels
    {
        public string Id => id;
        public string Name => name;
        public KeyboardShape Shape => shape;
        public (string Normal, string Shifted)? For(int code)
        {
            var key = FloatingKeyboardLayout.MacKeyCodes.FirstOrDefault(p => p.Value == code).Key;
            if (key == null) return null;
            if (cyrillic && key == "Q") return ("я", "Я");
            if (key.Length == 1) return (key.ToLowerInvariant(), key);
            return key switch { "D3" => ("3", "£"), "D2" => ("2", "@"), "Section" => ("§", "±"), "Grave" => ("`", "~"), _ => ("?", "?") };
        }
    }

    [Fact]
    public void Labels_FollowTheMacsLayout_SmallLettersUntilShiftOrCapsLock()
    {
        var british = new FakeLabels("com.apple.keylayout.British", "British", KeyboardShape.Iso);
        var key = (string v) => FloatingKeyboardLayout.RowsFor(KeyboardShape.Iso).SelectMany(r => r).First(k => k.Value == v);
        Assert.Equal("a", FloatingKeyboardLayout.LabelFor(key("A"), shift: false, capsLock: false, british));
        Assert.Equal("A", FloatingKeyboardLayout.LabelFor(key("A"), shift: true, capsLock: false, british));
        Assert.Equal("A", FloatingKeyboardLayout.LabelFor(key("A"), shift: false, capsLock: true, british));
        Assert.Equal("£", FloatingKeyboardLayout.LabelFor(key("D3"), shift: true, capsLock: false, british));
        Assert.Equal("3", FloatingKeyboardLayout.LabelFor(key("D3"), shift: false, capsLock: true, british));   // Caps Lock: letters only
        Assert.Equal("§", FloatingKeyboardLayout.LabelFor(key("Section"), shift: false, capsLock: false, british));
        var bulgarian = new FakeLabels("com.apple.keylayout.Bulgarian-Phonetic", "Bulgarian – QWERTY", KeyboardShape.Iso, cyrillic: true);
        Assert.Equal("я", FloatingKeyboardLayout.LabelFor(key("Q"), shift: false, capsLock: false, bulgarian));
        Assert.Equal("Я", FloatingKeyboardLayout.LabelFor(key("Q"), shift: false, capsLock: true, bulgarian));
        Assert.True(FloatingKeyboardLayout.IsLetter(key("Q"), bulgarian));
        Assert.False(FloatingKeyboardLayout.IsLetter(key("D3"), bulgarian));
        // without the Mac's layout: the US labels, letters small unless Shift / Caps Lock
        Assert.Equal("a", FloatingKeyboardLayout.LabelFor(key("A"), shift: false, capsLock: false));
        Assert.Equal("[", FloatingKeyboardLayout.LabelFor(key("LeftBracket"), shift: true, capsLock: false));
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

    /// <summary>Zak 20 Nov (build 7): "doesnt have upper case but shows as upper case letters only" and no Caps Lock.</summary>
    [Fact]
    public void LettersShowLowerCase_ShiftAndCapsLockMakeCapitals_CapsStaysOn()
    {
        var kb = new FloatingKeyboard();
        var key = (string v) => FloatingKeyboardLayout.Rows.SelectMany(r => r).First(k => k.Value == v);
        Assert.Null(kb.Tap(key("Caps")));
        Assert.True(kb.IsOn("Caps"));
        Assert.Equal("Shift+A", kb.Tap(key("A")));
        Assert.Equal("Shift+B", kb.Tap(key("B")));       // still on after a key
        Assert.Equal("D1", kb.Tap(key("D1")));           // Caps Lock leaves numbers alone, like a real one
        Assert.Null(kb.Tap(key("Caps")));
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
