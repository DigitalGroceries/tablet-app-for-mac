using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletArtist.Controls;
using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;

namespace OpenTabletArtist.UiTests;

/// <summary>TASKS #59 (Zak 18:21): the Menu transparency value can be typed, not only dragged. Type into the real box of the
/// real radial menu editor: Enter or leaving the box applies it, the slider and the live preview follow.</summary>
public class RadialTransparencyBoxTests
{
    private static (Window Window, TextBox Box, Slider Slider, RadialMenuPie Pie, RadialMenuEditorViewModel Vm) Open()
    {
        _ = new ThemeViewModel();
        var vm = new RadialMenuEditorViewModel(null, "Pen button 1");
        var window = new RadialMenuEditorDialogHost(vm).Window;
        return (window, window.GetVisualDescendants().OfType<TextBox>().First(t => t.Name == "TransparencyBox"),
            window.GetVisualDescendants().OfType<Slider>().First(s => s.Maximum == 100 && s.Minimum == 0),
            window.GetVisualDescendants().OfType<RadialMenuPie>().First(), vm);
    }

    private sealed class RadialMenuEditorDialogHost
    {
        public Window Window { get; }
        public RadialMenuEditorDialogHost(RadialMenuEditorViewModel vm)
        {
            Window = new RadialMenuEditorDialog(vm);
            Window.Show();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void TheBoxStartsAtTheDefault40_AndTheSliderAgrees()
    {
        var (w, box, slider, pie, vm) = Open();
        Assert.Equal("40", box.Text);
        Assert.Equal(40, slider.Value);
        Assert.Equal(40, pie.Transparency);
        w.Close();
    }

    [AvaloniaFact]
    public void Typing25_ThenEnter_MovesTheSlider_AndThePreview()
    {
        var (w, box, slider, pie, vm) = Open();
        box.Focus(); box.SelectAll();
        w.KeyTextInput("2");
        Assert.Equal(40, vm.Transparency);          // nothing applied after the first digit
        w.KeyTextInput("5");
        w.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(25, vm.Transparency);
        Assert.Equal(25, slider.Value);
        Assert.Equal(25, pie.Transparency);
        Assert.Equal("25", box.Text);
        w.Close();
    }

    [AvaloniaFact]
    public void TypingTooBig_IsClamped_WhenFocusLeaves()
    {
        var (w, box, slider, pie, vm) = Open();
        box.Focus(); box.SelectAll();
        w.KeyTextInput("350");
        w.GetVisualDescendants().OfType<TextBox>().First(t => t != box).Focus();   // leave the box
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(100, vm.Transparency);
        Assert.Equal(100, slider.Value);
        Assert.Equal("100", box.Text);
        w.Close();
    }

    [AvaloniaFact]
    public void MovingTheSlider_UpdatesTheBox_AndUpDownKeysStepByOne()
    {
        var (w, box, slider, pie, vm) = Open();
        slider.Value = 61;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("61", box.Text);
        box.Focus();
        w.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
        Assert.Equal(62, vm.Transparency);
        w.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        w.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        Assert.Equal(60, vm.Transparency);
        Assert.Equal("60", box.Text);
        w.Close();
    }

    [AvaloniaFact]
    public void TextThatIsNotANumber_PutsTheOldValueBack()
    {
        var (w, box, slider, pie, vm) = Open();
        box.Focus(); box.SelectAll();
        w.KeyTextInput("abc");
        w.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(40, vm.Transparency);
        Assert.Equal("40", box.Text);
        w.Close();
    }

    [AvaloniaFact]
    public void TextThatIsNotANumber_ThenLeavingTheBox_PutsTheOldValueBack()
    {
        var (w, box, slider, pie, vm) = Open();
        box.Focus(); box.SelectAll();
        w.KeyTextInput("xyz");
        w.GetVisualDescendants().OfType<TextBox>().First(t => t != box).Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(40, vm.Transparency);
        Assert.Equal("40", box.Text);
        w.Close();
    }

    [AvaloniaFact]
    public void SizeBox_StartsAt50_AndIsTheSamePropertyThePenPageUses()
    {
        var (w, _, _, pie, vm) = Open();
        var box = w.GetVisualDescendants().OfType<TextBox>().First(t => t.Name == "SizeBox");
        var slider = w.GetVisualDescendants().OfType<Slider>().First(s => s.Maximum == RadialMenuDef.MaxSize && s.Minimum == RadialMenuDef.MinSize);
        Assert.Equal("50", box.Text);
        Assert.Equal(50, slider.Value);
        Assert.Equal(vm.SizeInput, box.Text);          // Pen page binds the same SizeInput
        vm.Size = 20;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("20", box.Text);
        Assert.Equal(20, slider.Value);
        w.Close();
    }

    [AvaloniaFact]
    public void SizeBox_Typing12_ThenEnter_MovesTheSlider_AndClampsTooBig()
    {
        var (w, _, _, _, vm) = Open();
        var box = w.GetVisualDescendants().OfType<TextBox>().First(t => t.Name == "SizeBox");
        var slider = w.GetVisualDescendants().OfType<Slider>().First(s => s.Maximum == RadialMenuDef.MaxSize && s.Minimum == RadialMenuDef.MinSize);
        box.Focus(); box.SelectAll();
        w.KeyTextInput("12");
        w.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(12, vm.Size);
        Assert.Equal(12, slider.Value);
        Assert.Equal("12", box.Text);
        Assert.Equal("12", vm.SizeInput);

        box.Focus(); box.SelectAll();
        w.KeyTextInput("87");
        w.GetVisualDescendants().OfType<TextBox>().First(t => t != box && t.Name != "SizeBox").Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(RadialMenuDef.MaxSize, vm.Size);
        Assert.Equal("50", box.Text);
        w.Close();
    }
}
