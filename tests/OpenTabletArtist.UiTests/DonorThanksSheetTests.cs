using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenTabletArtist.Helpers;
using OpenTabletArtist.Views;
using Xunit;

namespace OpenTabletArtist.UiTests;

public class DonorThanksSheetTests
{
    [AvaloniaFact]
    public void Note_OpensInTheWindow_WhileDownloading_AndClosesAfter()
    {
        var mw = new MainWindow { Width = 900, Height = 700 };
        mw.Show();
        InWindowSheet.MainOverride = () => mw;
        try
        {
            UpdatePromptWindow.SyncDownloadNote(true);
            UpdatePromptWindow.SyncDownloadNote(true);   // no second copy
            Dispatcher.UIThread.RunJobs();
            Assert.True(UpdatePromptWindow.DownloadNoteOpen);
            Assert.True(InWindowSheet.IsOpen);
            UpdatePromptWindow.SyncDownloadNote(false);
            Dispatcher.UIThread.RunJobs();
            Assert.False(UpdatePromptWindow.DownloadNoteOpen);
            Assert.False(InWindowSheet.IsOpen);
        }
        finally { InWindowSheet.MainOverride = null; mw.Close(); }
    }
}
