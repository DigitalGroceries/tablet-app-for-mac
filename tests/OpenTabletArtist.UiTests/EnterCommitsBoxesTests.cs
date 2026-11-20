using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenTabletArtist.Helpers;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>V29 #168 (Zak 20 Nov ~09:50: "when I add a value and press enter the slider does not go to the type value until I click
/// save"): Enter in a number box applies the typed value at once, as the manual says.</summary>
public partial class EnterCommitsBoxesTests
{
    private sealed partial class Vm : ObservableObject
    {
        [ObservableProperty] private double _value = 50;
    }

    [AvaloniaFact]
    public void Enter_AppliesTheTypedValue_ToWhatTheBoxIsBoundTo()
    {
        EnterCommitsBoxes.Install();
        var vm = new Vm();
        var box = new TextBox { DataContext = vm };
        box[!TextBox.TextProperty] = new Binding(nameof(Vm.Value)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus };
        var slider = new Slider { DataContext = vm, Minimum = 0, Maximum = 100 };
        slider[!Slider.ValueProperty] = new Binding(nameof(Vm.Value)) { Mode = BindingMode.TwoWay };
        var window = new Window { Content = new StackPanel { Children = { box, slider } } };
        window.Show();
        try
        {
            box.Focus();
            box.Text = "80";
            Assert.Equal(50, vm.Value);                       // not applied while typing
            box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Assert.Equal(80, vm.Value);                       // Enter applies it
            Assert.Equal(80, slider.Value);                   // and the slider follows at once
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Enter_LeavesMultiLineBoxesAlone()
    {
        EnterCommitsBoxes.Install();
        var vm = new Vm();
        var box = new TextBox { DataContext = vm, AcceptsReturn = true };
        box[!TextBox.TextProperty] = new Binding(nameof(Vm.Value)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus };
        var window = new Window { Content = box };
        window.Show();
        try
        {
            box.Text = "70";
            box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Assert.Equal(50, vm.Value);
        }
        finally { window.Close(); }
    }
}
