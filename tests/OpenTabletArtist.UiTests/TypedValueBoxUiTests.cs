using System.Linq;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenTabletArtist.Controls;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>V29 #140/#132: typing a value into a slider's box moves the slider.</summary>
public class TypedValueBoxUiTests
{
    [AvaloniaFact]
    public void LabeledSlider_TypedValue_SetsTheValue_SnapsToTicks_AndAPercentReadoutReadsPercent()
    {
        var s = new LabeledSlider { Label = "Speed", Minimum = 1, Maximum = 100, TickFrequency = 1, IsSnapToTickEnabled = true, ValueFormat = "0", Value = 50 };
        var w = new Window { Content = s };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        var box = s.GetVisualDescendants().OfType<TextBox>().Single(b => b.Name == "PART_Value");
        Assert.Equal("50", box.Text);
        box.Text = "73.6"; s.CommitTyped();
        Assert.Equal(74, s.Value);
        Assert.Equal("74", box.Text);
        box.Text = "abc"; s.CommitTyped();
        Assert.Equal(74, s.Value);
        Assert.Equal("74", box.Text);

        var pct = new LabeledSlider { Label = "Opacity", Minimum = 0, Maximum = 1, ValueFormat = "0%", Value = 0.2 };
        w.Content = pct;
        Dispatcher.UIThread.RunJobs();
        var pbox = pct.GetVisualDescendants().OfType<TextBox>().Single(b => b.Name == "PART_Value");
        pbox.Text = "45%"; pct.CommitTyped();
        Assert.Equal(0.45, pct.Value, 6);
        w.Close();
    }

    [AvaloniaFact]
    public void ThemeLightness_TheBoxNumbersTheLevels1To9()
    {
        var vm = new ThemeViewModel();
        var before = vm.Lightness;
        try
        {
            vm.LightnessInput = "1";
            Assert.Equal(0, vm.Lightness);
            vm.LightnessInput = "9";
            Assert.Equal(LightnessService.Steps - 1, vm.Lightness);
            Assert.Equal("9", vm.LightnessInput);
            vm.LightnessInput = "40";               // clamped to the lightest
            Assert.Equal("9", vm.LightnessInput);
        }
        finally { vm.Lightness = before; }
    }
}
