using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OtdInterop;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>TASKS #212 part 6: a connected tablet whose keys nobody has mapped yet gets the question
/// "Set up your tablet's buttons?" once. "Later" is remembered; the button on the ExpressKeys page stays.</summary>
public class KeyMappingOfferTests
{
    private static string Tablet() => "TestTablet-KeyOffer-" + Guid.NewGuid().ToString("N")[..8];

    private static TabletDetailViewModel VmFor(string tablet, bool detected, params string[] keys)
    {
        var profile = new Profile
        {
            OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)),
            Tablet = tablet,
        };
        profile.BindingSettings.AuxButtons = new PluginSettingStoreCollection();
        foreach (var k in keys) profile.BindingSettings.AuxButtons.Add(AuxKeyBinding.MakeKeyBinding(k)!);
        var s = new Settings { Profiles = new ProfileCollection { profile } };
        var vm = new TabletDetailViewModel(profile, s, applyAction: _ => Task.FromResult(SettingsApplyOutcome.Live), isDetected: () => detected);
        vm.RefreshDetectionStatus();
        vm.IsAppInFront = () => true;
        return vm;
    }

    private static void Cleanup(string tablet)
    {
        KeyOrderStore.Clear(tablet);
        foreach (var k in AppSettings.Keys("").Where(k => k.Contains(tablet, StringComparison.Ordinal)).ToList()) AppSettings.Remove(k);
    }

    [AvaloniaFact]
    public async Task A_connected_tablet_nobody_has_mapped_is_asked_once_and_Set_up_now_opens_the_sheet()
    {
        var tablet = Tablet();
        try
        {
            var vm = VmFor(tablet, detected: true, "A", "B");
            int asked = 0, opened = 0;
            vm.AskSetUpButtons = () => { asked++; return Task.FromResult(true); };
            vm.ShowKeyMapping = sheet => { opened++; return Task.FromResult<KeyOrderMap?>(new KeyOrderMap(new[] { 1, 0 })); };

            Assert.True(vm.ShouldOfferKeyMapping());
            Assert.True(await vm.OfferKeyMappingAsync());
            Assert.Equal(1, asked);
            Assert.Equal(1, opened);
            Assert.Equal(new[] { 1, 0 }, KeyOrderStore.Load(tablet)!.Keys);

            Assert.False(vm.ShouldOfferKeyMapping());          // mapped now
            Assert.False(await vm.OfferKeyMappingAsync());
            Assert.Equal(1, asked);
        }
        finally { Cleanup(tablet); }
    }

    [AvaloniaFact]
    public async Task Later_is_remembered_and_the_question_does_not_come_again()
    {
        var tablet = Tablet();
        try
        {
            var vm = VmFor(tablet, detected: true, "A", "B");
            int asked = 0, opened = 0;
            vm.AskSetUpButtons = () => { asked++; return Task.FromResult(false); };
            vm.ShowKeyMapping = sheet => { opened++; return Task.FromResult<KeyOrderMap?>(null); };
            Assert.False(await vm.OfferKeyMappingAsync());
            Assert.Equal(1, asked);
            Assert.Equal(0, opened);

            // The same model on a new page (the next start of the app): not asked again.
            var next = VmFor(tablet, detected: true, "A", "B");
            next.AskSetUpButtons = () => { asked++; return Task.FromResult(true); };
            next.ShowKeyMapping = vm.ShowKeyMapping;
            Assert.False(next.ShouldOfferKeyMapping());
            Assert.False(await next.OfferKeyMappingAsync());
            Assert.Equal(1, asked);
        }
        finally { Cleanup(tablet); }
    }

    [AvaloniaFact]
    public void The_question_is_not_asked_when_it_should_not_be()
    {
        var tablet = Tablet();
        try
        {
            Func<Task<bool>> ask = () => Task.FromResult(true);
            Func<KeyMappingViewModel, Task<KeyOrderMap?>> show = _ => Task.FromResult<KeyOrderMap?>(null);

            var notConnected = VmFor(tablet, detected: false, "A", "B");
            notConnected.AskSetUpButtons = ask; notConnected.ShowKeyMapping = show;
            Assert.False(notConnected.ShouldOfferKeyMapping());                 // a tablet that is not plugged in cannot be pressed

            var noKeys = VmFor(tablet, detected: true);
            noKeys.AskSetUpButtons = ask; noKeys.ShowKeyMapping = show;
            Assert.False(noKeys.ShouldOfferKeyMapping());                       // nothing to map

            var behind = VmFor(tablet, detected: true, "A", "B");
            behind.AskSetUpButtons = ask; behind.ShowKeyMapping = show;
            behind.IsAppInFront = () => false;
            Assert.False(behind.ShouldOfferKeyMapping());                       // never while the user is in another app
            behind.IsAppInFront = () => true;
            Assert.True(behind.ShouldOfferKeyMapping());

            var noPage = VmFor(tablet, detected: true, "A", "B");
            Assert.False(noPage.ShouldOfferKeyMapping());                       // no page to ask or to show the sheet

            KeyOrderStore.Save(tablet, new KeyOrderMap(new[] { 0, 1 }));
            var mapped = VmFor(tablet, detected: true, "A", "B");
            mapped.AskSetUpButtons = ask; mapped.ShowKeyMapping = show;
            Assert.False(mapped.ShouldOfferKeyMapping());                       // already mapped
        }
        finally { Cleanup(tablet); }
    }

    [AvaloniaFact]
    public void The_question_says_where_to_find_it_later()
    {
        Assert.Equal("Set up your tablet's buttons?", TabletDetailView.KeyOfferTitle);
        Assert.Contains("\"Set up my buttons\" is on the ExpressKeys page", TabletDetailView.KeyOfferBody);
    }
}
