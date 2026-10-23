using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>Version 3: a held pen button lights its badge on the pen picture, and the Pen page says which.</summary>
public class BuildV3UiTests
{
    [AvaloniaFact]
    public void HeldPenButton_LightsItsBadge_AndTheLineSaysWhich()
    {
        _ = new ThemeViewModel();
        var profile = new Profile { Tablet = "Wacom PTK-440", OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)) };
        var settings = new OpenTabletDriver.Desktop.Settings { Profiles = new ProfileCollection { profile } };
        var vm = new TabletDetailViewModel(profile, settings);
        vm.PenButton1Row = new PenSwitchRowViewModel(PenSwitchKind.PenButton, 1, null, false, (_, _, _) => Task.CompletedTask);
        vm.PenButton2Row = new PenSwitchRowViewModel(PenSwitchKind.PenButton, 2, null, false, (_, _, _) => Task.CompletedTask);
        var view = new TabletDetailView { DataContext = vm };
        var w = new Window { Content = view, Width = 1100, Height = 1100 }; w.Show();
        view.FindControl<RadioButton>("PenTab")!.IsChecked = true;
        Dispatcher.UIThread.RunJobs(); w.UpdateLayout();
        var diagram = view.GetVisualDescendants().OfType<PenDiagram>().First(d => d.IsEffectivelyVisible);
        var b1 = diagram.FindControl<Border>("Badge1")!; var b2 = diagram.FindControl<Border>("Badge2")!;
        Assert.Equal("Press a pen button to test it.", vm.PenButtonTestText);
        Assert.False(b1.Classes.Contains("pressed"));

        vm.OnPenButtons(new[] { false, true });
        Dispatcher.UIThread.RunJobs();
        Assert.True(b2.Classes.Contains("pressed"));
        Assert.False(b1.Classes.Contains("pressed"));
        Assert.Equal("Button 2 pressed.", vm.PenButtonTestText);
        var c = ((ISolidColorBrush)b2.Background!).Color;
        Assert.True(c.R <= 0xE4 && c.G <= 0xE4 && c.B <= 0xE4);

        vm.OnPenButtons(new[] { false, false });
        Dispatcher.UIThread.RunJobs();
        Assert.False(b2.Classes.Contains("pressed"));
    }
}
