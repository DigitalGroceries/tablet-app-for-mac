using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>Version 29 (Digital Groceries, 17 Nov): "Setting search" under Home's tab row, from Home's left edge to
/// Advanced's right edge, "Find any setting or function" under it; typing lists matches with their paths; a
/// click opens the page and briefly marks the setting; it searches in the app's language.</summary>
[Collection(nameof(SettingSearchIndexCollection))]
public class SettingSearchBoxTests
{
    /// <summary>The window as on a Mac (the index lists the Mac pages; Settings › System differs on Linux).</summary>
    static (MainWindow W, MainViewModel Vm) Shell()
    {
        using var mac = UiPlatform.PretendMac();
        var w = new MainWindow { Width = 1100, Height = 760 };
        w.Show();
        Pump(w);
        var vm = (MainViewModel)w.DataContext!;
        vm.NavigateCommand.Execute(vm.Dashboard);
        Pump(w);
        return (w, vm);
    }

    static void Close(MainWindow w)
    {
        w.AllowCloseForQuit();
        w.Close();
        Pump(null);
    }

    static void Pump(Window? w)
    {
        for (int i = 0; i < 4; i++)
        {
            Dispatcher.UIThread.RunJobs();
            w?.UpdateLayout();
        }
    }

    [AvaloniaFact]
    public void The_box_runs_from_Home_to_Advanced_under_the_tabs_and_only_on_Home()
    {
        var (w, vm) = Shell();
        try
        {
            var row = w.FindControl<Panel>("SettingSearchRow")!;
            Assert.True(row.IsEffectivelyVisible);
            var tabs = w.FindControl<ItemsControl>("NavTabs")!.GetVisualDescendants().OfType<RadioButton>().Where(r => r.IsVisible).ToList();
            var home = tabs.First();
            var advanced = tabs.Last();
            double X(Visual v) => v.TranslatePoint(default, w)!.Value.X;
            double Y(Visual v) => v.TranslatePoint(default, w)!.Value.Y;
            Assert.InRange(X(row) - X(home), -1, 1);
            Assert.InRange(X(row) + row.Bounds.Width - (X(advanced) + advanced.Bounds.Width), -1, 1);
            Assert.True(Y(row) >= Y(home) + home.Bounds.Height - 0.5, "the box is not under the tabs");

            var box = w.GetVisualDescendants().OfType<TextBox>().First(t => t.Name == "SearchBox");
            Assert.Equal("Setting search", box.PlaceholderText);
            Assert.InRange(box.Bounds.Width - row.Bounds.Width, -1, 1);   // the box itself, not only its row
            var hint = w.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "SearchHint");
            Assert.Equal("Find any setting or function", hint.Text);
            Assert.True(Y(hint) > Y(box), "the hint is not under the box");

            vm.NavigateCommand.Execute(vm.Settings);
            Pump(w);
            Assert.False(row.IsEffectivelyVisible, "the box shows on Settings");
        }
        finally { Close(w); }
    }

    [AvaloniaFact]
    public void Typing_lists_the_matches_with_where_they_are()
    {
        var (w, vm) = Shell();
        try
        {
            vm.Search.Query = "radial";
            Pump(w);
            Assert.True(vm.Search.IsOpen);
            Assert.Contains(vm.Search.Results, e => e.Text == "Show the radial menu" && e.Path == "Settings › System");
            var popup = w.GetVisualDescendants().OfType<Popup>().First(p => p.Name == "ResultsPopup");
            Assert.True(popup.IsOpen);
            var lines = popup.Child!.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains("Show the radial menu", lines);
            Assert.Contains("Settings › System", lines);

            vm.Search.Query = "zzqx";
            Pump(w);
            Assert.True(vm.Search.NothingFound);
            Assert.Contains("Nothing found. Try another word.",
                popup.Child!.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text));
        }
        finally { Close(w); }
    }

    [AvaloniaFact]
    public void Choosing_a_match_opens_its_tab_and_marks_the_words()
    {
        var (w, vm) = Shell();
        try
        {
            vm.Search.Query = "automatic backups";
            Pump(w);
            var hit = vm.Search.Results.First(e => e.Text == "Automatic backups");
            vm.Search.OpenCommand.Execute(hit);
            Pump(w);
            Assert.Same(vm.Settings, vm.CurrentPage);
            Assert.Equal(SettingsTab.System, vm.Settings.SelectedTab);
            Assert.False(vm.Search.IsOpen);
            Assert.Equal("", vm.Search.Query);
            var marks = w.GetVisualDescendants().OfType<AdornerLayer>().SelectMany(l => l.Children).OfType<Border>()
                .Where(b => b.Name == "SearchMark").ToList();
            var mark = Assert.Single(marks);
            var marked = AdornerLayer.GetAdornedElement(mark)!;
            Assert.Contains(marked.GetVisualDescendants().Prepend(marked).OfType<TextBlock>(), t => t.Text == "Automatic backups");
        }
        finally { Close(w); }
    }

    [AvaloniaFact]
    public void A_match_on_a_sub_tab_opens_that_sub_tab()
    {
        var (w, vm) = Shell();
        try
        {
            var hit = SettingSearch.Find("show the json tab").First(e => e.Text == "Show the JSON tab");
            Assert.Equal("settings/Developer/InterfaceSubtab", hit.Route);
            vm.Search.OpenCommand.Execute(hit);
            Pump(w);
            Assert.Equal(SettingsTab.Developer, vm.Settings.SelectedTab);
            var sub = w.GetVisualDescendants().OfType<RadioButton>().First(r => r.Name == "InterfaceSubtab");
            Assert.True(sub.IsChecked);
        }
        finally { Close(w); }
    }

    [AvaloniaFact]
    public void It_searches_in_the_app_language()
    {
        var preference = Localizer.Preference;
        Localizer.SetPreference("de");
        SettingSearch.ResetForTests();
        var (w, vm) = Shell();
        try
        {
            Assert.Equal("Einstellungen suchen", vm.Search.Label);
            vm.Search.Query = "sprache";
            Pump(w);
            Assert.NotEmpty(vm.Search.Results);
            Assert.All(vm.Search.Results, e => Assert.StartsWith("Einstellungen", e.Path));
        }
        finally
        {
            Close(w);
            Localizer.SetPreference(preference);
            SettingSearch.ResetForTests();
        }
    }

    [AvaloniaFact]
    public void Matching_ignores_case_and_accents_and_puts_word_starts_first()
    {
        var index = new List<SettingSearchEntry>
        {
            new("Curva de presión", "Tableta › Lápiz", "tablet/ToolPenButton/PenTab"),
            new("Suavizado de la presión", "Tableta › Lápiz", "tablet/ToolPenButton/PenTab"),
            new("Impresión", "Ajustes › Sistema", "settings/System"),
        };
        var found = SettingSearch.Find("PRESION", index);
        Assert.Equal(new[] { "Curva de presión", "Suavizado de la presión", "Impresión" }, found.Select(e => e.Text));
        Assert.Single(SettingSearch.Find("lapiz curva", index));
        Assert.Empty(SettingSearch.Find("   ", index));
    }
}
