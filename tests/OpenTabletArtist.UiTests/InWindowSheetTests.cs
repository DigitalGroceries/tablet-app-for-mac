using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletArtist.Helpers;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>Version 19 (Zak: no popup windows): dialogs open inside the main window, keep their result and
/// close the same way.</summary>
public class InWindowSheetTests
{
    [AvaloniaFact]
    public void Dialog_OpensInsideTheMainWindow_AndClosesWithItsResult()
    {
        var mw = new MainWindow { Width = 1100, Height = 760 };
        mw.Show();
        InWindowSheet.MainOverride = () => mw;
        try
        {
            bool closed = false, opened = false;
            string? result = null;
            var ok = new Button { Content = "OK" };
            var dialog = new Window { Title = "Test", Width = 400, Content = new StackPanel { Children = { new TextBlock { Text = "Hello sheet" }, ok } } };
            dialog.Opened += (_, _) => opened = true;
            dialog.Closed += (_, _) => closed = true;
            ok.Click += (_, _) => { result = "ok"; dialog.Close(); };

            var task = InWindowSheet.ShowDialogAsync(dialog, mw);
            Dispatcher.UIThread.RunJobs();
            var host = mw.FindControl<Panel>(InWindowSheet.HostName)!;
            Assert.True(host.IsVisible);
            Assert.Contains(mw.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Hello sheet");
            Assert.False(dialog.IsVisible);   // no separate window
            Assert.True(opened);

            ok.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(task.IsCompleted);
            Assert.Equal("ok", result);
            Assert.True(closed);
            Assert.False(host.IsVisible);
        }
        finally { InWindowSheet.MainOverride = null; mw.Close(); }
    }

    [AvaloniaFact]
    public void CancelledClose_KeepsTheSheetOpen()
    {
        var mw = new MainWindow { Width = 1100, Height = 760 };
        mw.Show();
        InWindowSheet.MainOverride = () => mw;
        try
        {
            bool allow = false;
            var dialog = new Window { Title = "Ask", Content = new TextBlock { Text = "x" } };
            dialog.Closing += (_, e) => { if (!allow) e.Cancel = true; };
            var task = InWindowSheet.ShowDialogAsync(dialog, mw);
            Dispatcher.UIThread.RunJobs();
            dialog.Close(); Dispatcher.UIThread.RunJobs();
            Assert.False(task.IsCompleted);
            allow = true; dialog.Close(); Dispatcher.UIThread.RunJobs();
            Assert.True(task.IsCompleted);
        }
        finally { InWindowSheet.MainOverride = null; mw.Close(); }
    }

    [AvaloniaFact]
    public void KeysInTheSheet_ReachTheDialogsHandlers()
    {
        var mw = new MainWindow { Width = 1100, Height = 760 };
        mw.Show();
        InWindowSheet.MainOverride = () => mw;
        try
        {
            Key? seen = null;
            var box = new TextBox();
            var dialog = new Window { Content = box };
            dialog.AddHandler(InputElement.KeyDownEvent, (_, e) => seen = e.Key, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            _ = InWindowSheet.ShowDialogAsync(dialog, mw);
            Dispatcher.UIThread.RunJobs();
            box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Assert.Equal(Key.Escape, seen);
            dialog.Close(); Dispatcher.UIThread.RunJobs();
        }
        finally { InWindowSheet.MainOverride = null; mw.Close(); }
    }
}
