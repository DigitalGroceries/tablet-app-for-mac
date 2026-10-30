using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>The owner's note sits at the top of the Supported tablets list, above the title.</summary>
public class SupportedTabletsProgressNoteTests
{
    [AvaloniaFact]
    public void ProgressNote_IsTheFirstLineAboveTheTitle()
    {
        var dialog = new SupportedTabletsDialog { DataContext = new SupportedTabletsDialogViewModel(null) };
        dialog.Show();
        Dispatcher.UIThread.RunJobs();

        var note = dialog.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "ProgressNote");
        Assert.Equal(SupportedTabletsDialogViewModel.ProgressNote, note.Text);
        Assert.True(note.IsVisible);
        var header = Assert.IsType<StackPanel>(note.Parent);
        Assert.Same(note, header.Children[0]);
        dialog.Close();
    }
}
