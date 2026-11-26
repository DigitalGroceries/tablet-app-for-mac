using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
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

/// <summary>TASKS #198 (Digital Groceries 22 Nov 2026, screenshot 13:30 and his words: "The mismatch of Bulgarian and English came after I
/// repetedly switched between the two languages ... it did not redraw all text in the changed language GUI before I have
/// restarted the app."): every text on the Pen page follows a language change at once.</summary>
public class V29LanguageSwitchTests
{
    private static (TabletDetailViewModel Vm, Window Window) Open()
    {
        _ = new ThemeViewModel();
        var menu = RadialMenuDefaults.Create(Localizer.Te);
        var profile = new Profile { Tablet = "Lang " + Guid.NewGuid().ToString("N")[..8], OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)) };
        profile.BindingSettings.PenButtons = new PluginSettingStoreCollection { HoldKeyboardBinding.Make(), RadialMenuBinding.Make(menu) };
        profile.BindingSettings.AuxButtons = new PluginSettingStoreCollection();
        profile.BindingSettings.EraserButton = PenSwitchBinding.MakeAdaptiveBinding(PenSwitchKind.Eraser, 1);   // factory: "Default (erase)"
        var settings = new Settings { Profiles = new ProfileCollection { profile } };
        var vm = new TabletDetailViewModel(profile, settings, applyAction: _ => System.Threading.Tasks.Task.FromResult(SettingsApplyOutcome.Live));
        vm.ShowHeader = false;
        var window = new Window { Content = new TabletDetailView { DataContext = vm }, Width = 1046, Height = 1365 };
        window.Show();
        Settle(window);
        return (vm, window);
    }

    private static void Settle(Window w) { for (int i = 0; i < 6; i++) { Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); } }

    private static string[] Texts(TabletDetailViewModel vm) =>
    [
        vm.PenEraserRow!.CurrentPlainLabel ?? "",
        vm.PenButton1Row!.CurrentPlainLabel ?? "",
        vm.PenButton2Row!.CurrentPlainLabel ?? "",
        vm.SelectedMovement.Title,
        vm.MovementOptions[0].Title,
        vm.RadialSpotLine ?? "",
        vm.PaletteSpotLine ?? "",
    ];

    [AvaloniaFact]
    public void Item198_SwitchingBulgarianAndEnglishBackAndForth_RedrawsEveryPenPageText()
    {
        Localizer.SetPreference("en");
        var (vm, w) = Open();
        try
        {
            var en = Texts(vm);
            for (int round = 0; round < 3; round++)
            {
                Localizer.SetPreference("bg"); Settle(w);
                var bg = Texts(vm);
                for (int i = 0; i < en.Length; i++)
                    Assert.True(bg[i] != en[i], $"text {i} stayed English after switching to Bulgarian (round {round}): '{bg[i]}'");
                Localizer.SetPreference("en"); Settle(w);
                Assert.Equal(en, Texts(vm));
            }
            Assert.Equal("Default (erase)", en[0]);
            Assert.Equal("Right click; hold for the keyboard", en[1]);
            Assert.Equal("Normal (Absolute)", en[3]);
        }
        finally { Localizer.SetPreference("en"); w.Close(); }
    }

    [AvaloniaFact]
    public void Item182_ModifiedToAndTheDefaultButtonLabelsFollowTheLanguage()
    {
        Localizer.SetPreference("fr");
        try
        {
            var row = new PenSwitchRowViewModel(PenSwitchKind.PenButton, 1, HoldKeyboardBinding.Make(), true, (_, _, _) => System.Threading.Tasks.Task.CompletedTask);
            Assert.Equal("Modifié en Clic droit ; maintenir pour le clavier", row.ModifiedLabel);
            Localizer.SetPreference("en");
            Assert.Equal("Modified to Right click; hold for the keyboard", row.ModifiedLabel);
            Assert.Equal("Default (right click)", Localizer.Te("tablet.pen.default_right_click", "x"));
            var xaml = System.IO.File.ReadAllText(System.IO.Path.Combine(RepoRoot(), "OpenTabletArtist/Views/PenSwitchCard.axaml"));
            Assert.DoesNotContain("StringFormat='Modified to", xaml);
        }
        finally { Localizer.SetPreference("en"); }
    }

    [AvaloniaFact]
    public void Item183_LongTranslationsFit_TheDefaultButtonNeverCoversOffOrLarge_AndKeyboardSettingsIsNotCut()
    {
        foreach (var lang in new[] { "fr", "ru", "bg", "de", "es", "it", "sv", "pt-BR", "hi", "sw", "en" })
        {
            Localizer.SetPreference(lang);
            var (vm, w) = Open();
            try
            {
                var view = (TabletDetailView)w.Content!;
                var off = view.FindControl<TextBlock>("PenDoubleClickOff")!;
                var def = view.FindControl<Button>("PenDoubleClickDefaultButton")!;
                var large = view.FindControl<TextBlock>("PenDoubleClickLarge")!;
                Rect R(Visual v) => new(v.TranslatePoint(new Point(0, 0), w) ?? default, v.Bounds.Size);
                Assert.True(R(off).Right <= R(def).Left + 0.5, $"{lang}: Default covers Off ({R(off).Right} > {R(def).Left})");
                Assert.True(R(def).Right <= R(large).Left + 0.5, $"{lang}: Default covers Large");
                var dtext = view.FindControl<TextBlock>("PenDoubleClickDefault")!;
                Assert.True(dtext.Text == Localizer.Te("tablet.pen.double_click_default", "Default") || dtext.Text == Localizer.Te("tablet.pen.default", "Default"),
                            $"{lang}: Default button text is '{dtext.Text}'");
                var link = view.FindControl<Button>("KeyboardSettingsLink")!;
                var ltext = view.FindControl<TextBlock>("KeyboardSettingsLinkText")!;
                Assert.Equal(Localizer.Te("tablet.pen.keyboard_settings_link", "Keyboard settings…"), ltext.Text);
                var shown = ltext.TranslatePoint(new Point(ltext.Bounds.Width, 0), link);
                // the text (as drawn, after the Viewbox shrinks it) ends inside the button
                var vb = (Viewbox)ltext.Parent!;
                Assert.True(vb.Bounds.Width <= link.Bounds.Width, $"{lang}: keyboard settings text wider than its button");
            }
            finally { w.Close(); }
        }
        Localizer.SetPreference("en");
    }

    private static string RepoRoot()
    {
        var d = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !System.IO.File.Exists(System.IO.Path.Combine(d.FullName, "global.json"))) d = d.Parent;
        return d!.FullName;
    }
}
