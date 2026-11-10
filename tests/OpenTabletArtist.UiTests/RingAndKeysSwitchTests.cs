using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>TASKS #28 / #29: "All buttons" and the Touch Ring switch read "Off [switch] On", and the ring's label follows
/// the tablet in Wacom's words (Zak, 10 Nov 2026): "Touch Ring" for one ring (Intuos4 PTK-440), "Touch Rings" for two
/// (Cintiq 24HD), "Touch Strip" / "Touch Strips" for strip tablets. With two, each card has its own On/Off.</summary>
public class RingAndKeysSwitchTests
{
    private static WheelEditor Wheel(int i) => Wheel(i, strip: false);

    private static WheelEditor Wheel(int i, bool strip, bool showOnOff = false, bool canToggle = true, bool isOn = true)
    {
        ButtonBinding Row(string l) => new(0, AuxBinding.Unbound, isOtherBinding: false, otherLabel: "", canEdit: true, applyBinding: null, label: l);
        var ed = new RingModesEditor(i, RingModeBinding.Defaults, true, true, true, null, null, _ => Task.CompletedTask);
        return new WheelEditor(i, "Touch Ring " + (i + 1), true, Row("Clockwise"), Row("Anticlockwise"),
            new List<ButtonBinding> { Row("Centre button") }, 30, 30, 5, null, isStrip: strip, isOn: isOn,
            setOn: _ => Task.CompletedTask)
        { RingModes = ed, ShowOnOff = showOnOff, CanToggleOn = canToggle };
    }

