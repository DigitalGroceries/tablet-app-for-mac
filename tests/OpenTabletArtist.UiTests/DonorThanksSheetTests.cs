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

public class DonorThanksWiredTests
{
    /// <summary>The real show path: an AppUpdateState download, wired as MainViewModel wires it, puts the note in
    /// the window straight away and takes it down when the download ends.</summary>
    [AvaloniaFact]
    public async System.Threading.Tasks.Task Download_ShowsTheNote_ThroughTheWiring()
    {
        var mw = new MainWindow { Width = 900, Height = 700 };
        mw.Show();
        InWindowSheet.MainOverride = () => mw;
        try
        {
            var s = new OpenTabletArtist.Services.AppUpdateState { MinNoteTime = System.TimeSpan.Zero };
            UpdatePromptWindow.WireDownloadNote(s);
            var gate = new System.Threading.Tasks.TaskCompletionSource<string?>();
            s.Download = _ => gate.Task;
            var t = s.DownloadWithNoteAsync(new OpenTabletArtist.Services.Updater.Release(99, "z", "s"));
            Dispatcher.UIThread.RunJobs();
            Assert.True(UpdatePromptWindow.DownloadNoteOpen);
            Assert.True(InWindowSheet.IsOpen);
            gate.SetResult(null);
            await t;
            Dispatcher.UIThread.RunJobs();
            Assert.False(UpdatePromptWindow.DownloadNoteOpen);
        }
        finally { InWindowSheet.MainOverride = null; mw.Close(); }
    }
}
