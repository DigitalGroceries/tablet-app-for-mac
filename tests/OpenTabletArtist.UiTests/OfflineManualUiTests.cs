using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using OpenTabletArtist.Views;
using Xunit;

namespace OpenTabletArtist.UiTests;

public class OfflineManualUiTests
{
    [AvaloniaFact]
    public void Manual_RendersHeadingsAndImages_FromTheApp()
    {
        var w = new ManualWindow();
        var all = ((Control)w.Content!).GetLogicalDescendants().ToList();
        Assert.True(all.OfType<Image>().Count() > 5);
        Assert.Contains(all.OfType<TextBlock>(), t => t.Text == "Updating");
        Assert.Contains(all.OfType<Button>(), b => (b.Content as string) == "Open online version");
    }
}
