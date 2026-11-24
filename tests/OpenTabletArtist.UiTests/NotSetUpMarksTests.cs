using OpenTabletArtist.Services;
using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OtdInterop;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>V30 #200 B2 (Zak 23 Nov 2026: users don't know "their tablets buttons, wheels and strips wouldnt do anything until
/// they are setup"; team vote: do this first): a key with nothing on it says "Not set up" and offers to choose, instead of a
/// quiet "Do nothing". A key that does something, and a key turned Off, show no mark.</summary>
public class NotSetUpMarksTests
{
    private static Settings SettingsFor(string tablet, params string?[] keys)
    {
        var profile = new Profile
        {
            OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)),
            Tablet = tablet,
        };
        profile.BindingSettings.AuxButtons = new PluginSettingStoreCollection();
        foreach (var k in keys) profile.BindingSettings.AuxButtons.Add(k == null ? null! : AuxKeyBinding.MakeKeyBinding(k)!);
        return new Settings { Profiles = new ProfileCollection { profile } };
    }

    [AvaloniaFact]
    public void A_key_with_nothing_on_it_is_marked_Not_set_up_and_offers_to_choose()
    {
        var s = SettingsFor("TestTablet-NotSetUp-" + Guid.NewGuid().ToString("N")[..8], "A", null, "C");
        var vm = new TabletDetailViewModel(s.Profiles[0], s, applyAction: _ => Task.FromResult(SettingsApplyOutcome.Live));
        Assert.False(vm.AuxButtons[0].IsNotSetUp);
        Assert.True(vm.AuxButtons[1].IsNotSetUp);
        Assert.False(vm.AuxButtons[1].ShowSummary);
        Assert.Equal("Not set up", vm.AuxButtons[1].NotSetUpText);
        Assert.Equal("Choose what it does…", vm.AuxButtons[1].ChooseText);
        Assert.False(vm.AuxButtons[2].IsNotSetUp);

        // the card shows the mark and a Choose button that opens the editor (the same command as Edit…)
        var view = new TabletDetailView { DataContext = vm };
        var w = new Window { Content = view, Width = 1300, Height = 1100 }; w.Show();
        if (view.FindControl<Avalonia.Controls.Primitives.ToggleButton>("ToolFunctionsButton") is { } f) f.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        view.FindControl<RadioButton>("ButtonsTab")!.IsChecked = true;
        for (int i = 0; i < 6; i++) { Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); }
        var choose = view.GetVisualDescendants().OfType<Button>().Where(b => b.Name == "NotSetUpChoose" && b.IsEffectivelyVisible).ToList();
        Assert.Single(choose);
        Assert.Equal("Choose what it does…", choose[0].Content);
        Assert.Same(vm.AuxButtons[1].EditCommand, choose[0].Command);
        Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Not set up" && t.IsEffectivelyVisible);
        w.Close();
    }

    [AvaloniaFact]
    public void Home_says_how_many_keys_are_not_set_up_and_links_to_the_ExpressKeys_page()
    {
        var s = SettingsFor("TestTablet-NotSetUp-" + Guid.NewGuid().ToString("N")[..8], "A", null, null, "D");
        Assert.Equal(2, MainViewModel.KeysNotSetUp(s.Profiles[0]));
        var opened = 0;
        var card = new TabletOverviewItemViewModel("T", true, "Detected", "CONNECTED", () => { }, () => Task.CompletedTask,
            keysNotSetUp: 2, openExpressKeys: () => opened++);
        Assert.True(card.HasKeysNotSetUp);
        Assert.Equal("2 ExpressKeys not set up: Choose what they do or restore to factory settings", card.KeysNotSetUpText);   // Zak 24 Nov (L6.1)
        Assert.Equal("Forget this tablet", card.ForgetText);                                                                 // L6.2
        card.OpenExpressKeysCommand.Execute(null);
        Assert.Equal(1, opened);
        var one = new TabletOverviewItemViewModel("T", true, "Detected", null, () => { }, () => Task.CompletedTask, keysNotSetUp: 1);
        Assert.Equal("1 ExpressKey not set up: Choose what it does or restore to factory settings", one.KeysNotSetUpText);
        var none = new TabletOverviewItemViewModel("T", true, "Detected", null, () => { }, () => Task.CompletedTask);
        Assert.False(none.HasKeysNotSetUp);
        Assert.Equal(0, MainViewModel.KeysNotSetUp(SettingsFor("TestTablet-NotSetUp-" + Guid.NewGuid().ToString("N")[..8], "A", "B").Profiles[0]));
    }

    [AvaloniaFact]
    public void A_bound_key_or_one_set_in_the_editor_loses_the_mark()
    {
        var s = SettingsFor("TestTablet-NotSetUp-" + Guid.NewGuid().ToString("N")[..8], (string?)null);
        var vm = new TabletDetailViewModel(s.Profiles[0], s, applyAction: _ => Task.FromResult(SettingsApplyOutcome.Live));
        var key = vm.AuxButtons[0];
        Assert.True(key.IsNotSetUp);
        string? changed = null;
        key.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ButtonBinding.IsNotSetUp)) changed = e.PropertyName; };
        key.ShowQuietly(new AuxBinding(AuxKind.Keyboard, new AuxCombo(false, false, false, "Z", true), AuxKeyBinding.None, AuxKeyBinding.None));
        Assert.False(key.IsNotSetUp);
        Assert.True(key.ShowSummary);
        Assert.Equal(nameof(ButtonBinding.IsNotSetUp), changed);
    }

    [AvaloniaFact]
    public void The_marks_can_be_seen_bold_yellow_text_and_real_buttons()
    {
        // Zak 23 Nov 2026 (test build 1): the Home line was small dark text he "would have never seen let alone try to click it"
        // ("That deserves a huge button"); on the cards: "These warning texts must be in bold yellow".
        var s = SettingsFor("TestTablet-NotSetUp-" + Guid.NewGuid().ToString("N")[..8], "A", null);
        var vm = new TabletDetailViewModel(s.Profiles[0], s, applyAction: _ => Task.FromResult(SettingsApplyOutcome.Live));
        var view = new TabletDetailView { DataContext = vm };
        var w = new Window { Content = view, Width = 1300, Height = 1100 }; w.Show();
        if (view.FindControl<Avalonia.Controls.Primitives.ToggleButton>("ToolFunctionsButton") is { } f) f.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        view.FindControl<RadioButton>("ButtonsTab")!.IsChecked = true;
        for (int i = 0; i < 6; i++) { Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); }
        var mark = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Not set up" && t.IsEffectivelyVisible);
        Assert.Equal(Avalonia.Media.FontWeight.Bold, mark.FontWeight);
        Assert.True(Avalonia.Application.Current!.TryFindResource("AttentionTextBrush", out var yellow));
        Assert.Equal(((Avalonia.Media.ISolidColorBrush)yellow!).Color, ((Avalonia.Media.ISolidColorBrush)mark.Foreground!).Color);
        var choose = view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "NotSetUpChoose" && b.IsEffectivelyVisible);
        Assert.NotEqual(Avalonia.Media.Colors.Transparent, (choose.Background as Avalonia.Media.ISolidColorBrush)?.Color ?? Avalonia.Media.Colors.Transparent);
        Assert.True(choose.Bounds.Height >= 26, $"Choose is {choose.Bounds.Height} high");
        w.Close();
    }

    [AvaloniaFact]
    public void On_the_light_steps_the_mark_is_dark_ink_on_a_yellow_highlight()
    {
        // 23 Nov 2026, the fresh check of test build 2's pictures: on the light look the "readable" yellow had turned dark brown.
        var before = LightnessService.Saved;
        try
        {
            LightnessService.Set(8);
            var s = SettingsFor("TestTablet-NotSetUp-" + Guid.NewGuid().ToString("N")[..8], "A", null);
            var vm = new TabletDetailViewModel(s.Profiles[0], s, applyAction: _ => Task.FromResult(SettingsApplyOutcome.Live));
            var view = new TabletDetailView { DataContext = vm };
            var w = new Window { Content = view, Width = 1300, Height = 1100 }; w.Show();
            if (view.FindControl<Avalonia.Controls.Primitives.ToggleButton>("ToolFunctionsButton") is { } f) f.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            view.FindControl<RadioButton>("ButtonsTab")!.IsChecked = true;
            for (int i = 0; i < 6; i++) { Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); }
            var mark = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Not set up" && t.IsEffectivelyVisible);
            var fill = ((Avalonia.Media.ISolidColorBrush)((Border)mark.Parent!).Background!).Color;
            var ink = ((Avalonia.Media.ISolidColorBrush)mark.Foreground!).Color;
            Assert.Equal(LightnessService.Attention, fill);
            Assert.True(LightnessService.Contrast(ink, fill) >= 4.5, $"ink on yellow {LightnessService.Contrast(ink, fill):0.0}:1");
            Assert.Equal(Avalonia.Media.FontWeight.Bold, mark.FontWeight);
            w.Close();
        }
        finally { LightnessService.Set(before); }
    }
}
