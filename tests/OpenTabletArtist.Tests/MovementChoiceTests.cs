using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OtdInterop;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>TASKS #55: the Pen page's Movement choice is always visible (Zak / Claude, 12 Nov 18:54), a saved Relative
/// stays Relative (no fallback to Absolute), and Relative is called "Relative (slow / no mouse-like speed-up)". Position smoothing is 0-10.</summary>
[Collection(DisplayEnumeratorCollection.Name)]
public class MovementChoiceTests
{
    private const string WinInkRelative = "VoiDPlugins.OutputMode.WinInkRelativeMode";
    private const string NativeAbsolute = "OpenTabletDriver.Desktop.Output.AbsoluteMode";
    private const string NativeRelative = "OpenTabletDriver.Desktop.Output.RelativeMode";

    private static Settings SettingsOn(string path)
    {
        var s = new Settings { Profiles = new ProfileCollection { new Profile { Tablet = "T", OutputMode = new PluginSettingStore(path) { Path = path } } } };
        return s;
    }

    [Theory]
    [InlineData(NativeAbsolute)]
    [InlineData(NativeRelative)]
    public void TheChoiceIsOffered_ForEveryProfile_AndHasTwoEntries(string path)
    {
        var settings = SettingsOn(path);
        var vm = new TabletDetailViewModel(settings.Profiles.First(), settings);
        Assert.Equal(2, vm.MovementOptions.Length);
        Assert.Equal(path == NativeAbsolute ? "absolute" : "relative", vm.SelectedMovement.Key);
    }

    [Fact]
    public void SavedRelative_StaysRelative_AfterAnUpdate_NothingIsApplied()
    {
        var settings = SettingsOn(NativeRelative);
        int applies = 0;
        var vm = new TabletDetailViewModel(settings.Profiles.First(), settings,
            applyAction: _ => { applies++; return Task.FromResult(SettingsApplyOutcome.Live); });
        Assert.Equal(0, applies);                                   // nothing is switched behind the user's back
        Assert.Equal(NativeRelative, settings.Profiles.First().OutputMode!.Path);
        Assert.False(vm.IsAbsoluteMode);
        Assert.Equal("relative", vm.SelectedMovement.Key);
    }

    [Fact]
    public void Prepare_KeepsEverySavedRelativeProfile()
    {
        var settings = SettingsOn(WinInkRelative);
        settings.Profiles.Add(new Profile { Tablet = "U", OutputMode = new PluginSettingStore(NativeRelative) { Path = NativeRelative } });
        settings.Profiles.Add(new Profile { Tablet = "V", OutputMode = new PluginSettingStore(NativeAbsolute) { Path = NativeAbsolute } });
        OtaSettingsPolicy.Prepare(settings, ownedDaemon: false);    // every settings send and update passes here
        Assert.Equal(WinInkRelative, settings.Profiles[0].OutputMode!.Path);
        Assert.Equal(NativeRelative, settings.Profiles[1].OutputMode!.Path);
        Assert.Equal(NativeAbsolute, settings.Profiles[2].OutputMode!.Path);
    }

    [Fact]
    public void Relative_CanBePicked_AndAbsoluteBack()
    {
        var settings = SettingsOn(NativeAbsolute);
        int applies = 0;
        var vm = new TabletDetailViewModel(settings.Profiles.First(), settings,
            applyAction: _ => { applies++; return Task.FromResult(SettingsApplyOutcome.Live); });
        vm.SelectMovementCommand.Execute("relative");
        Assert.Equal(1, applies);
        Assert.False(vm.IsAbsoluteMode);
        vm.SelectMovementCommand.Execute("absolute");
        Assert.Equal(2, applies);
        Assert.True(vm.IsAbsoluteMode);
    }

    [Fact]
    public void TheMovementOptions_AreTheHonestLabels()
    {
        var settings = SettingsOn(NativeAbsolute);
        var vm = new TabletDetailViewModel(settings.Profiles.First(), settings);
        Assert.Equal("Relative (slow / no mouse-like speed-up)", vm.MovementOptions[1].Title);
        Assert.Equal("The pointer moves from where it is, by how far the pen moves. No Mac-style speed-up.", vm.MovementOptions[1].Description);
        Assert.DoesNotContain("Mouse-like", vm.MovementOptions[1].Title);
    }

    [Fact]
    public void TheHideSwitchIsGone()
    {
        // No Settings switch, no setting key, no strings: the choice is never hidden.
        Assert.Null(typeof(PenMovementPolicy).GetProperty("ShowChoice"));
        Assert.DoesNotContain(typeof(StartupViewModel).GetProperties(), p => p.Name.Contains("MovementChoice"));
    }

    // ---- Position smoothing 0-10
    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.25, 10.0)]
    [InlineData(0.125, 5.0)]
    [InlineData(0.05, 2.0)]
    [InlineData(0.5, 10.0)]     // above the ceiling: clamped
    public void SmoothingToUi_ScalesToZeroToTen(double internalValue, double ui) =>
        Assert.Equal(ui, PenMovementPolicy.SmoothingToUi(internalValue, 0.25), 6);

    [Theory]
    [InlineData(10.0, 0.25)]
    [InlineData(5.0, 0.125)]
    [InlineData(0.0, 0.0)]
    [InlineData(12.0, 0.25)]
    public void SmoothingFromUi_ScalesBack(double ui, double internalValue) =>
        Assert.Equal(internalValue, PenMovementPolicy.SmoothingFromUi(ui, 0.25), 9);

    [Fact]
    public void PositionSmoothingScale_MapsBothWays_AndShowsTheNumber()
    {
        var settings = SettingsOn(NativeAbsolute);
        var vm = new TabletDetailViewModel(settings.Profiles.First(), settings);
        vm.PositionSmoothing = 0.25;                                // an old saved maximum
        Assert.Equal(10, vm.PositionSmoothingScale, 6);
        Assert.Equal("10.0", vm.PositionSmoothingText);
        vm.PositionSmoothingScale = 4;
        Assert.Equal(0.1, vm.PositionSmoothing, 9);
        Assert.Equal("4.0", vm.PositionSmoothingText);
        vm.PositionSmoothingScale = 4.04;                           // an echo that rounds to the same: kept as it is
        Assert.Equal(0.1, vm.PositionSmoothing, 9);
        vm.PositionSmoothingScale = 0;
        Assert.Equal(0, vm.PositionSmoothing, 9);
    }
}
