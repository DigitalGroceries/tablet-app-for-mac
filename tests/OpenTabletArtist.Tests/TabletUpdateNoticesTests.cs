using System.Collections.Generic;
using System.Linq;
using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.Tests;

public class TabletUpdateNoticesTests
{
    private static (TabletNoticesViewModel Vm, Dictionary<string, string> Store) Make()
    {
        var store = new Dictionary<string, string>();
        var vm = new TabletNoticesViewModel(k => store.TryGetValue(k, out var v) ? v : null, (k, v) => store[k] = v);
        return (vm, store);
    }

    [Theory]
    [InlineData("Wacom Cintiq 21UX (DTK-2100)")]
    [InlineData("Wacom Cintiq 22HD (DTK-2200)")]
    [InlineData("Wacom Cintiq 22HD Touch (DTH-2200)")]
    public void CintiqExpressKeysNotice_ShowsForItsTablets_WithTheTabletName(string tablet)
    {
        var (vm, _) = Make();
        vm.Refresh([tablet]);

        var item = Assert.Single(vm.Items);
        Assert.Equal("cintiq-v1-expresskeys", item.Id);
        Assert.True(vm.HasNotices);
        Assert.Contains(tablet, item.Paragraphs[0]);
        Assert.DoesNotContain(item.Paragraphs, p => p.Contains("{tablet}"));
    }

    [Fact]
    public void OtherTablets_GetNoNotice()
    {
        var (vm, _) = Make();
        vm.Refresh(["Wacom PTK-440", "Wacom Cintiq 21UX (DTZ-2100)"]);
        Assert.Empty(vm.Items);
        Assert.False(vm.HasNotices);
    }

    [Fact]
    public void Dismissed_HidesIt_AndItStaysHiddenAfterARestart()
    {
        var (vm, store) = Make();
        vm.Refresh(["Wacom Cintiq 22HD (DTK-2200)"]);
        vm.DismissCommand.Execute(vm.Items.Single());

        Assert.Empty(vm.Items);
        Assert.False(vm.HasNotices);
        Assert.Equal("true", store[TabletUpdateNotices.DismissedKey("cintiq-v1-expresskeys")]);

        var restarted = new TabletNoticesViewModel(k => store.TryGetValue(k, out var v) ? v : null, (k, v) => store[k] = v);
        restarted.Refresh(["Wacom Cintiq 22HD (DTK-2200)"]);
        Assert.Empty(restarted.Items);
    }

    [Fact]
    public void NotDismissed_ItComesBackWhenTheTabletReconnects()
    {
        var (vm, _) = Make();
        vm.Refresh(["Wacom Cintiq 21UX (DTK-2100)"]);
        vm.Refresh([]);
        Assert.Empty(vm.Items);
        vm.Refresh(["Wacom Cintiq 21UX (DTK-2100)"]);
        Assert.Single(vm.Items);
    }

    [Fact]
    public void EveryNotice_SaysWhatToDo_AndUsesSentenceCase()
    {
        foreach (var n in TabletUpdateNotices.All)
        {
            Assert.NotEmpty(n.TabletNames);
            Assert.True(char.IsUpper(n.Title[0]));
            Assert.Contains(n.Paragraphs, p => p.StartsWith("What to do:"));
            Assert.DoesNotContain(n.Paragraphs, p => p.Contains("daemon", System.StringComparison.OrdinalIgnoreCase));
        }
    }
}
