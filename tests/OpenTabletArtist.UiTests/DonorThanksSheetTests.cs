using System.Linq;
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

public class ThankAfterUpdateSheetTests
{
    [AvaloniaFact]
    public void ThankYou_OpensInTheWindow_WithTheDonorLine()
    {
        var mw = new MainWindow { Width = 900, Height = 700 };
        mw.Show();
        InWindowSheet.MainOverride = () => mw;
        try
        {
            var w = UpdatePromptWindow.ShowThanks("21 BETA 2");
            Dispatcher.UIThread.RunJobs();
            Assert.True(InWindowSheet.IsOpen);
            var texts = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Select(
                System.Linq.Enumerable.OfType<Avalonia.Controls.TextBlock>(Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(mw)), t => t.Text));
            Assert.Contains("We are grateful to all who donated and keep this project alive", texts);
            w.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.False(InWindowSheet.IsOpen);
        }
        finally { InWindowSheet.MainOverride = null; mw.Close(); }
    }
}

/// <summary>Zak (2 Nov mock-ups): the download sheet has no title (only the close button), the thanks large,
/// bold and centred on top, then the bar, then "Downloading the update…" small and right-aligned.</summary>
public class DownloadSheetLayoutTests
{
    [AvaloniaFact]
    public void DownloadSheet_HasNoTitle_ThanksFirst_StatusUnderTheBar()
    {
        var mw = new MainWindow { Width = 900, Height = 700 };
        mw.Show();
        InWindowSheet.MainOverride = () => mw;
        try
        {
            UpdatePromptWindow.SyncDownloadNote(true);
            Dispatcher.UIThread.RunJobs();
            var sheet = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(mw)
                .OfType<Avalonia.Controls.Border>().First(b => b.Name == "Sheet");
            var texts = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(sheet)
                .OfType<Avalonia.Controls.TextBlock>().Where(t => !string.IsNullOrEmpty(t.Text) && t.Text != "×" && t.IsEffectivelyVisible).ToList();
            Assert.True(texts.Count == 2, string.Join(" | ", texts.Select(t => $"[{t.Text}]")));   // no title line
            Assert.Equal("We are grateful to all who donated and keep this project alive", texts[0].Text);
            Assert.True(texts[0].FontSize >= 20);
            Assert.Equal(Avalonia.Media.TextAlignment.Center, texts[0].TextAlignment);
            Assert.Equal("Downloading the update…", texts[1].Text);
            Assert.Equal(Avalonia.Layout.HorizontalAlignment.Right, texts[1].HorizontalAlignment);
            Assert.True(texts[1].FontSize < texts[0].FontSize);
        }
        finally { UpdatePromptWindow.SyncDownloadNote(false); Dispatcher.UIThread.RunJobs(); InWindowSheet.MainOverride = null; mw.Close(); }
    }
}
