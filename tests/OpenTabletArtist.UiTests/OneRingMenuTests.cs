using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletArtist.Controls;
using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>V29 #158 (Zak 20 Nov): one Touch Ring switch and one menu on the page: no "Touch Ring modes" switch, no plain rows, no
/// "On" tick box per mode; the centre button has its own menu; a ring that used the plain rows opens as mode 1.</summary>
public class OneRingMenuTests
{
    private static AuxCombo K(string key, bool cmd = false) => new(false, false, false, key, cmd);

    private static async Task PumpUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "UI did not settle.");
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static (TabletDetailViewModel Vm, TabletDetailView View, Window Window, Profile Profile) Open(WheelBindingSettings ring)
    {
        _ = new ThemeViewModel();
        var profile = new Profile { Tablet = "One ring menu " + Guid.NewGuid().ToString("N")[..8],
            OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)) };
        profile.BindingSettings.WheelBindings = new List<WheelBindingSettings> { ring };
        var settings = new Settings { Profiles = new ProfileCollection { profile } };
        var vm = new TabletDetailViewModel(profile, settings, applyAction: _ => Task.FromResult(OtdInterop.SettingsApplyOutcome.Live));
        var view = new TabletDetailView { DataContext = vm };
        var window = new Window { Content = view, Width = 1200, Height = 1100 };
        window.Show();
        view.FindControl<RadioButton>("WheelsTab")!.IsChecked = true;
        for (int i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
        return (vm, view, window, profile);
    }

    private static T? Named<T>(Control root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == name);

    [AvaloniaFact]
    public async Task APlainRing_OpensAsModeOne_DoingTheSame_WithTheCentreMenuAndNoModesSwitch()
    {
        var ring = new WheelBindingSettings
        {
            ClockwiseRotation = AuxKeyBinding.MakeBinding(K("Equal", cmd: true)),
            CounterClockwiseRotation = AuxKeyBinding.MakeBinding(K("Minus", cmd: true)),
            WheelButtons = new PluginSettingStoreCollection(new[] { AuxKeyBinding.MakeBinding(K("D1"))! }),
            ClockwiseActivationThreshold = 5, CounterClockwiseActivationThreshold = 5,
        };
        var (vm, view, window, profile) = Open(ring);
        try
        {
            var w = profile.BindingSettings.WheelBindings[0];
            await PumpUntil(() => RingModeBinding.IsActive(w) && vm.Wheels.Count == 1 && vm.Wheels[0].RingModes?.IsOn == true);
            Assert.Equal("Equal", RingModeBinding.Read(w)[0].Clockwise.Key);          // mode 1 = the plain keys
            Assert.Equal("D1", RingModeBinding.ReadCentre(w).Binding.Combo.Key);      // the centre button kept its key
            for (int i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }

            Assert.Null(Named<ToggleSwitch>(view, "RingModesSwitch"));                // no Touch Ring modes switch
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<CheckBox>(), c => (c.Content as string) == "On");   // no On tick box
            var centre = Named<ComboBox>(view, "RingCentreBox");
            Assert.NotNull(centre);
            Assert.True(centre!.IsEffectivelyVisible);
            Assert.Equal(RingModesEditor.CentreKeysIndex, centre.SelectedIndex);
            var keys = Named<KeyCaptureBox>(view, "RingCentreKeys");
            Assert.True(keys!.IsEffectivelyVisible);
            Assert.False(string.IsNullOrEmpty(keys.Text));
            Assert.Contains("mode 1", Named<TextBlock>(view, "RingModesSummary")!.Text);
            // the modes' own shortcut boxes, in place of a window
            Assert.NotEmpty(view.GetVisualDescendants().OfType<KeyCaptureBox>().Where(b => b.Name != "RingCentreKeys" && b.IsEffectivelyVisible));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task PickingNextMode_InTheCentreMenu_SwitchesTheModes()
    {
        var ring = new WheelBindingSettings
        {
            WheelButtons = new PluginSettingStoreCollection(new[] { (PluginSettingStore)null! }),
            ClockwiseActivationThreshold = 5, CounterClockwiseActivationThreshold = 5,
        };
        RingModeBinding.ApplyLayout(ring, RingModeBinding.Defaults, RingCentre.Nothing);   // mode 1 only, centre does nothing
        var (vm, view, window, profile) = Open(ring);
        try
        {
            var editor = vm.Wheels[0].RingModes!;
            Assert.Equal(RingModesEditor.CentreNothingIndex, editor.CentreIndex);
            Assert.False(editor.Rows[1].IsReachable);
            editor.CentreIndex = RingModesEditor.CentreNextIndex;
            var w = profile.BindingSettings.WheelBindings[0];
            await PumpUntil(() => RingModeBinding.CentreIsNext(w));
            Assert.Equal(4, RingModeBinding.Read(w).Count);
        }
        finally { window.Close(); }
    }
}
