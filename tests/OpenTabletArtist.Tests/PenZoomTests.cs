using System.Numerics;
using OpenTabletArtist.Domain;
using OpenTabletDriver.Desktop.Binding;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Pen zoom / pan+zoom (TASKS #24): binding store, filter sync, scroll-tab round-trip.</summary>
public class PenZoomTests
{
    [Fact]
    public void MakeZoom_DefaultsAndReads()
    {
        var store = PenZoomBinding.MakeZoom();
        Assert.Equal(PenZoomBinding.BindingTypeName, store.Path);
        Assert.Equal(PenZoomBinding.ModeZoom, PenZoomBinding.ReadMode(store));
        Assert.Equal(PenScrollMath.DefaultSpeed, PenZoomBinding.ReadSpeed(store));
        Assert.False(PenZoomBinding.ReadInvert(store));
        Assert.True(PenZoomBinding.IsZoom(store));
        Assert.False(PenZoomBinding.IsPanZoom(store));
    }

    [Fact]
    public void MakePanZoom_ModeAndSpeed()
    {
        var store = PenZoomBinding.MakePanZoom(speed: 75, invert: true);
        Assert.Equal(PenZoomBinding.ModePanZoom, PenZoomBinding.ReadMode(store));
        Assert.Equal(75, PenZoomBinding.ReadSpeed(store));
        Assert.True(PenZoomBinding.ReadInvert(store));
        Assert.True(PenZoomBinding.IsPanZoom(store));
    }

    [Fact]
    public void ScrollTab_RoundTripsZoomAndPanZoom()
    {
        foreach (var (value, mode) in new[]
                 {
                     (PenZoomBinding.ZoomValue, PenZoomBinding.ModeZoom),
                     (PenZoomBinding.PanZoomValue, PenZoomBinding.ModePanZoom),
                 })
        {
            var binding = new AuxBinding(AuxKind.Scroll, AuxCombo.Unbound, AuxKeyBinding.None, value, 40);
            var store = AuxKeyBinding.MakeBinding(binding);
            Assert.NotNull(store);
            Assert.Equal(PenZoomBinding.BindingTypeName, store!.Path);
            Assert.Equal(mode, PenZoomBinding.ReadMode(store));
            Assert.Equal(40, PenZoomBinding.ReadSpeed(store));

            var back = AuxKeyBinding.ReadBinding(store)!;
            Assert.Equal(AuxKind.Scroll, back.Kind);
            Assert.Equal(value, back.Scroll);
            Assert.Equal(40, back.GestureSpeed);
            Assert.Contains(value == PenZoomBinding.ZoomValue ? "zoom" : "pan",
                AuxKeyBinding.Describe(back), System.StringComparison.OrdinalIgnoreCase);
        }
    }

    private static Profile ProfileWithPenButtons(int count = 2)
    {
        var p = new Profile
        {
            Tablet = "Wacom PTH-651",
            OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)),
        };
        for (int i = 0; i < count; i++) p.BindingSettings.PenButtons.Add(null);
        return p;
    }

    [Fact]
    public void SyncFilter_AddsAndRemovesWithBinding()
    {
        var p = ProfileWithPenButtons();
        p.BindingSettings.PenButtons[0] = PenZoomBinding.MakeZoom();
        Assert.True(PenZoomBinding.SyncFilter(p));
        Assert.True(Assert.Single(p.Filters, f => f?.Path == PenZoomBinding.FilterTypeName)!.Enable);
        Assert.False(PenZoomBinding.SyncFilter(p));

        p.BindingSettings.PenButtons[0] = null;
        Assert.True(PenZoomBinding.SyncFilter(p));
        Assert.DoesNotContain(p.Filters, f => f?.Path == PenZoomBinding.FilterTypeName);
    }

    [Fact]
    public void ZoomUsesVerticalOnly_PanZoomUsesBothAxes_InMath()
    {
        // Same Step maths pen scroll uses; zoom filter locks Vertical, pan+zoom uses Both.
        var rest = Vector2.Zero;
        var (vOnly, hOnly) = PenScrollMath.Step(new Vector2(1, 1), 50, PenScrollAxes.Vertical, false, ref rest);
        Assert.Equal(0, hOnly);
        Assert.NotEqual(0, vOnly);

        rest = Vector2.Zero;
        var (v, h) = PenScrollMath.Step(new Vector2(1, 1), 50, PenScrollAxes.Both, false, ref rest);
        Assert.NotEqual(0, v);
        Assert.NotEqual(0, h);
    }

    [Fact]
    public void IsPenGestureScroll_CoversScrollZoomPanZoom()
    {
        Assert.True(AuxKeyBinding.IsPenGestureScroll(PenScrollBinding.ScrollValue));
        Assert.True(AuxKeyBinding.IsPenGestureScroll(PenZoomBinding.ZoomValue));
        Assert.True(AuxKeyBinding.IsPenGestureScroll(PenZoomBinding.PanZoomValue));
        Assert.False(AuxKeyBinding.IsPenGestureScroll("Up"));
        Assert.False(AuxKeyBinding.IsPenGestureScroll(null));
    }
}
