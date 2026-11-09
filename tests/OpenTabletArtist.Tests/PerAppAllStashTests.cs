using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>#74: the All binding table must never be taken from a live app overlay.</summary>
public class PerAppAllStashTests
{
    private static string Tablet => "TestTablet-AllStash-" + Guid.NewGuid().ToString("N")[..8];

    [Theory]
    [InlineData(null, null, "com.apple.finder", null, true)]   // All -> app: stash All first
    [InlineData(null, null, null, "pen-1", true)]               // All -> extra pen
    [InlineData("com.apple.finder", null, "com.apple.Safari", null, false)] // app -> app: workspace holds an overlay
    [InlineData("com.apple.finder", null, null, null, false)]   // app -> All
    [InlineData(null, "pen-1", "com.apple.Safari", null, false)]
    [InlineData(null, null, null, null, false)]                 // All -> All
    public void StashesAll_OnlyWhenLeavingAll(string? appliedApp, string? appliedPen, string? app, string? pen, bool expected) =>
        Assert.Equal(expected, PerAppBindingRuntime.ShouldStashAllBeforeApply(appliedApp, appliedPen, app, pen));

    [Fact]
    public void EditorProfileIsAll_WithoutRuntime() =>
        Assert.True(PerAppBindingRuntime.EditorProfileIsAll(null));

    [Fact]
    public void StashedAllBindings_RoundTripsDoubleClick()
    {
        var tablet = Tablet;
        Assert.Null(PerAppBindingRuntime.StashedAllBindings(tablet));
        var all = new BindingSettings();
        all.PenButtons.Add(AuxKeyBinding.NewStore(AuxKeyBinding.DoubleClickBindingPath));
        PerAppBindingRuntime.SyncAllBindings(tablet, all);
        var back = PerAppBindingRuntime.StashedAllBindings(tablet);
        Assert.NotNull(back);
        Assert.Equal(AuxKeyBinding.DoubleClickBindingPath, back!.PenButtons[0].Path);
    }

    [Fact]
    public void MouseLabels_FallBackToEnglish()
    {
        Assert.Equal("Left click", AuxKeyBinding.Describe(AuxBinding.Unbound with { Kind = AuxKind.Mouse, MouseButton = "Left" }));
        Assert.Contains(AuxKeyBinding.MouseButtonOptions, o => o.Value == AuxKeyBinding.DoubleClickValue);
    }

    [Fact]
    public void DoubleClickFix_IsPerApp_WithAllAsFallback_Tasks15()
    {
        Assert.Equal(80, PerAppBindingRuntime.EffectiveDoubleClickFix(80, 50));
        Assert.Equal(50, PerAppBindingRuntime.EffectiveDoubleClickFix(null, 50));
        Assert.Null(PerAppBindingRuntime.EffectiveDoubleClickFix(null, null));
    }

    [Fact]
    public void AnAllEdit_IsKeptInTheStash_Tasks15()
    {
        var tablet = Tablet;
        var all = new BindingSettings { DoubleClickFix = 50 };
        PerAppBindingRuntime.SyncAllBindings(tablet, all);
        all.DoubleClickFix = 80;
        PerAppBindingRuntime.SyncAllBindings(tablet, all);
        Assert.Equal(80, PerAppBindingRuntime.StashedAllBindings(tablet)!.DoubleClickFix);
    }

    [Fact]
    public void AnExtraPenWithoutItsOwnDoubleClickFix_KeepsTheAppsValue_Tasks15()
    {
        var target = new BindingSettings { DoubleClickFix = 50 };
        PenBindings.Overlay(target, new BindingSettings { DoubleClickFix = null });
        Assert.Equal(50, target.DoubleClickFix);
        PenBindings.Overlay(target, new BindingSettings { DoubleClickFix = 70 });
        Assert.Equal(70, target.DoubleClickFix);
    }
}

/// <summary>TASKS #15: per-app / per-pen Double Click Fix storage (Zak, 9 Nov, test build 3).</summary>
public class DoubleClickFixPerAppStoreTests
{
    private static string Tablet => "TestTablet-DcfStore-" + Guid.NewGuid().ToString("N")[..8];

    [Fact]
    public void ANewApp_FollowsAll_UntilGivenItsOwnValue()
    {
        var tablet = Tablet;
        var app = PerAppBindingStore.Add(tablet, "com.example.app", "App", "", new BindingSettings { DoubleClickFix = 50 });
        Assert.Null(app.Bindings.DoubleClickFix);
        Assert.Null(PerAppBindingStore.Find(tablet, "com.example.app")!.Bindings.DoubleClickFix);
    }

    [Fact]
    public void AnAppsOwnValue_RoundTripsThroughTheStore_AndBeatsAll()
    {
        var tablet = Tablet;
        PerAppBindingStore.Add(tablet, "com.example.app", "App", "", new BindingSettings { DoubleClickFix = 50 });
        var copy = PerAppBindingStore.CloneBindings(PerAppBindingStore.Find(tablet, "com.example.app")!.Bindings);
        copy.DoubleClickFix = 80;
        PerAppBindingStore.SaveBindings(tablet, "com.example.app", copy);
        PerAppBindingRuntime.SyncAllBindings(tablet, new BindingSettings { DoubleClickFix = 50 });

        var app = PerAppBindingStore.Find(tablet, "com.example.app")!.Bindings;
        var all = PerAppBindingRuntime.StashedAllBindings(tablet)!;
        Assert.Equal(80, TabletDetailViewModel.DoubleClickFixDisplay(app, all));
        Assert.Equal(50, TabletDetailViewModel.DoubleClickFixDisplay(all, all));
        Assert.Equal(80, PerAppBindingRuntime.EffectiveDoubleClickFix(app.DoubleClickFix, all.DoubleClickFix));
    }

    [Fact]
    public void CopiedValues_AreClearedOnce_LaterOwnValuesStay()
    {
        var tablet = Tablet;
        PerAppBindingStore.Add(tablet, "com.example.app", "App", "", new BindingSettings());
        var copy = PerAppBindingStore.CloneBindings(PerAppBindingStore.Find(tablet, "com.example.app")!.Bindings);
        copy.DoubleClickFix = 50;   // what Version 25 and earlier copied from All
        PerAppBindingStore.SaveBindings(tablet, "com.example.app", copy);

        Assert.True(PerAppBindingStore.ClearCopiedDoubleClickFixOnce(tablet));
        Assert.Null(PerAppBindingStore.Find(tablet, "com.example.app")!.Bindings.DoubleClickFix);

        copy.DoubleClickFix = 70;   // the user's own value, after the migration
        PerAppBindingStore.SaveBindings(tablet, "com.example.app", copy);
        Assert.False(PerAppBindingStore.ClearCopiedDoubleClickFixOnce(tablet));
        Assert.Equal(70, PerAppBindingStore.Find(tablet, "com.example.app")!.Bindings.DoubleClickFix);
    }

    [Fact]
    public void ANewExtraPen_KeepsTheAppsOrAllsValue()
    {
        var tablet = Tablet;
        var main = new BindingSettings { DoubleClickFix = 50 };
        PerPenStore.Seen(tablet, new PenToolSignal(1, 0x802, false), main, out _);
        PerPenStore.Seen(tablet, new PenToolSignal(2, 0x802, false), main, out var added);
        Assert.NotNull(added);
        Assert.Null(added!.Bindings.DoubleClickFix);
    }
}
