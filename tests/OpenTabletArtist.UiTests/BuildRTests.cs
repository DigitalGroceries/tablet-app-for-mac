using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>Build R: the Device/Tool panel shows on the TABLET page (ShowHeader = false), so Functions,
/// expresskeys and touch ring can be reached.</summary>
public class BuildRTests
{
    private static WheelEditor Wheel()
    {
        ButtonBinding Row(string l) => new(0, AuxBinding.Unbound, isOtherBinding: false, otherLabel: "", canEdit: false, applyBinding: null, label: l);
        return new WheelEditor(wheelIndex: 0, title: "Wheel", showTitle: false, clockwise: Row("Clockwise"),
            counterClockwise: Row("Counter-clockwise"), buttons: new List<ButtonBinding>(),
            clockwiseThreshold: 30, counterClockwiseThreshold: 30, stepSizeDegrees: 5, applyThreshold: null);
    }

    [AvaloniaTheory]
    [InlineData(true)] [InlineData(false)]
    public void TabletPage_ShowsFunctionsTile_AndReachesExpressKeysAndTouchRing(bool hasWheel)
    {
        var vm = new TabletDetailViewModel { ShowHeader = false };
        if (hasWheel) vm.Wheels = new List<WheelEditor> { Wheel() };
        var view = new TabletDetailView { DataContext = vm };
        var w = new Window { Content = view, Width = 1100, Height = 761 };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.True(view.FindControl<Border>("WacomHeaderPane")!.IsEffectivelyVisible);
        var funcs = view.FindControl<RadioButton>("ToolFunctionsButton")!;
        Assert.True(funcs.IsEffectivelyVisible);
        funcs.IsChecked = true;
        funcs.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(view.FindControl<RadioButton>("ButtonsTab")!.IsEffectivelyVisible);
        Assert.Equal(hasWheel, view.FindControl<RadioButton>("WheelsTab")!.IsEffectivelyVisible);
        Assert.False(view.FindControl<RadioButton>("PenTab")!.IsVisible);
        w.Close();
    }

    [AvaloniaFact]
    public void TabletPage_OnlyOneToolTileIsSelected_AndFunctionsShowsOnlyItsTabs()
    {
        var vm = new TabletDetailViewModel { ShowHeader = false };
        vm.Wheels = new List<WheelEditor> { Wheel() };
        var view = new TabletDetailView { DataContext = vm };
        var w = new Window { Content = view, Width = 1100, Height = 761 };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        var funcs = view.FindControl<RadioButton>("ToolFunctionsButton")!;
        var pen = view.FindControl<RadioButton>("ToolPenButton")!;
        var more = view.FindControl<RadioButton>("ToolMoreButton")!;
        // a real click, as the user does it
        var pt = funcs.TranslatePoint(new Avalonia.Point(funcs.Bounds.Width / 2, funcs.Bounds.Height / 2), w)!.Value;
        w.MouseDown(pt, Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.None);
        w.MouseUp(pt, Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.True(funcs.IsChecked);
        Assert.False(pen.IsChecked);
        Assert.False(more.IsChecked);
        foreach (var t in new[] { "ButtonsTab", "WheelsTab" }) Assert.True(view.FindControl<RadioButton>(t)!.IsVisible, t);
        foreach (var t in new[] { "PenTab", "EraserTab", "MappingTab" }) Assert.False(view.FindControl<RadioButton>(t)!.IsVisible, t);
        pt = pen.TranslatePoint(new Avalonia.Point(pen.Bounds.Width / 2, pen.Bounds.Height / 2), w)!.Value;
        w.MouseDown(pt, Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.None);
        w.MouseUp(pt, Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.True(pen.IsChecked);
        Assert.False(funcs.IsChecked);
        Assert.False(view.FindControl<RadioButton>("ButtonsTab")!.IsVisible);
        w.Close();
    }
}
