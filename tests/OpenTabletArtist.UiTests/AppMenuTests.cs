using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>Version 20: our macOS app menu replaces Avalonia's default (no "About Avalonia" popup).</summary>
public class AppMenuTests
{
    [AvaloniaFact]
    public void AppMenu_HasOurAbout_AndSettingsItems_NoAvalonia()
    {
        var menu = AppMenu.Build();
        var headers = menu.Items.OfType<NativeMenuItem>().Where(i => i is not NativeMenuItemSeparator).Select(i => i.Header).ToList();
        Assert.Equal(new[] { "About Tablet App for Mac", "Export settings…", "Import settings…" }, headers);
        Assert.IsType<NativeMenuItemSeparator>(menu.Items[1]);
        Assert.DoesNotContain(headers, h => h!.Contains("Avalonia"));
    }
}