    private static (TabletDetailViewModel, TabletDetailView, Window) Open(string tablet, int rings, string tab)
    {
        _ = new ThemeViewModel();
        var profile = TestPens.Arm(new Profile { Tablet = tablet, OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)) });
        profile.BindingSettings.AuxButtons = new PluginSettingStoreCollection();
        profile.BindingSettings.AuxButtons.Add(AuxKeyBinding.MakeKeyBinding("B")!);
        var settings = new OpenTabletDriver.Desktop.Settings { Profiles = new ProfileCollection { profile } };
        var vm = new TabletDetailViewModel(profile, settings, applyAction: _ => Task.FromResult(OtdInterop.SettingsApplyOutcome.Live));
        vm.PenButton1Row = new PenSwitchRowViewModel(PenSwitchKind.PenButton, 1, null, false, (_, _, _) => Task.CompletedTask);
        vm.PenButton2Row = new PenSwitchRowViewModel(PenSwitchKind.PenButton, 2, null, false, (_, _, _) => Task.CompletedTask);
        vm.Wheels = Enumerable.Range(0, rings).Select(Wheel).ToList();
        var view = new TabletDetailView { DataContext = vm };
        var w = new Window { Content = view, Width = 1100, Height = 1100 }; w.Show();
        view.FindControl<RadioButton>(tab)!.IsChecked = true;
        for (int i = 0; i < 3; i++) { Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); }
        return (vm, view, w);
    }

    /// <summary>The row's children in order: label, "Off", the switch, "On".</summary>
    private static string[] Row(TabletDetailView view, string labelName)
    {
        var label = view.FindControl<TextBlock>(labelName)!;
        var panel = (StackPanel)label.Parent!;
        return panel.Children.Select(c => c switch { TextBlock t => t.Text ?? "", ToggleSwitch => "[switch]", _ => "?" }).ToArray();
    }

    [AvaloniaFact]
    public void OneRing_ShowsTouchRing_WithOffOnEitherSideOfTheSwitch()
    {
        var (vm, view, w) = Open("Wacom Intuos4 PTK-440", 1, "WheelsTab");
        Assert.Equal(new[] { "Touch Ring", "Off", "[switch]", "On" }, Row(view, "LblEnableWheels"));
        Assert.Equal("Turn Touch Ring Off to pause the ring. Your settings are kept.", view.FindControl<TextBlock>("LblRingAboutEnable")!.Text);
        w.Close();
    }

    [AvaloniaFact]
    public void TwoRings_ShowTouchRings_WithOffOnEitherSideOfTheSwitch()
    {
        var (vm, view, w) = Open("Wacom Cintiq 24HD", 2, "WheelsTab");
        Assert.Equal(new[] { "Touch Rings", "Off", "[switch]", "On" }, Row(view, "LblEnableWheels"));
        Assert.Equal("Turn Touch Rings Off to pause every ring, or use each ring's own On/Off. Your settings are kept.",
            view.FindControl<TextBlock>("LblRingAboutEnable")!.Text);
        // and no extra button next to the switch (Zak, 10 Nov: there is none)
        var panel = (StackPanel)view.FindControl<TextBlock>("LblEnableWheels")!.Parent!;
        Assert.Empty(panel.Children.Where(c => c is Button and not ToggleSwitch));
        w.Close();
    }

    [AvaloniaFact]
    public void TheLabel_FollowsTheNumberOfRings_WhenTheTabletChanges()
    {
        var (vm, view, w) = Open("Wacom Intuos4 PTK-440", 1, "WheelsTab");
        vm.Wheels = new[] { Wheel(0), Wheel(1) }.ToList();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Touch Rings", view.FindControl<TextBlock>("LblEnableWheels")!.Text);
        vm.Wheels = new[] { Wheel(0) }.ToList();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Touch Ring", view.FindControl<TextBlock>("LblEnableWheels")!.Text);
        w.Close();
    }

    [AvaloniaFact]
    public void StripTablets_SayTouchStrip_OrTouchStrips()
    {
        var (vm, view, w) = Open("Wacom Cintiq 22HD", 1, "WheelsTab");
        vm.Wheels = new[] { Wheel(0, strip: true) }.ToList();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { "Touch Strip", "Off", "[switch]", "On" }, Row(view, "LblEnableWheels"));
        Assert.Equal("Turn Touch Strip Off to pause the strip. Your settings are kept.", view.FindControl<TextBlock>("LblRingAboutEnable")!.Text);
        vm.Wheels = new[] { Wheel(0, strip: true), Wheel(1, strip: true) }.ToList();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Touch Strips", view.FindControl<TextBlock>("LblEnableWheels")!.Text);
        Assert.DoesNotContain("wheel", vm.WheelEnableLabel, System.StringComparison.OrdinalIgnoreCase);
        w.Close();
    }

    /// <summary>The Off / switch / On row inside each ring card (its own On/Off).</summary>
    private static List<string[]> CardSwitchRows(TabletDetailView view) =>
        view.GetVisualDescendants().OfType<ToggleSwitch>()
            .Where(t => t.Name == "RingOwnSwitch" && ((Control)t.Parent!).IsVisible)
            .Select(t => ((StackPanel)t.Parent!).Children
                .Select(c => c switch { TextBlock tb => tb.Text ?? "", ToggleSwitch => "[switch]", _ => "?" }).ToArray())
            .ToList();

    [AvaloniaFact]
    public void TwoRings_EachCardHasItsOwnOffOnSwitch_GreyedWhileTheMainSwitchIsOff()
    {
        var (vm, view, w) = Open("Wacom Cintiq 24HD", 2, "WheelsTab");
        vm.Wheels = new[] { Wheel(0, false, showOnOff: true), Wheel(1, false, showOnOff: true, isOn: false) }.ToList();
        for (int i = 0; i < 3; i++) { Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); }
        var rows = CardSwitchRows(view);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(new[] { "Off", "[switch]", "On" }, r));
        var switches = view.GetVisualDescendants().OfType<ToggleSwitch>().Where(t => t.Name == "RingOwnSwitch").ToList();
        Assert.Equal(new bool?[] { true, false }, switches.Select(s => s.IsChecked).ToArray());
        Assert.All(switches, s => Assert.True(s.IsEnabled));

        // main switch Off: each card's switch is greyed and keeps its own state
        vm.Wheels = new[] { Wheel(0, false, showOnOff: true, canToggle: false), Wheel(1, false, showOnOff: true, canToggle: false, isOn: false) }.ToList();
        for (int i = 0; i < 3; i++) { Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); }
        switches = view.GetVisualDescendants().OfType<ToggleSwitch>().Where(t => t.Name == "RingOwnSwitch").ToList();
        Assert.All(switches, s => Assert.False(s.IsEnabled));
        Assert.Equal(new bool?[] { true, false }, switches.Select(s => s.IsChecked).ToArray());
        w.Close();
    }

    [AvaloniaFact]
    public void OneRing_HasNoSwitchOfItsOwn()
    {
        var (vm, view, w) = Open("Wacom Intuos4 PTK-440", 1, "WheelsTab");
        Assert.Empty(CardSwitchRows(view));
        w.Close();
    }

    [AvaloniaFact]
    public void ExpressKeys_AllButtons_HasOffOnEitherSideOfTheSwitch_AndAnOnOffItemOnEachKeyMenu()
    {
        var (vm, view, w) = Open("Wacom Intuos4 PTK-440", 1, "ButtonsTab");
        Assert.Equal(new[] { "All buttons", "Off", "[switch]", "On" }, Row(view, "LblEnableButtons"));
        Assert.True(vm.AuxButtons[0].ShowKeyToggle);
        var about = view.FindControl<Control>("AboutExpressKeys")!;
        var texts = about.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.DoesNotContain(texts, t => t != null && t.Contains("Enable buttons"));
        w.Close();
    }

    static string I18nDir()
    {
        var d = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "OpenTabletArtist", "Assets", "i18n"))) d = d.Parent;
        Assert.NotNull(d);
        return Path.Combine(d!.FullName, "OpenTabletArtist", "Assets", "i18n");
    }

    [Fact]
    public void EveryLocale_HasTheSwitchTexts_Translated()
    {
        string[] keys =
        {
            "tablet.expresskeys.enable", "tablet.switch.off", "tablet.switch.on", "tablet.expresskeys.key_turn_off",
            "tablet.expresskeys.key_turn_on", "tablet.expresskeys.key_needs_all", "tablet.expresskeys.enable_note",
            "tablet.expresskeys.key_note", "tablet.ring.enable", "tablet.ring.enable_all", "tablet.ring.about_enable",
            "tablet.ring.about_enable_all", "tablet.ring.needs_all", "tablet.ring.n",
            "tablet.strip.about_enable", "tablet.strip.about_enable_all", "tablet.strip.needs_all",
        };
        var dir = I18nDir();
        var files = Directory.GetFiles(dir, "*.json");
        Assert.Equal(8, files.Length);
        var en = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(dir, "en.json")))!;
        foreach (var f in files)
        {
            var t = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(f))!;
            foreach (var k in keys)
            {
                Assert.True(t.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v), $"{Path.GetFileName(f)} lacks {k}");
                if (!f.EndsWith("en.json")) Assert.NotEqual(en[k], v);
                Assert.DoesNotContain("Enable buttons", v);
                Assert.DoesNotContain("Enable wheels", v);
            }
            if (f.EndsWith("es.json"))   // es.json says "tableta" and quotes labels with «»
                foreach (var k in keys) Assert.DoesNotMatch(@"\btablet\b", t[k]);
        }
    }
}
