using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
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

/// <summary>Build R16 (the user's mock-up): dark inset Movement/Pen input frame, pen-button cards on the Eraser tab
/// (the same shared rows), flipped pen drawing with upright badges, redrawn cone + nib.</summary>
public class BuildR16Tests
{
    private static (TabletDetailViewModel, TabletDetailView, Window) Open(string tab)
    {
        _ = new ThemeViewModel();
        var profile = new Profile { Tablet = "Wacom PTK-440", OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)) };
        var settings = new OpenTabletDriver.Desktop.Settings { Profiles = new ProfileCollection { profile } };
        var vm = new TabletDetailViewModel(profile, settings);
        vm.PenButton1Row = new PenSwitchRowViewModel(PenSwitchKind.PenButton, 1, null, false, (_, _, _) => Task.CompletedTask);
        vm.PenButton2Row = new PenSwitchRowViewModel(PenSwitchKind.PenButton, 2, null, false, (_, _, _) => Task.CompletedTask);
        var view = new TabletDetailView { DataContext = vm };
        var w = new Window { Content = view, Width = 1100, Height = 1100 }; w.Show();
        view.FindControl<RadioButton>(tab)!.IsChecked = true;
        Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        return (vm, view, w);
    }

    [AvaloniaFact]
    public void BasicsFrame_IsDarkerInset_WithSubtleBorder()
    {
        var (_, view, w) = Open("PenTab");
        var b = view.FindControl<Border>("PenBasicsBox")!;
        var bg = ((ISolidColorBrush)b.Background!).Color; var br = ((ISolidColorBrush)b.BorderBrush!).Color;
        Assert.Equal(Color.Parse("#262626"), bg);
        Assert.Equal(Color.Parse("#4A4A4A"), br);
        Assert.Equal(1, b.BorderThickness.Top);
        w.Close();
    }

    [AvaloniaFact]
    public void EraserTab_HasTheSameButtonCards_InFlippedOrder()
    {
        var (vm, view, w) = Open("EraserTab");
        var cards = view.FindControl<StackPanel>("EraserCards")!.Children.OfType<PenSwitchCard>().Where(c => c.IsVisible).ToList();
        Assert.Same(vm.PenButton1Row, cards[0].DataContext);
        Assert.Same(vm.PenButton2Row, cards[1].DataContext);
        Assert.Same(vm.PenEraserRow, cards[^1].DataContext);
        var tops = cards.Select(c => c.TranslatePoint(new Point(0, 0), w)!.Value.Y).ToList();
        Assert.True(tops[0] < tops[1] && tops[1] < tops[^1]);
        w.Close();
    }

    [AvaloniaFact]
    public void FlippedDiagram_RotatesShapes_BadgesUpright_OnTheirButtons()
    {
        var (_, view, w) = Open("EraserTab");
        var d = view.FindControl<PenDiagram>("EraserPenDiagram")!;
        Assert.True(d.Flipped);
        var shapes = d.FindControl<Canvas>("Shapes")!;
        Assert.Equal(180, ((RotateTransform)shapes.RenderTransform!).Angle);
        var b1 = d.FindControl<Border>("Badge1")!; var b2 = d.FindControl<Border>("Badge2")!;
        Assert.Null(b1.RenderTransform);
        Assert.Equal(360 - 204 - 26, Canvas.GetTop(b1)); Assert.Equal(360 - 172 - 26, Canvas.GetTop(b2));
        Assert.True(Canvas.GetTop(b1) < Canvas.GetTop(b2));   // tip now up: button 1 (nearest the tip) is the upper one
        var upright = new PenDiagram(); Assert.True(PenDiagram.BadgeTop(1, false) > PenDiagram.BadgeTop(2, false));
        w.Close();
    }

    [AvaloniaFact]
    public void Tip_IsSeparateCone_WithFlatEnd_AndSolidNib()
    {
        var d = new PenDiagram();
        var cone = d.FindControl<Avalonia.Controls.Shapes.Path>("Cone")!.Data!.Bounds;
        var nib = d.FindControl<Avalonia.Controls.Shapes.Path>("Nib")!;
        Assert.True(cone.Width >= 28 && cone.Height / 28 is > 1.8 and < 2.3);
        Assert.Null(nib.Stroke);
        Assert.True(nib.Data!.Bounds.Width <= 8 && nib.Data.Bounds.Top >= cone.Bottom);
    }

}
