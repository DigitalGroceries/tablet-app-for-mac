using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using OpenTabletArtist.Helpers;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>Items 9 and 10 (#17): in-window dialogs take keyboard focus, give it back, and ⌘S doesn't save behind them.</summary>
public class SheetFocusTests
{
    [AvaloniaFact]
    public void Sheet_TakesFocus_AndGivesItBack()
    {
        var mw = new MainWindow { Width = 1100, Height = 760 };
        mw.Show();
        InWindowSheet.MainOverride = () => mw;
        try
        {
            var page = new TextBox();
            var host = mw.FindControl<Panel>(InWindowSheet.HostName)!;
            var holder = new Panel(); holder.Children.Add(page);
            ((Panel)host.Parent!).Children.Insert(0, holder);
            Dispatcher.UIThread.RunJobs();
            page.Focus(); Dispatcher.UIThread.RunJobs();
            Assert.Same(page, mw.FocusManager!.GetFocusedElement());

            var box = new TextBox();
            var dialog = new Window { Title = "T", Content = new StackPanel { Children = { new TextBlock { Text = "x" }, box } } };
            var task = InWindowSheet.ShowDialogAsync(dialog, mw);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(box, mw.FocusManager!.GetFocusedElement());   // not the page behind

            dialog.Close(); Dispatcher.UIThread.RunJobs();
            Assert.True(task.IsCompleted);
            Assert.Same(page, mw.FocusManager!.GetFocusedElement());   // given back
        }
        finally { InWindowSheet.MainOverride = null; mw.Close(); }
    }

    [AvaloniaFact]
    public void SheetWithNothingFocusable_FocusesTheSheet()
    {
        var mw = new MainWindow { Width = 1100, Height = 760 };
        mw.Show();
        InWindowSheet.MainOverride = () => mw;
        try
        {
            var dialog = new Window { Title = "T", Content = new TextBlock { Text = "only text" } };
            _ = InWindowSheet.ShowDialogAsync(dialog, mw);
            Dispatcher.UIThread.RunJobs();
            var focused = mw.FocusManager!.GetFocusedElement() as Control;
            Assert.NotNull(focused);
            Assert.True(focused!.Name == "Sheet" || Avalonia.VisualTree.VisualExtensions.GetVisualAncestors(focused).OfType<Border>().Any(b => b.Name == "Sheet"));
            dialog.Close(); Dispatcher.UIThread.RunJobs();
        }
        finally { InWindowSheet.MainOverride = null; mw.Close(); }
    }

    [Fact]
    public void CmdS_IsThePageSaveGesture()
    {
        Assert.True(InWindowSheet.IsPageSaveGesture(Key.S, KeyModifiers.Meta));
        Assert.False(InWindowSheet.IsPageSaveGesture(Key.S, KeyModifiers.None));
        Assert.False(InWindowSheet.IsPageSaveGesture(Key.C, KeyModifiers.Meta));
    }
}
