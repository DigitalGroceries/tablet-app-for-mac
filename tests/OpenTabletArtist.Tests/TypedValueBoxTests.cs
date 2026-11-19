using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>V29 #140 (Zak 19 Nov 18:37: every slider gets a box to type its value), #130 (Tip / Eraser Feel 1 to 100) and #132
/// (Theme Lightness).</summary>
public class TypedValueBoxTests
{
    [Theory]
    [InlineData("40", 40)]
    [InlineData(" 40% ", 40)]
    [InlineData("40,5", 41)]   // rounded to the box's whole numbers
    [InlineData("500", 100)]   // clamped
    [InlineData("-3", 1)]
    public void TypedValue_ReadsNumbers_ClampsAndRounds(string text, double expected)
    {
        Assert.True(TypedValue.TryParse(text, 1, 100, 0, out var v));
        Assert.Equal(expected, v);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData(null)]
    public void TypedValue_RefusesWhatIsNotANumber(string? text) => Assert.False(TypedValue.TryParse(text, 1, 100, 0, out _));

    [Fact]
    public void TypedValue_KeepsOneDecimal_WhenTheBoxHasOne()
    {
        Assert.True(TypedValue.TryParse("2.35", 0, 10, 1, out var v));
        Assert.Equal(2.4, v, 6);
        Assert.Equal("2.4", TypedValue.Format(v, 1));
    }

    [Fact]
    public void FeelScale_1IsSoftest_50IsTheStraightCurve_100IsFirmest_AndRoundTrips()
    {
        Assert.Equal(-0.9, FeelScale.FromDisplay(1), 6);
        Assert.Equal(0.0, FeelScale.FromDisplay(50), 6);
        Assert.Equal(0.9, FeelScale.FromDisplay(100), 6);
        Assert.Equal(50, FeelScale.ToDisplay(0));
        Assert.Equal(1, FeelScale.ToDisplay(-0.9));
        Assert.Equal(100, FeelScale.ToDisplay(0.9));
        for (int v = 1; v <= 100; v++) Assert.Equal(v, FeelScale.ToDisplay(FeelScale.FromDisplay(v)));
    }

    /// <summary>A guard for the future: every slider written in the app's pages has a number box next to it (the shared
    /// LabeledSlider has one in its template).</summary>
    [Fact]
    public void EverySliderInThePages_HasATypedBoxNextToIt()
    {
        var dir = Path.Combine(RepoRoot(), "OpenTabletArtist");
        var missing = new System.Collections.Generic.List<string>();
        foreach (var file in Directory.GetFiles(Path.Combine(dir, "Views"), "*.axaml"))
        {
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                if (!Regex.IsMatch(lines[i], @"<Slider\b")) continue;
                var near = string.Join("\n", lines.Skip(Math.Max(0, i - 8)).Take(24));
                if (!near.Contains("<TextBox")) missing.Add($"{Path.GetFileName(file)}:{i + 1}");
            }
        }
        Assert.True(missing.Count == 0, "sliders without a typed value box: " + string.Join(", ", missing));
        var styles = File.ReadAllText(Path.Combine(dir, "Themes", "Styles.axaml"));
        Assert.Matches(new Regex(@"<TextBox Grid\.Column=""2"" x:Name=""PART_Value"""), styles);
    }

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "OpenTabletArtist", "Views"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}

/// <summary>V29 #87 (Reddit, 17 Nov: "Can this assign volume and screen brightness shortcuts to intous dials?"): Brightness up and
/// down are on the Media tab and are posted as the Mac's own brightness-key events.</summary>
public class BrightnessKeyTests
{
    [Fact]
    public void BrightnessKeys_UseTheMacsSystemKeyNumbers_DownAndUp()
    {
        Assert.Equal(2, OpenTabletDriver.Desktop.Interop.Input.Keyboard.MacOSSystemKeys.Keys["BrightnessUp"]);
        Assert.Equal(3, OpenTabletDriver.Desktop.Interop.Input.Keyboard.MacOSSystemKeys.Keys["BrightnessDown"]);
        Assert.Equal(0x20A00, OpenTabletDriver.Desktop.Interop.Input.Keyboard.MacOSSystemKeys.Data1(2, down: true));
        Assert.Equal(0x30B00, OpenTabletDriver.Desktop.Interop.Input.Keyboard.MacOSSystemKeys.Data1(3, down: false));
    }

    [Fact]
    public void ARingStepSetToBrightness_OpensOnTheMediaTab()
    {
        var vm = new OpenTabletArtist.ViewModels.BindingEditorViewModel(
            new AuxBinding(AuxKind.Keyboard, new AuxCombo(false, false, false, "BrightnessUp"), AuxKeyBinding.None, AuxKeyBinding.None), "Clockwise");
        Assert.Equal(OpenTabletArtist.ViewModels.BindingEditorViewModel.MediaTab, vm.SelectedTabIndex);
        Assert.Equal("BrightnessUp", vm.SelectedMediaKey);
    }
}

/// <summary>V29 #143 (Zak's build 3 test, 'No floating keyboard menu'): the keyboard goes on the menu in one click from the
/// pen button's Radial menu tab.</summary>
public class KeyboardOnTheMenuTests
{
    [Fact]
    public void PutKeyboard_PutsItOnTheChosenSlice_OnlyOnce()
    {
        var vm = new OpenTabletArtist.ViewModels.BindingEditorViewModel(AuxBinding.RadialMenu(null), "Pen button 2");
        Assert.Equal(8, vm.KeyboardSlots.Count);
        Assert.Equal(7, vm.KeyboardSlot);                    // the default menu is full: the last slice is offered
        Assert.Equal("", vm.KeyboardStatus);
        vm.KeyboardSlot = 6;                                  // Zoom out
        vm.PutKeyboardCommand.Execute(null);
        var m = RadialMenuJson.Parse(vm.RadialMenuText);
        Assert.Equal(RadialActions.Keyboard, m.Slices[6].Action);
        Assert.Equal("Keyboard", m.Slices[6].Label);
        Assert.Equal("The keyboard is on slice 7.", vm.KeyboardStatus);
        vm.KeyboardSlot = 0;                                  // moving it: never two keyboard slices
        vm.PutKeyboardCommand.Execute(null);
        m = RadialMenuJson.Parse(vm.RadialMenuText);
        Assert.Equal(1, m.Slices.Count(s => s.Action == RadialActions.Keyboard));
        Assert.Equal(RadialActions.Keyboard, m.Slices[0].Action);
        Assert.False(m.Slices[6].IsSet);
    }
}
