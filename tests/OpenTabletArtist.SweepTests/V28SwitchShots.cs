using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using Xunit;

namespace OpenTabletArtist.SweepTests;

/// <summary>TASKS #28 / #29 screenshots: the ExpressKeys page (All buttons, an Off key) and the Touch ring page for a
/// one-ring and a two-ring tablet. Written to $OTA_SHOTS_DIR (default /workspace/ota-28-shots). Run it with OTA_APPDATA pointing at an empty folder, so no saved On/Off state leaks in.</summary>
public class V28SwitchShots
{
    static string Dir => System.Environment.GetEnvironmentVariable("OTA_SHOTS_DIR") ?? "/workspace/ota-28-shots";

    private static WheelEditor Wheel(int i)
    {
        ButtonBinding Row(string l) => new(0, AuxBinding.Unbound, isOtherBinding: false, otherLabel: "", canEdit: true, applyBinding: null, label: l);
        var ed = new RingModesEditor(i, RingModeBinding.Defaults, true, true, true, null, null, _ => Task.CompletedTask);
        return new WheelEditor(i, "Ring " + (i + 1), false, Row("Clockwise"), Row("Anticlockwise"),
            new List<ButtonBinding> { Row("Centre button") }, 30, 30, 5, null) { RingModes = ed };
    }

    private static (TabletDetailViewModel, TabletDetailView, Window) Open(string tablet, int rings, int keys, string tab)
    {
        _ = new ThemeViewModel();
        var profile = new Profile { Tablet = tablet, OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)) };
        profile.BindingSettings.AuxButtons = new PluginSettingStoreCollection();
        foreach (var k in new[] { "B", "E", "Space", "Control", "Z", "X", "C", "V" }.Take(keys)) profile.BindingSettings.AuxButtons.Add(AuxKeyBinding.MakeKeyBinding(k)!);
        var settings = new OpenTabletDriver.Desktop.Settings { Profiles = new ProfileCollection { profile } };
        var vm = new TabletDetailViewModel(profile, settings, applyAction: _ => Task.FromResult(OtdInterop.SettingsApplyOutcome.Live));
        vm.PenButton1Row = new PenSwitchRowViewModel(PenSwitchKind.PenButton, 1, null, false, (_, _, _) => Task.CompletedTask);
        vm.PenButton2Row = new PenSwitchRowViewModel(PenSwitchKind.PenButton, 2, null, false, (_, _, _) => Task.CompletedTask);
        vm.Wheels = Enumerable.Range(0, rings).Select(Wheel).ToList();
        var view = new TabletDetailView { DataContext = vm };
        var w = new Window { Content = view, Width = 1100, Height = 900 }; w.Show();
        view.FindControl<RadioButton>(tab)!.IsChecked = true;
        Settle(w);
        return (vm, view, w);
    }

    private static void Settle(Window w) { for (int i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); } }

    [AvaloniaFact]
    public async Task Screenshots()
    {
        System.IO.Directory.CreateDirectory(Dir);
        var (vm, _, w) = Open("Wacom Intuos4 PTK-440", 1, 8, "ButtonsTab");
        vm.AuxButtons[1].ToggleKeyCommand.Execute(null);
        vm.AuxButtons[4].ToggleKeyCommand.Execute(null);
        await Task.Delay(100, TestContext.Current.CancellationToken); Settle(w);
        w.CaptureRenderedFrame()!.Save($"{Dir}/expresskeys-all-on-two-keys-off.png");
        vm.AuxButtonsEnabled = false;
        await Task.Delay(100, TestContext.Current.CancellationToken); Settle(w);
        w.CaptureRenderedFrame()!.Save($"{Dir}/expresskeys-all-off.png");
        w.Close();

        var (_, _, w1) = Open("Wacom Intuos4 PTK-440", 1, 8, "WheelsTab");
        w1.CaptureRenderedFrame()!.Save($"{Dir}/touchring-one-ring-intuos4.png"); w1.Close();
        var (_, _, w2) = Open("Wacom Cintiq 24HD", 2, 8, "WheelsTab");
        w2.CaptureRenderedFrame()!.Save($"{Dir}/touchring-two-rings-cintiq24hd.png"); w2.Close();
    }
}
