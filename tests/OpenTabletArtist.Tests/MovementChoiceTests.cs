using System;
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

/// <summary>Turns the Settings switch "Show Movement choice" on for a test and puts it back after. Shared
/// state, so the tests that use it sit in the display collection (they do not run in parallel).</summary>
internal sealed class MovementChoiceScope : IDisposable
{
    private readonly bool _was = PenMovementPolicy.ShowChoice;
    private MovementChoiceScope(bool on) => PenMovementPolicy.ShowChoice = on;
    public static MovementChoiceScope On() => new(true);
    public static MovementChoiceScope Off() => new(false);
    public void Dispose() => PenMovementPolicy.ShowChoice = _was;
}

/// <summary>TASKS #55: the Pen page's Movement choice is hidden by default (the app uses Absolute), a saved
/// Relative falls back to Absolute, and a Settings switch brings the choice back. Position smoothing is 0-10.</summary>
[Collection(DisplayEnumeratorCollection.Name)]
public class MovementChoiceTests
{
    private const string WinInkAbsolute = "VoiDPlugins.OutputMode.WinInkAbsoluteMode";
    private const string WinInkRelative = "VoiDPlugins.OutputMode.WinInkRelativeMode";
    private const string NativeAbsolute = "OpenTabletDriver.Desktop.Output.AbsoluteMode";
    private const string NativeRelative = "OpenTabletDriver.Desktop.Output.RelativeMode";

    private static Settings SettingsOn(string path)
    {
        var s = new Settings { Profiles = new ProfileCollection { new Profile { Tablet = "T", OutputMode = new PluginSettingStore(path) { Path = path } } } };
        return s;
    }

    [Fact]
    public void TheChoiceIsOffByDefault()
    {
        using var off = MovementChoiceScope.Off();
        Assert.False(PenMovementPolicy.ShowChoice);
        var settings = SettingsOn(NativeAbsolute);
        var vm = new TabletDetailViewModel(settings.Profiles.First(), settings);
        Assert.False(vm.ShowMovementChoice);
    }

    [Fact]
    public void TheSwitch_IsRememberedAndRaisesChanged()
    {
        using var off = MovementChoiceScope.Off();
        int changed = 0;
        void H() => changed++;
        PenMovementPolicy.Changed += H;
        try
        {
            PenMovementPolicy.ShowChoice = true;
            Assert.True(PenMovementPolicy.ShowChoice);
            Assert.Equal("1", AppSettings.Get(PenMovementPolicy.SettingKey));
            PenMovementPolicy.ShowChoice = true;      // same value: nothing
            Assert.Equal(1, changed);
            PenMovementPolicy.ShowChoice = false;
            Assert.Equal(2, changed);
        }
        finally { PenMovementPolicy.Changed -= H; }
    }

    [Fact]
    public void StartupViewModel_Switch_ShowsAndHidesTheChoice()
    {
        using var off = MovementChoiceScope.Off();
        var vm = new StartupViewModel();
        Assert.False(vm.ShowMovementChoiceOn);
        vm.ShowMovementChoiceOn = true;
        Assert.True(PenMovementPolicy.ShowChoice);
        vm.ShowMovementChoiceOn = false;
        Assert.False(PenMovementPolicy.ShowChoice);
    }

    [Theory]
    [InlineData(WinInkRelative, WinInkAbsolute)]
    [InlineData(NativeRelative, NativeAbsolute)]
    [InlineData(WinInkAbsolute, WinInkAbsolute)]
    public void AbsolutePathFor_SwapsTheRelativeToken(string from, string to) =>
        Assert.Equal(to, PenMovementPolicy.AbsolutePathFor(from));

    [Fact]
    public void SavedRelative_FallsBackToAbsolute_WhileHidden_AndIsApplied()
    {
        using var off = MovementChoiceScope.Off();
        var settings = SettingsOn(NativeRelative);
        int applies = 0;
        var vm = new TabletDetailViewModel(settings.Profiles.First(), settings,
            applyAction: _ => { applies++; return Task.FromResult(SettingsApplyOutcome.Live); });
        Assert.Equal(1, applies);                                   // once, no loop
        Assert.Equal(NativeAbsolute, settings.Profiles.First().OutputMode!.Path);
        Assert.True(vm.IsAbsoluteMode);
    }

    [Fact]
    public void SavedRelative_IsKept_WhileTheChoiceIsShown()
    {
        using var on = MovementChoiceScope.On();
        var settings = SettingsOn(NativeRelative);
        int applies = 0;
        var vm = new TabletDetailViewModel(settings.Profiles.First(), settings,
            applyAction: _ => { applies++; return Task.FromResult(SettingsApplyOutcome.Live); });
        Assert.Equal(0, applies);
        Assert.False(vm.IsAbsoluteMode);
        Assert.True(vm.ShowMovementChoice);
    }

    [Fact]
    public void FailingApply_DoesNotLoopTheFallback()
    {
        using var off = MovementChoiceScope.Off();
        var settings = SettingsOn(NativeRelative);
        int applies = 0;
        _ = new TabletDetailViewModel(settings.Profiles.First(), settings,
            applyAction: _ => { applies++; return Task.FromResult(SettingsApplyOutcome.Disconnected); });
        Assert.True(applies <= 1);
    }

    [Fact]
    public void WhileHidden_RelativeCannotBePicked()
    {
        using var off = MovementChoiceScope.Off();
        var settings = SettingsOn(NativeAbsolute);
        int applies = 0;
        var vm = new TabletDetailViewModel(settings.Profiles.First(), settings,
            applyAction: _ => { applies++; return Task.FromResult(SettingsApplyOutcome.Live); });
        vm.SelectMovementCommand.Execute("relative");
        Assert.Equal(0, applies);
        Assert.True(vm.IsAbsoluteMode);
    }

    [Fact]
    public void TurningTheSwitchOff_WhileOnRelative_FallsBackToAbsolute()
    {
        var settings = SettingsOn(NativeRelative);
        int applies = 0;
        TabletDetailViewModel vm;
        using (MovementChoiceScope.On())
        {
            vm = new TabletDetailViewModel(settings.Profiles.First(), settings,
                applyAction: _ => { applies++; return Task.FromResult(SettingsApplyOutcome.Live); });
            Assert.Equal(0, applies);
            PenMovementPolicy.ShowChoice = false;                   // the user switches it off
            Assert.Equal(1, applies);
            Assert.True(vm.IsAbsoluteMode);
        }
        vm.Dispose();
    }

    [Fact]
    public void Prepare_ChangesEverySavedRelativeProfile_WhileHidden()
    {
        using var off = MovementChoiceScope.Off();
        var settings = SettingsOn(WinInkRelative);
        settings.Profiles.Add(new Profile { Tablet = "U", OutputMode = new PluginSettingStore(NativeAbsolute) { Path = NativeAbsolute } });
        OtaSettingsPolicy.Prepare(settings, ownedDaemon: false);
        Assert.Equal(WinInkAbsolute, settings.Profiles[0].OutputMode!.Path);
        Assert.Equal(NativeAbsolute, settings.Profiles[1].OutputMode!.Path);
        using var on = MovementChoiceScope.On();
        var kept = SettingsOn(NativeRelative);
        OtaSettingsPolicy.Prepare(kept, ownedDaemon: false);
        Assert.Equal(NativeRelative, kept.Profiles[0].OutputMode!.Path);
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
        using var off = MovementChoiceScope.Off();
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
