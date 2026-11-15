using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenTabletArtist.Views;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>
/// V28 #80 (Daniel's report; agreed by Claude and Devin, built by Claude while Devin was out of usage): every edit
/// already works on the driver, and only Save keeps it after a driver restart. While there are unsaved changes the
/// footer says so in one quiet line under "Unsaved changes"; otherwise the line is hidden.
/// </summary>
public class UnsavedHintTests
{
    private sealed class State
    {
        public string FooterStatusText { get; init; } = "";
        public bool ShowUnsavedHint { get; init; }
        public string UnsavedHint => "Your changes work now. Click Save to keep them after a restart.";
    }

    private static TextBlock Hint(State state)
    {
        var footer = new SettingsFooterView();
        var window = new Window { Content = footer, DataContext = state, Width = 1100 };
        window.Show();
        window.Measure(new Size(1100, 600));
        window.Arrange(new Rect(0, 0, 1100, 600));
        Dispatcher.UIThread.RunJobs();
        return footer.FindControl<TextBlock>("UnsavedHint")!;
    }

    [AvaloniaFact]
    public void ShownWhileThereAreUnsavedChanges()
    {
        var hint = Hint(new State { FooterStatusText = "Unsaved changes", ShowUnsavedHint = true });
        Assert.True(hint.IsVisible);
        Assert.Equal("Your changes work now. Click Save to keep them after a restart.", hint.Text);
    }

    [AvaloniaFact]
    public void HiddenOtherwise_AndWhenTheContextHasNoSuchLine()
    {
        Assert.False(Hint(new State { ShowUnsavedHint = false }).IsVisible);
        var footer = new SettingsFooterView();
        var window = new Window { Content = footer, DataContext = new object(), Width = 1100 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.False(footer.FindControl<TextBlock>("UnsavedHint")!.IsVisible);   // FallbackValue: never an empty line
    }
}
