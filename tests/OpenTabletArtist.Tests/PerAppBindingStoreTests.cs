using OpenTabletArtist.Services;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using Xunit;

namespace OpenTabletArtist.Tests;

public class PerAppBindingStoreTests
{
    private static string Tablet => "TestTablet-PerApp-" + Guid.NewGuid().ToString("N")[..8];

    [Fact]
    public void Add_ClonesAllBindings_AndListRoundTrips()
    {
        var tablet = Tablet;
        var all = new BindingSettings();
        all.TipButton = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Binding.MouseBinding));

        var added = PerAppBindingStore.Add(tablet, "com.example.Paint", "Paint", "/Applications/Paint.app", all);
        Assert.Equal("com.example.Paint", added.BundleId);
        Assert.Equal("Paint", added.DisplayName);
        Assert.NotSame(all, added.Bindings);
        Assert.NotNull(added.Bindings.TipButton);

        var list = PerAppBindingStore.List(tablet);
        Assert.Single(list);
        Assert.Equal("com.example.Paint", list[0].BundleId);

        var again = PerAppBindingStore.Add(tablet, "com.example.Paint", "Paint", "", all);
        Assert.Equal(added.BundleId, again.BundleId);
        Assert.Single(PerAppBindingStore.List(tablet));
    }

    [Fact]
    public void Resolve_ReturnsAppBindings_OrNullForUnknown()
    {
        var tablet = Tablet;
        var all = new BindingSettings();
        PerAppBindingStore.Add(tablet, "com.adobe.Photoshop", "Photoshop", "", all);
        Assert.NotNull(PerAppBindingStore.Resolve(tablet, "com.adobe.Photoshop"));
        Assert.Null(PerAppBindingStore.Resolve(tablet, "com.apple.Safari"));
        Assert.Null(PerAppBindingStore.Resolve(tablet, null));
    }

    [Fact]
    public void Remove_DropsProfile()
    {
        var tablet = Tablet;
        PerAppBindingStore.Add(tablet, "com.example.X", "X", "", new BindingSettings());
        Assert.True(PerAppBindingStore.Remove(tablet, "com.example.X"));
        Assert.Empty(PerAppBindingStore.List(tablet));
        Assert.False(PerAppBindingStore.Remove(tablet, "com.example.X"));
    }

    [Fact]
    public void SaveBindings_UpdatesStoredTable()
    {
        var tablet = Tablet;
        PerAppBindingStore.Add(tablet, "com.example.Y", "Y", "", new BindingSettings());
        var edited = new BindingSettings();
        edited.TipButton = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Binding.MouseBinding));
        PerAppBindingStore.SaveBindings(tablet, "com.example.Y", edited);
        var found = PerAppBindingStore.Find(tablet, "com.example.Y");
        Assert.NotNull(found);
        Assert.NotNull(found!.Bindings.TipButton);
    }
}
