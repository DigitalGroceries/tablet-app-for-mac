using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>Build O: Wacom-style Tool row decides the tab group; Tip Feel mirrors the curve's Softness.</summary>
public class BuildOTests
{
    [Fact]
    public void TipFeel_IsMirroredSoftness()
    {
        var vm = new TabletDetailViewModel();
        vm.TipFeel = 0.6;
        Assert.Equal(-0.6, vm.Softness, 3);
        vm.Softness = 0.3;
        Assert.Equal(-0.3, vm.TipFeel, 3);
        vm.ResetPenFeelToDefault();
        Assert.Equal(0, vm.TipFeel, 3);
    }

    [AvaloniaFact]
    public void ToolTiles_PickTheirTabs_AndFollowTheOpenTab()
    {
        var view = new TabletDetailView { DataContext = new TabletDetailViewModel() };
        var w = new Window { Content = view, Width = 1100, Height = 800 };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        var pen = view.FindControl<RadioButton>("ToolPenButton")!;
        var funcs = view.FindControl<RadioButton>("ToolFunctionsButton")!;
        Assert.True(pen.IsChecked);                                          // opens on Pen, like Wacom
        Assert.True(view.FindControl<RadioButton>("PenTab")!.IsChecked);
        view.FindControl<RadioButton>("ButtonsTab")!.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(funcs.IsChecked);
        Assert.True(view.FindControl<RadioButton>("ButtonsTab")!.IsVisible);
        Assert.False(view.FindControl<RadioButton>("PenTab")!.IsVisible);
        view.FindControl<RadioButton>("MappingTab")!.IsChecked = true;      // deep link → Pen tool
        Dispatcher.UIThread.RunJobs();
        Assert.True(pen.IsChecked);
    }
}
