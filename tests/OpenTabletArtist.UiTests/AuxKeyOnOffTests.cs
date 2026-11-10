using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OtdInterop;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>TASKS #28: the ExpressKeys page — "All buttons" switch plus each key's own On/Off, through the real editor.
/// "What the driver gets" is the profile's AuxButtons: a null entry is a key that does nothing.</summary>
public class AuxKeyOnOffTests
{
    private const string Photoshop = "com.adobe.Photoshop";

    private static string NewTablet() => "TestTablet-KeyOnOff-" + Guid.NewGuid().ToString("N")[..8];

    private static Settings SettingsFor(string tablet)
    {
        var profile = new Profile
        {
            OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)),
            Tablet = tablet,
        };
        profile.BindingSettings.AuxButtons = new PluginSettingStoreCollection();
        foreach (var k in new[] { "A", "B", "C" }) profile.BindingSettings.AuxButtons.Add(AuxKeyBinding.MakeKeyBinding(k)!);
        return new Settings { Profiles = new ProfileCollection { profile } };
    }

    private static TabletDetailViewModel Editor(Settings s) =>
        new(s.Profiles[0], s, applyAction: _ => Task.FromResult(SettingsApplyOutcome.Live));

    private static async Task Settle()
    {
        for (int i = 0; i < 10; i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(20, TestContext.Current.CancellationToken); }
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task Toggle(TabletDetailViewModel vm, int key)
    {
        var card = vm.AuxButtons[key - 1];
        Assert.True(card.ToggleKeyCommand.CanExecute(null));
        card.ToggleKeyCommand.Execute(null);
        await Settle();
    }

    private static string?[] Driver(Settings s) =>
        s.Profiles[0].BindingSettings.AuxButtons.Select(x => x == null ? null : AuxKeyBinding.ReadKey(x)).ToArray();

    [AvaloniaFact]
    public async Task AllOn_KeyOn_TheKeyWorks()
    {
        var s = SettingsFor(NewTablet()); var vm = Editor(s);
        Assert.True(vm.AuxButtonsEnabled);
        Assert.Equal(new[] { "A", "B", "C" }, Driver(s));
        Assert.All(vm.AuxButtons, b => { Assert.True(b.IsKeyOn); Assert.True(b.ShowKeyToggle); Assert.Equal(1.0, b.CardOpacity); });
    }

    [AvaloniaFact]
    public async Task AllOn_KeyOff_TheKeyDoesNothing_ButKeepsItsShortcut_AndIsGreyed()
    {
        var s = SettingsFor(NewTablet()); var vm = Editor(s);
        await Toggle(vm, 2);
        Assert.Equal(new string?[] { "A", null, "C" }, Driver(s));
        var b = vm.AuxButtons[1];
        Assert.False(b.IsKeyOn);
        Assert.True(b.CardOpacity < 1.0);
        Assert.Equal("B", b.Summary);              // the shortcut is still shown
        Assert.Equal("Turn on", b.KeyToggleHeader);
        Assert.Equal("Turn off", vm.AuxButtons[0].KeyToggleHeader);
        await Toggle(vm, 2);                        // On again: B is back
        Assert.Equal(new[] { "A", "B", "C" }, Driver(s));
        Assert.True(vm.AuxButtons[1].IsKeyOn);
    }

    [AvaloniaFact]
    public async Task AllOff_KeyOn_NothingWorks_AndThePerKeyItemIsGreyedWithItsTip()
    {
        var s = SettingsFor(NewTablet()); var vm = Editor(s);
        vm.AuxButtonsEnabled = false; await Settle();
        Assert.Equal(new string?[] { null, null, null }, Driver(s));
        foreach (var b in vm.AuxButtons)
        {
            Assert.True(b.IsKeyOn);                                  // the key's own state is untouched
            Assert.False(b.AllButtonsOn);
            Assert.False(b.ToggleKeyCommand.CanExecute(null));       // greyed
            Assert.Equal("Turn on All buttons first", b.KeyToggleTip);
        }
        Assert.Equal("A", vm.AuxButtons[0].Summary);                 // the shortcuts are still listed
    }

    [AvaloniaFact]
    public async Task AllOff_KeyOff_NothingWorks()
    {
        var s = SettingsFor(NewTablet()); var vm = Editor(s);
        await Toggle(vm, 3);
        vm.AuxButtonsEnabled = false; await Settle();
        Assert.Equal(new string?[] { null, null, null }, Driver(s));
        Assert.False(vm.AuxButtons[2].IsKeyOn);
        Assert.False(vm.AuxButtons[2].ToggleKeyCommand.CanExecute(null));
        Assert.Equal("C", vm.AuxButtons[2].Summary);
    }

    [AvaloniaFact]
    public async Task SwitchingAllButtons_NeverChangesAKeysOwnOnOff()
    {
        var s = SettingsFor(NewTablet()); var vm = Editor(s);
        await Toggle(vm, 2);                                   // key 2 Off, keys 1 and 3 On
        for (int round = 0; round < 2; round++)
        {
            vm.AuxButtonsEnabled = false; await Settle();
            Assert.Equal(new[] { true, false, true }, vm.AuxButtons.Select(b => b.IsKeyOn).ToArray());
            vm.AuxButtonsEnabled = true; await Settle();
            Assert.Equal(new[] { true, false, true }, vm.AuxButtons.Select(b => b.IsKeyOn).ToArray());
            // back On: only the On keys work again; the Off key stays silent but keeps B
            Assert.Equal(new string?[] { "A", null, "C" }, Driver(s));
            Assert.Equal("B", vm.AuxButtons[1].Summary);
        }
        // and the other way round: turn a key On while All is Off is refused, so state can't drift
        vm.AuxButtonsEnabled = false; await Settle();
        vm.AuxButtons[1].ToggleKeyCommand.Execute(null); await Settle();
        Assert.False(vm.AuxButtons[1].IsKeyOn);
    }

    [AvaloniaFact]
    public async Task PerKeyState_IsKeptForEachApplication_LikeTheBinding()
    {
        var tablet = NewTablet(); var s = SettingsFor(tablet);
        PerAppBindingStore.Add(tablet, Photoshop, "Photoshop", "", s.Profiles[0].BindingSettings);
        var vm = Editor(s);

        vm.SelectedAppBundleId = Photoshop;
        await Toggle(vm, 1);                                    // key 1 Off for Photoshop only
        Assert.False(vm.AuxButtons[0].IsKeyOn);
        Assert.Equal(new string?[] { "A", "B", "C" }, Driver(s));   // All's table is untouched
        var app = PerAppBindingStore.Find(tablet, Photoshop)!.Bindings.AuxButtons;
        Assert.Null(app[0]);                                    // the app's table has nothing for it …
        Assert.Equal("A", vm.AuxButtons[0].Summary);            // … and keeps the shortcut

        vm.SelectedAppBundleId = null;                          // All: key 1 is still On
        Assert.True(vm.AuxButtons[0].IsKeyOn);
        vm.SelectedAppBundleId = Photoshop;
        Assert.False(vm.AuxButtons[0].IsKeyOn);

        // editing an Off key keeps the new shortcut aside
        var again = Editor(s);                                  // a fresh editor reads the saved state
        again.SelectedAppBundleId = Photoshop;
        Assert.False(again.AuxButtons[0].IsKeyOn);
        Assert.Equal("A", again.AuxButtons[0].Summary);
        again.SelectedAppBundleId = null;
        Assert.True(again.AuxButtons[0].IsKeyOn);
    }

    [AvaloniaFact]
    public async Task ANewApplication_StartsWithAllsOffKeys()
    {
        var tablet = NewTablet(); var s = SettingsFor(tablet); var vm = Editor(s);
        await Toggle(vm, 2);
        await vm.AddApplicationAsync(new MacAppCandidate(Photoshop, "Photoshop", ""));
        await Settle();
        Assert.Equal(Photoshop, vm.SelectedAppBundleId);
        Assert.Equal(new[] { true, false, true }, vm.AuxButtons.Select(b => b.IsKeyOn).ToArray());
        Assert.Equal("B", vm.AuxButtons[1].Summary);
    }

    [AvaloniaFact]
    public async Task Default_ClearsTheActions_AndAnOffKeyStaysOff()
    {
        var s = SettingsFor(NewTablet()); var vm = Editor(s);
        await Toggle(vm, 2);
        await vm.ResetExpressKeysToDefaultsAsync(); await Settle();
        Assert.Equal(new string?[] { null, null, null }, Driver(s));
        Assert.False(vm.AuxButtons[1].IsKeyOn);
        Assert.Equal("Do nothing", vm.AuxButtons[1].Summary);
    }

    [AvaloniaFact]
    public async Task WheelRows_HaveNoOnOffItem()
    {
        var row = new ButtonBinding(1, AuxBinding.Unbound, false, "", true, null);
        Assert.False(row.ShowKeyToggle);
        Assert.False(row.ToggleKeyCommand.CanExecute(null));
        await Task.CompletedTask;
    }
}
