using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenTabletArtist.Helpers;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>Item 28: with desktop.MainWindow still unset (quiet login start), sheets such as setup and the manual
/// still open inside the app's window, not as their own windows.</summary>
public class InWindowAfterQuietStartTests
{
    [AvaloniaFact]
    public void Sheet_UsesTheAppWindow_WhenDesktopMainWindowIsUnset()
    {
        var mw = new MainWindow { Width = 900, Height = 700 };
        var before = AppMenu.Window;
        InWindowSheet.MainOverride = null;
        AppMenu.Window = mw;
        try
        {
            var dialog = new Window { Title = "Setup", Content = new TextBlock { Text = "setup body" } };
            _ = InWindowSheet.ShowDialogAsync(dialog);
            Dispatcher.UIThread.RunJobs();
            Assert.True(InWindowSheet.IsOpen);
            Assert.False(dialog.IsVisible);        // not a window of its own
            Assert.True(mw.IsVisible);             // the hidden app window was brought forward
            dialog.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.False(InWindowSheet.IsOpen);
        }
        finally { AppMenu.Window = before; mw.Close(); }
    }
}
