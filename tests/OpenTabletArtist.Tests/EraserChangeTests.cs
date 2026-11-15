using System;
using System.Threading.Tasks;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OtdInterop;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// Version 28 (TASKS #78, Daniel: "No option to use the eraser at the back of the pen, which was an option in the Wacom
/// software"): the eraser card gets Change… and Default like the pen buttons, so the eraser end can do any action.
/// </summary>
[Collection(DisplayEnumeratorCollection.Name)]
public class EraserChangeTests
{
    private static (TabletDetailViewModel Vm, Profile Profile, Func<string?> Title) Make(AuxBinding choice)
    {
        var profile = new Profile { Tablet = "Wacom CTH-461", OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)) };
        var settings = new OpenTabletDriver.Desktop.Settings { Profiles = new ProfileCollection { profile } };
        string? title = null;
        var vm = new TabletDetailViewModel(profile, settings,
            applyAction: _ => Task.FromResult(SettingsApplyOutcome.Live),
            editBinding: (_, t) => { title = t; return Task.FromResult<AuxBinding?>(choice); });
        return (vm, profile, () => title);
    }

    [Fact]
    public async Task Change_OnTheEraser_SetsTheEraserEnd()
    {
        var keyE = new AuxBinding(AuxKind.Keyboard, new AuxCombo(false, false, false, "E"), AuxKeyBinding.None, AuxKeyBinding.None);
        var (vm, profile, title) = Make(keyE);
        var eraser = vm.PenEraserRow!;
        await eraser.ChooseActionCommand.ExecuteAsync(null);
        Assert.Equal("Eraser", title());
        Assert.Equal(keyE, AuxKeyBinding.ReadBinding(profile.BindingSettings.EraserButton));
        Assert.Equal(OperatingSystem.IsMacOS(), vm.PenEraserRow!.CanChooseAction);   // the card offers it on macOS
        Assert.False(vm.PenTipRow!.CanChooseAction);                                // the tip keeps its own controls
    }

    [Fact]
    public void Default_Eraser_ReadsAsErase()
    {
        var row = new PenSwitchRowViewModel(PenSwitchKind.Eraser, 1, PenSwitchBinding.MakeAdaptiveBinding(PenSwitchKind.Eraser, 1),
            true, (_, _, _) => Task.CompletedTask, _ => Task.CompletedTask);
        Assert.Equal(PenSwitchBindingMode.Auto, row.Mode);
        Assert.Equal("Default (erase)", row.CurrentPlainLabel);
        Assert.False(row.ShowUseAdaptive);   // already Default: no Default button
    }
}
