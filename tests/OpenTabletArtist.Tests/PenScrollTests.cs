using System.Linq;
using System.Numerics;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Pen scroll: hold a pen button and move the pen to scroll (maths, binding store, filter sync).</summary>
public class PenScrollTests
{
    private static Profile ProfileWithPenButtons(int count = 2)
    {
        var p = new Profile { Tablet = "Wacom PTH-651", OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)) };
        for (int i = 0; i < count; i++) p.BindingSettings.PenButtons.Add(null);
        return p;
    }

    [Fact]
    public void Step_PageFollowsThePen_ByDefault()
    {
        var rest = Vector2.Zero;
        // Pen moves down 1 mm: the page follows it down = scroll up = negative (OTD convention).
        var (v, h) = PenScrollMath.Step(new Vector2(0, 1), 50, PenScrollAxes.Both, invert: false, ref rest);
        Assert.Equal(-20, v);
        Assert.Equal(0, h);
        // Pen moves right: page follows right = scroll left = negative.
        (v, h) = PenScrollMath.Step(new Vector2(1, 0), 50, PenScrollAxes.Both, invert: false, ref rest);
        Assert.Equal(-20, h);
    }

    [Fact]
    public void Step_Invert_FlipsBothAxes()
    {
        var rest = Vector2.Zero;
        var (v, h) = PenScrollMath.Step(new Vector2(1, 1), 50, PenScrollAxes.Both, invert: true, ref rest);
        Assert.Equal(20, v);
        Assert.Equal(20, h);
    }

    [Fact]
    public void Step_CarriesTheRemainder_SoSlowMovementStillScrolls()
    {
        var rest = Vector2.Zero;
        int total = 0;
        for (int i = 0; i < 10; i++)   // 10 × 0.1 mm at Speed 1 (0.4 px/mm) = 0.4 px
            total += PenScrollMath.Step(new Vector2(0, 0.1f), 1, PenScrollAxes.Both, false, ref rest).Vertical;
        for (int i = 0; i < 20; i++)   // 2 mm more = 0.8 px, so 1.2 px in all
            total += PenScrollMath.Step(new Vector2(0, 0.1f), 1, PenScrollAxes.Both, false, ref rest).Vertical;
        Assert.Equal(-1, total);
    }

    [Theory]
    [InlineData(PenScrollAxes.Vertical, -20, 0)]
    [InlineData(PenScrollAxes.Horizontal, 0, -20)]
    public void Step_AxisLock(PenScrollAxes axes, int expectV, int expectH)
    {
        var rest = Vector2.Zero;
        var (v, h) = PenScrollMath.Step(new Vector2(1, 1), 50, axes, false, ref rest);
        Assert.Equal(expectV, v);
        Assert.Equal(expectH, h);
    }

    [Fact]
    public void MmPerUnit_UsesTheTabletsOwnSize_PTH651()
    {
        // Wacom PTH-651 (OTD config): 223.52 × 139.7 mm, 44704 × 27940 units = 0.005 mm per unit.
        var mm = PenScrollMath.MmPerUnit(223.52f, 139.7f, 44704, 27940)!.Value;
        Assert.Equal(0.005f, mm.X, 5);
        Assert.Equal(0.005f, mm.Y, 5);
        Assert.Null(PenScrollMath.MmPerUnit(0, 0, 0, 0));
    }

    [Fact]
    public void ScrollTab_RoundTripsPenScroll()
    {
        var binding = new AuxBinding(AuxKind.Scroll, AuxCombo.Unbound, AuxKeyBinding.None, PenScrollBinding.ScrollValue);
        var store = AuxKeyBinding.MakeBinding(binding);
        Assert.Equal(PenScrollBinding.BindingTypeName, store!.Path);
        Assert.Equal(binding, AuxKeyBinding.ReadBinding(store));
        Assert.Equal("Move pen to scroll", AuxKeyBinding.Describe(binding));
        Assert.Equal(PenScrollMath.DefaultSpeed, PenScrollBinding.ReadSpeed(store));
    }

    [Fact]
    public void Filter_IsAddedWhileUsed_AndRemovedWithoutTouchingOtherFilters()
    {
        var p = ProfileWithPenButtons();
        var other = AuxKeyBinding.NewStore("Some.Other.Filter");
        p.Filters.Add(other);

        p.BindingSettings.PenButtons[1] = PenScrollBinding.Make();
        Assert.True(PenScrollBinding.SyncFilter(p));
        Assert.False(PenScrollBinding.SyncFilter(p));   // idempotent
        Assert.True(Assert.Single(p.Filters, f => f?.Path == PenScrollBinding.FilterTypeName)!.Enable);

        p.BindingSettings.PenButtons[1] = null;
        Assert.True(PenScrollBinding.SyncFilter(p));
        Assert.DoesNotContain(p.Filters, f => f?.Path == PenScrollBinding.FilterTypeName);
        Assert.Contains(other, p.Filters);
    }

    [Fact]
    public void Filter_FollowsExpressKeysToo()
    {
        var p = ProfileWithPenButtons();
        p.BindingSettings.AuxButtons.Add(PenScrollBinding.Make());
        PenScrollBinding.SyncFilter(p);
        Assert.Contains(p.Filters, f => f?.Path == PenScrollBinding.FilterTypeName);
    }

    [Fact]
    public void Policy_AddsTheFilter_AndKeepsItOn()
    {
        var p = ProfileWithPenButtons();
        p.BindingSettings.PenButtons[0] = PenScrollBinding.Make();
        var settings = new Settings { Profiles = new ProfileCollection { p } };
        OtaSettingsPolicy.Prepare(settings, ownedDaemon: true);
        Assert.True(Assert.Single(p.Filters, f => f?.Path == PenScrollBinding.FilterTypeName)!.Enable);
    }
}
