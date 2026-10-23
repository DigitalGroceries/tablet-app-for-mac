using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>Build R2: the Home About sentence has no credit; the credit, modified-version notice and
/// licences stay one click away, and the Source code link stays.</summary>
public class BuildR2AboutTests
{
    [AvaloniaFact]
    public void HomeSentence_HasNoCredit_ButCreditsAndSourceStay()
    {
        var vm = new AboutViewModel();
        var view = new AboutView { DataContext = vm };
        var w = new Window { Content = view, Width = 1000, Height = 700 };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        var runs = view.GetLogicalDescendants().OfType<TextBlock>()
            .Where(t => t.Inlines != null).SelectMany(t => t.Inlines!.OfType<Run>()).Select(r => r.Text ?? "").ToList();
        var sentence = string.Concat(runs);
        Assert.StartsWith("Tablet App for Mac is a free, artist-friendly tablet app. Version ", sentence);
        Assert.DoesNotContain("based on", sentence);
        Assert.Equal("Version 6.", vm.AppVersionSentence);
        var flyoutText = (((Button)view.FindControl<Button>("CreditsButton")!).Flyout as Flyout)!.Content as TextBlock;
        Assert.NotNull(flyoutText);
        foreach (var s in new[] { "modified version based on OpenTabletArtist and OpenTabletDriver", "GPL-3.0", "LGPL-3.0" })
            Assert.Contains(s, flyoutText!.Text);
        Assert.Contains(view.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "Source code");
        w.Close();
    }
}
