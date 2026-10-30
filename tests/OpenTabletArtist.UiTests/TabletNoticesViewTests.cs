using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>Update notices sit in the main window until the user clicks Dismissed (never a popup window).</summary>
public class TabletNoticesViewTests
{
    [AvaloniaFact]
    public void Notice_ShowsInTheWindow_UntilDismissedIsClicked()
    {
        var store = new Dictionary<string, string>();
        var vm = new TabletNoticesViewModel(k => store.TryGetValue(k, out var v) ? v : null, (k, v) => store[k] = v);
        vm.Refresh(["Wacom Cintiq 22HD (DTK-2200)"]);
        var view = new TabletNoticesView { DataContext = vm };
        var window = new Window { Content = view, Width = 900, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Check your ExpressKeys", texts);
        Assert.Contains(texts, t => t != null && t.Contains("Wacom Cintiq 22HD (DTK-2200)"));
        var button = Assert.Single(view.GetVisualDescendants().OfType<Button>());
        Assert.Equal("Dismissed", button.Content);
        Assert.NotNull(button.Command);

        button.Command!.Execute(button.CommandParameter);
        Dispatcher.UIThread.RunJobs();

        Assert.False(vm.HasNotices);
        Assert.Empty(view.GetVisualDescendants().OfType<Button>());
        window.Close();
    }
}
